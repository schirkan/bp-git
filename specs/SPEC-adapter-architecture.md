# SPEC — BP-Git-Adapter-Architektur

Stand: 2026-08-10 (Martin-Anforderung 16:13 GMT+2)  
Status: draft v1  
Bezieht sich auf: [SPEC-target-environment.md](./SPEC-target-environment.md), [context/bp-database-schema.md](../context/bp-database-schema.md)

## Ziel

Git-konformer Read/Write-Adapter für Blue Prism (BP) v7.5. XML-Repräsentationen von Processes / Objects / Releases werden im Dateisystem sicht- und editierbar; Versionsverwaltung über Standard-Git-Befehle (`git diff`, `git commit`, `git log`, `git status`).

## High-Level-Architektur

<pre>
  ┌────────────────────────────────────────────────────────────────────┐
  │  Developer Workstation (Windows 11 / .NET 10)                       │
  │                                                                    │
  │   ┌──────────────┐  ┌──────────────────┐  ┌──────────────────────┐ │
  │   │  bp-git Repo  │◄─┤  Adapter CLI      │◄─┤  BP DB               │ │
  │   │  *.bpprocess  │  │  (dotnet bpgit)   │  │  (localdb)\…LocalDB  │ │
  │   │  *.bpobject   │  └────────┬─────────┘  └──────────────────────┘ │
  │   │  *.bprelease  │           │                                    │
  │   └──────┬───────┘           │  Signatur-Check                     │
  │          │                   │  + SqlClient (Win-Auth)             │
  │          │                   │  + Dapper-Mapping                   │
  │   ┌──────▼───────┐  ┌────────▼─────────┐                            │
  │   │  git CLI /    │  │  BP-Lizenz         │                            │
  │   │  VS Code      │  │  (signiert, XAdES)│                            │
  │   └──────────────┘  └──────────────────┘                            │
  └────────────────────────────────────────────────────────────────────┘
</pre>

## Komponenten (.NET, C# 13)

### 1. Adapter-CLI (`BPGit.Cli`)

`dotnet`-basiertes Konsolen-Tool mit Subcommands analog zu Git:

| Subcommand | Funktion |
|---|---|
| `bpgit init` | Initialisiert bp-git-Worktree aus BP-Instanz (konfiguriert `config.toml`) |
| `bpgit status` | Zeigt Diffs Worktree ↔ DB |
| `bpgit pull` | Exportiert aktuellen BP-Stand → Worktree |
| `bpgit commit` | Worktree → DB-Import (Round-Trip-Write) — explizit `--force`-Flag |
| `bpgit log` | Commit-History mit BP-Snapshots (manuell + auto-Pull-Snapshots) |
| `bpgit diff` | Unified-Diff Worktree vs. DB-Snapshot |

Package: `BPGit.Cli` mit `<OutputType>Exe</OutputType>`, referenziert die anderen Packages.

### 2. Data-Layer (`BPGit.Data`)

POCOs für die Kern-BPA*-Tabellen, Dapper-Mapping:

| DTO | Quell-Tabellen |
|---|---|
| `Process` | `BPAProcess`, `BPAProcessAttribute`, `BPAProcessBackup` |
| `ProcessDependency` | 9 `BPAProcess*Dependency`-Tabellen |
| `ProcessEnvVar` | `BPAProcessEnvVar` |
| `ProcessLock` | `BPAProcessLock` |
| `Environment` | `BPAEnvironment`, `BPAEnvironmentVar` |
| `WorkQueue` | `BPAWorkQueue`, `BPAWorkQueueFilter`, `BPAWorkQueueItem` |
| `Release` | `BPARelease`, `BPAReleaseEntry` |
| `Package` | `BPAPackage`, `BPAPackageProcess` |

### 3. XML-Serializer (`BPGit.Format`)

Kanonisches Mapping BP-DB-Zeilen ↔ XML-Repräsentation:

- **Input:** DTOs aus `BPGit.Data`
- **Output:** `*.bpprocess.xml`, `*.bpobject.xml`, `*.bprelease.xml` (oder nach BP-Convention; TBD nach Sample)
- **Validierung:** Self-Consistency-Check (`XmlReader` strict, dann Re-Parse und Property-Bag-Vergleich)
- **Diff-Format:** Unified-Diff (Line-basierter XML-Diff mit Whitespace-Normalisierung)

### 4. License-Guard (`BPGit.License`)

Prüft **vor jedem BP-DB-Zugriff** die BP-Lizenz-Datei — **Compliance-Gate**, nicht Auth-Mechanismus. Die SQL-Verbindung selbst läuft separat über Windows-Integrated-Auth oder SQL-Auth (siehe `config.toml`).

**Blockierende Checks** (verhindern Adapter-Start bzw. ersten DB-Zugriff):

