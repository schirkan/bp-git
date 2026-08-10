# SPEC — BP-Git-Adapter-Architektur

Stand: 2026-08-10 (Martin-Anforderung 17:02 GMT+2)
Status: draft v2 — License-Guard entfernt, Bridge-Architektur hinzugefügt
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
  │   └──────┬───────┘           │  SqlClient (Win-Auth)             │
  │          │                   │  + Dapper-Mapping                   │
  │          │                   │                                    │
  │   ┌──────▼───────┐                                                 │
  │   │  git CLI /    │                                                 │
  │   │  VS Code      │                                                 │
  │   └──────────────┘                                                 │
  └────────────────────────────────────────────────────────────────────┘
</pre>

## Komponenten (.NET, C# 13)

### 1. Adapter-CLI (`BPGit.Cli`)

`dotnet`-basiertes Konsolen-Tool mit Subcommands analog zu Git:

| Subcommand | Funktion |
|---|---|
| `bpgit init` | Initialisiert bp-git-Worktree aus BP-Instanz (konfiguriert `config.toml`, ggf. Hooks, initialer Pull + git-Initial-Commit) |
| `bpgit status` | Zeigt Diffs Worktree ↔ DB (lokal modifiziert + DB-Drift) |
| `bpgit pull` | Exportiert aktuellen BP-Stand → Worktree, schreibt Snapshot |
| `bpgit commit` | Worktree → DB-Import (Round-Trip-Write) — explizit `--force`-Flag |
| `bpgit log` | BP-History aus `BPAProcessBackup` (statt git-log) |
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

## Bridge-Architektur (git ↔ BP-DB)

bpgit ist der Synchronisations-Layer zwischen zwei Welten:
- **VS Code / git:** datei-basiert, Working-Tree, Hashes, Commits, Diffs
- **BP-DB:** SQL-basiert, `BPAProcess` / `BPARelease` / `BPA*`-Tabellen, Identity-PKs (`UNIQUEIDENTIFIER`)

bpgit übersetzt zwischen beiden: **DB → XML-Dateien** (`pull`) und **XML → DB-UPSERT** (`commit`). VS Code muss nichts von BP wissen — es sieht einen normalen Git-Worktree mit XML-Dateien. bpgit läuft als externer Sync-Step zwischen git-Operationen.

### Pattern: File-Based Adapter, git als Versions-Layer

```
<pre>
   ┌──────────────────────────────────────────────────────────────────┐
   │  VS Code (oder jedes Git-fähige Tool)                            │
   │  • Datei-Editor mit XML-Highlighting                              │
   │  • Integrierte Git-UI (Source-Control-Panel, Diff-View)           │
   │  • Terminal: bpgit …                                              │
   └──────────────────────────┬───────────────────────────────────────┘
                              │  liest / schreibt
                              ▼
   ┌──────────────────────────────────────────────────────────────────┐
   │  Working-Tree (von git versioniert)                               │
   │                                                                  │
   │   .bpgit/                                                         │
   │   ├── config.toml         # DB-Credentials, Profile, …            │
   │   ├── snapshot.json       # Pull-Snapshot (Hashes + IDs)          │
   │   └── lock                                                       │
   │                                                                  │
   │   processes/<processid>/                                         │
   │   ├── process.xml         # = BPAProcess.processxml              │
   │   ├── attributes.xml      # = BPAProcessAttribute-Zeilen         │
   │   ├── envvars.xml         # = BPAProcessEnvVar-Zeilen            │
   │   └── meta.json           # {name, type, version, lastmodified}  │
   │                                                                  │
   │   releases/<releaseid>/release.xml                              │
   │   objects/<objectid>/process.xml                                │
   │   environments/<envid>/env.xml                                   │
   │                                                                  │
   │   .gitattributes       # registriert *.xml als diffbares Text     │
   └──────────────────────────┬───────────────────────────────────────┘
                              │  SqlClient (Win-Auth oder SQL-Auth)
                              ▼
   ┌──────────────────────────────────────────────────────────────────┐
   │  Blue Prism DB (BPAProcess, BPARelease, BPA*Identity-PKs)       │
   └──────────────────────────────────────────────────────────────────┘
</pre>
```

**Naming-Strategie** (`config.toml.worktree.naming`):

- **`by-uuid`** (Default): `processes/<processid>/` — deterministisch, immun gegen Renames in BP.
- **`by-name`** (optional): `processes/<sanitized-name>/` — menschenlesbar, aber Renames in BP brechen das Mapping (Reconciler muss datei umbenennen).

### User Flow (VS-Code-Workflow)

| Schritt | Kommando / Aktion | Was passiert |
|---|---|---|
| 1 | `bpgit init` (einmalig) | `.bpgit/config.toml`, `.gitattributes`, ggf. Hooks. Initialer `pull` + git-Initial-Commit. |
| 2 | `bpgit pull` | SqlClient öffnet BP-DB → `SELECT * FROM BPAProcess` + abhängige Tabellen → XML-Dateien in Worktree → Snapshot in `.bpgit/snapshot.json` → `git add . && git commit -m "bpgit: pull YYYY-MM-DD"`. |
| 3 | User editiert `processes/<guid>/process.xml` in VS Code | Normaler Datei-Edit. `git diff` zeigt XML-Unified-Diff. |
| 4 | `bpgit status` | Parst lokale XMLs, vergleicht Hashes mit `.bpgit/snapshot.json` → modified / added / deleted. Außerdem DB-Drift (was seit letztem pull in BP geändert wurde). |
| 5 | `git add . && git commit -m "Edit HellWorld validation"` | Standard-git-Versionierung. VS Code Source-Control-Panel macht das automatisch. |
| 6 | `bpgit commit` | Parst geänderte XMLs → pro Process SqlCommand-Transaktion: `UPSERT BPAProcess.processxml` + Reconcile in `BPAProcessAttribute` / `BPAProcess*Dependency` / `BPAProcessEnvVar` → Snapshot-Update → `git add . && git commit -m "bpgit: commit YYYY-MM-DD"`. |
| 7 | `git push` (optional, wenn Remote konfiguriert) | Standard-git. |

**Wichtig:** bpgit läuft **zwischen** den git-Schritten, nicht parallel. Es werden keine git-Hooks installiert, die Commits blockieren.

### Identity-Layer

| Konzept | BP-DB | Worktree | Zuordnung |
|---|---|---|---|
| Process | `BPAProcess.processid UNIQUEIDENTIFIER` | `<processid>` als Verzeichnis-Name | 1:1 |
| Object | `BPAProcess.processid` (ProcessType='O') | `<processid>` als Verzeichnis-Name | 1:1 |
| Release | `BPARelease.id` | `<releaseid>` als Verzeichnis-Name | 1:1 |
| Versionen | `BPAProcessBackup.backupdate` + diff in `processxml` | git-Commits | über `bpgit log` |

`meta.json` führt menschenlesbare Identität (`name`, `type`, `version`, `lastmodified`) parallel zum UUID-Pfad — für UX-Anzeige in bpgit-Status.

### Sync-Sicherheit

- **`BPAProcessLock`** wird vor jedem `bpgit commit` geprüft → Lock aktiv → Exit mit Hinweis auf Lock-Owner.
- **`lastmodifieddate` als Optimistic-Lock** — wenn der DB-Stand vom Snapshot abweicht → Konflikt-Meldung (Merge-Workflow nötig).
- **Override-Flag:** `--allow-stale` (Legacy-Mode, klares WARNING-Log) umgeht den Stale-Check.
- **Atomare Transaktion pro Process** — Rollback bei XML-/Schema-Validierungsfehler.
- **Idempotenz** — zweimaliges Commit desselben XML ändert die DB nicht (Hash-Vergleich vor UPSERT).

### VS-Code-Integration

| Phase | Mechanismus | Aufwand |
|---|---|---|
| **Jetzt v1** | Worktree als VS-Code-Ordner öffnen, Standard-git-Integration, Standard-XML-Syntax-Highlighting. `bpgit` ist ein externes CLI in der Shell. | ✓ fertig, kein Code |
| **Phase 2** | Custom Diff-Driver via `.gitattributes`: `*.xml diff=bp-xml-clean` → git nutzt `bpgit diff-xml` für semantische Diffs (Stage-Order, Inputs/Outputs) statt Text-Diffs. | ~50 LoC |
| **Phase 3** | VS-Code-Extension: Snippets für BP-Stages, `BPAValCheck`-Validierung, Inline-Vorschau der Stage-Effekte. | separates Extension-Projekt |