| # | Check | Quelle | Failure-Mode |
|---|---|---|---|
| G1 | Datei existiert | `config.toml.license_path` | Exit 2 + Hinweis-Pfad |
| G2 | XML well-formed, Pflichtfelder vorhanden (`type`, `licensee`, `starts`, `expires`, `maxprocesses`, `maxresources`, `maxconcurrentsessions`) | XDocument.Parse | Exit 3 + Diagnose |
| G3 | `expires` > heute | ISO-Parse | Exit 4 + Datum |
| G4 | License-Typ in Whitelist (`allowed_license_types`, default `["education","perpetual"]`) — `"trial"` und unbekannte Typen werden geblockt | XML `<type>` | Exit 5 |
| G5 | Operational-Footprint ≤ Limits (`BPAProcess` ≤ `maxprocesses`, `BPAResource` ≤ `maxresources`) — gesteuert durch `footprint_policy`: `"block"` (Exit 6) oder `"warn"` (nur log) | `SELECT COUNT(*)` + max-Felder aus .lic | Exit 6 oder warn |

**Log-Only Checks** (kein Block, Audit-Trail):

| # | Inhalt | Ausgabe |
|---|---|---|
| L1 | `<licensee>`-Name | stderr: `Licensee: Martin Pietschmann` |
| L2 | Tage bis `<expires>` | `warn` 30 Tage vorher, `critical` 7 Tage vorher, dann kein Adapter-Start mehr |
| L3 | Aktuelle Counts vs. Limits | stderr-Info (z. B. `BPAProcess=31 / max=5 (OK)`) |
| L4 | `<source>` der Lizenz (Portal / andere) | Audit-Log |

**Bypass (Dev/CI):**

- CLI-Flag: `bpgit --skip-license-check …` setzt G1–G5 als bestanden.
- Env-Var: `BPGIT_SKIP_LICENSE_CHECK=1` (gleiche Wirkung).
- Wird explizit in stderr/Log vermerkt: `WARNING: License-Check bypassed (B1–G5 nicht ausgeführt)`.

**Out of Scope v1:**