### Hooks (optional, installierbar per `bpgit init --install-hooks`)

- `.git/hooks/post-checkout` — Warnung wenn Working-Tree von DB abweicht (DB-Drift-Detection)
- `.git/hooks/post-merge` — analog nach Branch-Merge

Pre-commit- und Push-Hooks werden **explizit nicht** installiert — sie würden `git commit` bzw. `git push` blockieren, was die UX verschlechtert. bpgit läuft als expliziter Sync-Step zwischen den git-Befehlen.

### Erkennungs-Heuristik für `bpgit init`

Wenn im aktuellen Ordner bereits ein `.git/`-Verzeichnis existiert und dort BP-XML-Files mit der typischen Struktur (`<process name="..." version="...">`-Root) liegen, erkennt `bpgit init` das bestehende Repo und bietet `bpgit pull --add-missing` (nur neue BP-Items in den Worktree legen) statt eines Voll-`init`.

## Datenfluss

### `bpgit pull`

<pre>
SqlClient öffnet (localdb)\BluePrismLocalDB (Win-Integrated-Auth)
     ↓
Dapper-Mapping BPAProcess → List&lt;Process&gt;
     ↓
XML-Serializer schreibt processes/&lt;processid&gt;/process.xml + Sidecar-Files
     ↓
Snapshot in .bpgit/snapshot.json (Hashes + IDs)
     ↓
git add . && git commit (Stage-Modus mit --no-commit möglich)
</pre>

### `bpgit commit`

<pre>
Liest Worktree-XMLs → parsed → mapped auf BPA*-Tabellen
     ↓
Pro Process: SqlCommand-Transaktion
   • UPSERT BPAProcess.processxml (Haupt-XML) + Head-Metadaten
   • Reconcile-Loop ueber BPAProcessAttribute, BPAProcess*Dependency,
     BPAProcessEnvVar, BPAProcessLock usw.
   • Atomare Commit-Transaktion (Rollback bei Validierungsfehler)
     ↓
Snapshot-Update
     ↓
git add . && git commit
</pre>

> **Implementierungs-Hinweis:** Schreibpfad ist **direkter SqlCommand**, kein `automateC.exe /import`-Round-Trip — Martin-Direktive (16:44 GMT+2): CLI-Round-Trip bei grossen Process-XMLs zu langsam.

## Sicherheitsgrenzen

- **Credentials ausschließen:** `BPACredentials`, `BPAKeyStore`, `BPAPassword`, alle Spalten mit `encryptid` als FK — niemals in Worktree ausgeben (Whitelist via `ignore_tables` in `config.toml`)
- **Read-Only by Default:** `commit`-Subcommand scharf explizit (`--force`-Flag erforderlich); sonst nur `pull`/`status`/`log`/`diff`
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
# connection_string_sql_auth = "Server=bp-prod.acme.local;Database=BluePrism;User Id=bpgit_readonly;Password=${BPGI…ORD}"
# sql_user = "bpgit_readonly"

# Tabellen, die der Adapter ignoriert (Credentials, Session-Logs, System-Seed)
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

[worktree]
# Naming-Strategie: "by-uuid" (Default, stabil gegen Renames) oder "by-name" (menschenlesbar)
naming = "by-uuid"
```

## MVP-Phasen

### Phase 1: Read-Only Export (Scope diese Woche)

- Subcommands: `init`, `pull`, `status`, `log`, `diff`
- Tabellen-Scope: `BPAProcess` + Attribute + Dependencies + EnvVar, `BPAEnvironment` (Variable only), `BPAWorkQueue` + Filter + Item, `BPARelease` + Entry
- Round-Trip-Test: Adapter exportiert BP-Demo-Process → XML → manuelle Studio-`Import` → Diff = leer

### Phase 2: Round-Trip-Write (DB-direct)

- Subcommand: `commit --force`
- **Schreibpfad: direkter SqlCommand + Transaktion** (kein CLI-Round-Trip)
- Begründung (Martin 16:44 GMT+2): `automateC.exe /import` zu langsam für grosse Process-XMLs
- UPSERT in `BPAProcess.processxml` (Haupt-XML) + Reconcile in allen abhängigen `BPAProcess*`-Tabellen (Attribute, Dependencies, EnvVar, Lock, …)
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