- **Kryptografische XAdES-Signatur-Prüfung** (`<Signature>`-Element). Würde BP-Public-Key benötigen — ist nicht öffentlich dokumentiert. Kandidaten-Suche: signierte DLLs im `BPUi/`-Verzeichnis (`C:\Program Files\Blue Prism Limited\Blue Prism Automate\BPUi\`), oder via BP-Support anfordern. Phase-2-Ticket (separate Karte).
- **Online-Revocation-Checks** (keine Public-API vorhanden).
- **Per-User-Lizenzen** (BP unterscheidet nur Application-Level-Lizenzen, nicht per BP-Login-User).

**Bypass-Empfehlung:** `--skip-license-check` nur in CI/Headless-Tests und isolierten Dev-Setups verwenden. In Prod-Umgebungen immer aktiviert.

## Datenfluss

### `bpgit pull`

<pre>
License-Guard → Signatur-Validation OK
     ↓
SqlClient öffnet (localdb)\BluePrismLocalDB (Win-Integrated-Auth)
     ↓
Dapper-Mapping BPAProcess → List&lt;Process&gt;
     ↓
XML-Serializer schreibt *.bpprocess.xml + zugehörige Process-Attribute-Files
     ↓
Snapshot im Adapter-Cache (für `bpgit diff` und `bpgit commit`)
</pre>

### `bpgit commit`

<pre>
License-Guard → OK
     ↓
Liest Worktree-XMLs → parsed → mapped auf BPA*-Tabellen
     ↓
Schreibt direkt per SqlCommand + Transaktion:
   • UPSERT BPAProcess.processxml (Haupt-XML-Body) + Head-Metadaten
   • Reconcile-Loop ueber BPAProcessAttribute, BPAProcess*Dependency,
     BPAProcessEnvVar, BPAProcessLock usw.
   • Atomare Commit-Transaktion pro Process (Rollback bei Validierungsfehlern)
     ↓
Snapshot-Hash-Vergleich (idempotent-check)
</pre>

> **Implementierungs-Hinweis:** Schreibpfad ist **direkter SqlCommand**, kein `automateC.exe /import`-Round-Trip — Martin-Direktive (16:44 GMT+2): CLI-Round-Trip bei grossen Process-XMLs zu langsam.

## Sicherheitsgrenzen

- **Credentials ausschließen:** `BPACredentials`, `BPAKeyStore`, `BPAPassword`, alle Spalten mit `encryptid` als FK — niemals in Worktree ausgeben (Whitelist via `ignore_tables` in `config.toml`)
- **Read-Only by Default:** `commit`-Subcommand scharf explizit (`--force`-Flag erforderlich); sonst nur `pull`/`status`/`log`/`diff`
- **Signaturprüfung:** Adapter bricht ab bei ungültiger / abgelaufener Lizenz
- **Keine impliziten Mutationen:** Der Adapter mutiert die BP-DB nur auf expliziten User-Befehl (`commit`)
- **Administrativer Account:** Adapter läuft mit dem gleichen Windows-Konto wie der laufende BP-Service; keine künstliche Berechtigungs-Eskalation
- **Audit-Trail:** Jeder `commit` legt einen Eintrag in der Log-Konfiguration ab (Datum, Versionen, Tabellen-Diffs)

## Konfiguration: `~/.bpgit/config.toml`

```toml
[bp]
# Auth-Modus A: SSPI (Windows Integrated Auth; funktioniert automatisch
# mit NTLM lokal und mit Kerberos in AD-Domänen-SSO)
# Default-Modus — wenn keine sql_user-Eintraege gesetzt sind.
connection_string = "Server=(localdb)\\BluePrismLocalDB;Integrated Security=SSPI;Database=BluePrism"

# Auth-Modus B: SQL-Auth (für CI oder wenn keine Windows-Identity verfügbar)
# Aktiv, sobald `sql_user` gesetzt ist. Password NIEMALS ins Repo — kommt
# via env-var BPGIT_DB_PASSWORD oder `dotnet user-secrets` (Substitution ${BPGIT_DB_PASSWORD}).
# connection_string_sql_auth = "Server=bp-prod.acme.local;Database=BluePrism;User Id=bpgit_readonly;Password=${BPGIT_DB_PASSWORD}"
# sql_user = "bpgit_readonly"

license_path = "C:\\Users\\Admin\\Desktop\\bp-education-license-v2-2027.lic"

# === License-Guard Tuning ===
allowed_license_types = ["education", "perpetual"]   # "trial" => block
footprint_policy       = "block"                      # "block" | "warn"
warn_before_expiry_days    = 30
critical_before_expiry_days = 7

# Tabellen, die der Adapter ignoriert (Credentials, Session-Logs, System-Seed)
ignore_tables = [
ignore_tables = [
  "BPASessionLog_*",
  "BPASession",
  "BPASessionSource",
  "BPAPassword",
  "BPAPasswordRules",
  "BPACredentials",
  "BPACredentialsProcesses",
  "BPACredentialsProperties",
  "BPACredentialsResources",
  "BPAKeyStore",
  "BPAInternalAuth",
  "BPAUserRolePerm",
  "BPAPerm",
  "BPAPermGroup",
  "BPAPermGroupMember",
  "BPADBVersion",
  "BPAPublicHoliday*",
  "BPADBMaintenance*",
  "BPADataTracker",
  "BPASync*",
  "BPACache*",
  "BPAStatistics",
  "BPAStatus",
  "BPAAliveResources",
  "BPAAliveAutomateC",
  "BPAAuditEvents",
  "BPAScreenshot"
]

[git]
remote = null  # lokal-only für v1
author_name = "BP-Git Adapter"
author_email = "bp-git@local"
commit_msg_template = "bpgit: {action} {count} items ({date})"
```

## MVP-Phasen

### Phase 1: Read-Only Export (Scope diese Woche)

- Subcommands: `init`, `pull`, `status`, `log`, `diff`
- Tabellen-Scope: `BPAProcess` + Attribute + Dependencies + EnvVar, `BPAEnvironment` (Variable only), `BPAWorkQueue` + Filter + Item, `BPARelease` + Entry
- **Round-Trip-Test:** Adapter exportiert BP-Demo-Process → XML → manuelle Studio-`Import` → Diff = leer

### Phase 2: Round-Trip-Write (DB-direct)

- Subcommand: `commit --force`
- **Schreibpfad: direkter SqlCommand + Transaktion** (kein CLI-Round-Trip)
- Begründung (Martin 16:44 GMT+2): `automateC.exe /import` zu langsam für grosse Process-XMLs
- UPSERT in BPAProcess.processxml (Haupt-XML) + Reconcile in allen abhängigen BPAProcess*-Tabellen (Attribute, Dependencies, EnvVar, Lock, …)
- Atomare Transaktion pro Process (Rollback bei XSD-/Self-Check-Fehler)
- Validierung vor Commit: Version-Konflikt-Erkennung + Identity-Lock-Check
- Idempotenz-Prüfung (gleiches XML zweimal committen = kein DB-Drift)
- Optionale Phase-2b-Erweiterung: `automateC.exe /import` als Fallback für sehr grosse/komplexe Prozesse (später, falls Performance-Daten das rechtfertigen)

### Phase 3: VS-Code-Integration

- VS-Code-Extension für In-IDE-Diff
- Pre-Commit-Hook gegen versehentliche Credential-Diffs

## Out-of-Scope (für alle Phasen)

- **Live-Editing von BP-Processes im Adapter** (zu riskant; immer Studio/automateC)
- **Multi-Instance-Replikation**
- **Performance-Optimierung für >10k Processes**
- **Encryption-Layer für exportierte XMLs** (optional, später; klar markiert mit `[[unencrypted]]`-Hinweis in `config.toml`)
- **Web-Service-Endpoint** (BP hat eigene API; separater Adapter dafür, nicht Scope dieses Tools)
