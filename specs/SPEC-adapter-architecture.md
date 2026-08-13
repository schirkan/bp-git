# SPEC — BP-Git-Adapter-Architektur

**Stand:** 2026-08-12 (v4 — Git-Server-Architektur + Phase 4c + xunit-Tests-Welle)
**Status:** draft v4 — Worktree-Layout final, processid-Mapping via git-diff; PostReceive/PostCheckout Hooks done (Phase 4c); git-CLI receive-pack + upload-pack delegation done (Phase 4b-follow-up); xunit-Tests-Welle done (12 Commits, 65 gruen + 4 skipped)
**Bezieht sich auf:** [SPEC-target-environment.md](./SPEC-target-environment.md), [context/SPEC-git-server.md](../context/SPEC-git-server.md), [context/bp-database-schema.md](../context/bp-database-schema.md)

## Ziel

Git-konformer Read/Write-Adapter für Blue Prism (BP) v7.5. XML-Repräsentationen von Processes / Objects werden im Dateisystem sicht- und editierbar; Versionsverwaltung über Standard-Git-Befehle. Der Adapter läuft als self-hosted C#-Server (Kestrel + LibGit2Sharp) auf OpenClawPC — kein IIS, kein Apache, kein bpgit-CLI auf Developer-Workstations.

> **Detaillierte Server-Architektur, Hook-Implementation, Deployment**: siehe [`context/SPEC-git-server.md`](../context/SPEC-git-server.md). Dieses Dokument beschreibt die **Adapter-Domain-Logik** (Worktree-Layout, processid-Mapping, XML-Serialisierung, Sanitisierung).

## High-Level-Architektur

```
+-------------------+    git clone/push/pull       +-------------------------+    AutomateC.exe    +------------------+
|                   |   (HTTP, Win-Auth/SSO)    |                         |    /import /        |                  |
|   Developer       | <------------------------> |   bpgit-git-server      |    /forceid /       |  Blue Prism      |
|   Workstation     |     standard-git-protocol  |   (OpenClawPC)          |    /overwrite       |  Database        |
|   (kein bpgit)    |                            |                         | <-----------------> |  (localdb)       |
+-------------------+                            |  - Kestrel HTTP         |    SqlCommand       +------------------+
                                                |  - LibGit2Sharp         |
                                                |  - pre-/post-receive Hooks|
                                                +-------------------------+
```

**Developer Workstation** führt ausschliesslich Standard-git aus — kein `bpgit.exe`, keine Hooks, kein BP-CLI. **bpgit-git-server** auf OpenClawPC hält die BP-DB-Verbindung und führt alle Hooks serverseitig aus.

## Komponenten (.NET 10, C# 13)

### 1. bpgit-git-server (`BPGit.Server`)

Kestrel-basierter HTTP-Server mit LibGit2Sharp für git-smart-HTTP-Protocol:

| Modul                | Aufgabe                                                                                   |
| -------------------- | ----------------------------------------------------------------------------------------- |
| `KestrelListener`    | HTTP-Listener auf konfigurierbarem Port (Default 8181)                                    |
| `WindowsAuthHandler` | Negotiate/NTLM-Authentifizierung                                                          |
| `GitHttpHandler`     | git-smart-HTTP (`/info/refs`, `/git-upload-pack`, `/git-receive-pack`) via LibGit2Sharp   |
| `PreReceiveHook`     | Processid-Lookup + `AutomateC.exe /import /forceid /overwrite`                            |
| `PostReceiveHook`    | BP-DB → canonical Filenames schreiben                                                     |
| `PostCheckoutHook`   | Worktree-Materialization bei `git clone` und `git checkout`                               |
| `BpDbService`        | SqlCommand-Zugriff auf BPAProcess + BPATree + BPAGroup + BPAGroupProcess + BPAAuditEvents |
| `AutomateCRunner`    | Process.Start-Wrapper für AutomateC.exe `/import /importrelease /export`                  |

### 2. Data-Layer (`BPGit.Data`)

POCOs für die Kern-BPA*-Tabellen, Dapper-Mapping:

| DTO                 | Quell-Tabellen                                                |
| ------------------- | ------------------------------------------------------------- |
| `Process`           | `BPAProcess`, `BPAProcessAttribute`, `BPAProcessBackup`       |
| `ProcessAuditEvent` | `BPAAuditEvents`, LEFT JOIN `BPAUser`, LEFT JOIN `BPAProcess` |
| `Tree`              | `BPATree` (gefiltert auf Processes/Objects)                   |
| `Group`             | `BPAGroup` (+ rekursiv `BPAGroupGroup` für nested)            |
| `ProcessMembership` | `BPAGroupProcess` (M:N)                                       |
| `ProcessDependency` | 9 `BPAProcess*Dependency`-Tabellen                            |
| `ProcessEnvVar`     | `BPAProcessEnvVar`                                            |
| `ProcessLock`       | `BPAProcessLock`                                              |
| `Release`           | `BPARelease`, `BPAReleaseEntry`                               |

### 3. XML-Serializer (`BPGit.Format`)

Kanonisches Mapping BP-DB-Zeilen ↔ XML-Repräsentation:

- **Input:** `BPAProcess.processxml` (bereits XML, 1:1 aus BP Studio)
- **Output:** XML-Datei mit unverändertem `processxml`-Inhalt
- **Validierung:** XML-Parse-Check + Root-Element-Typ (`<process>` oder `<object>`) + Name-Extraktion
- **Sanitization:** Windows-Dateinamen-Sanitization für abgeleitete Filenames (siehe unten)

## Worktree-Layout (final, per #6289, #6311)

```
<worktree>/                                  # git working tree
|
+-- processes/                               # Folder-aware BP-Worktree
|   +-- Processes/                           # BPATree id=2 (gefiltert)
|   |   +-- Default/                         # BPAGroup name="Default"
|   |   |   +-- MP - Subprocess A.xml        # filename = sanitize(BPAProcess.name) + ".xml"
|   |   |   +-- Test Process.xml
|   |   +-- dummy/                           # BPAGroup name="dummy"
|   |       +-- bp demo.xml
|   +-- Objects/                             # BPATree id=3 (gefiltert)
|       +-- Default/
|       |   +-- Data - SQL Server.xml
|       |   +-- Email - POP3-SMTP-IMAP.xml
+-- .bpgit/
|   +-- config.toml                          # BP connection + [cli] auth section
+-- .git/                                    # Standard git internals
+-- .gitignore                               # excludes .bpgit/, *.bak, temp files
```

### Worktree-Invariante (per #6311)

**`filename = sanitize(BPAProcess.name) + ".xml"`** — abgeleitet aus dem XML-Root-`name`-Attribut.

- BP-Name ist Single Source of Truth.
- Filename wird beim Pull automatisch normalisiert (Post-Receive-Hook schreibt canonical Filenames, löscht veraltete).
- User editiert NUR die XML-Datei (Inhalt), nicht den Filename.
- Manuelle `git mv`-Operationen werden vom Server toleriert, beim nächsten Pull aber rückgängig gemacht.

### Filename-Sanitisierung

```
sanitize(name):
    return re.sub(r'[<>:"/\\|?*]', '_', name).rstrip('. ')
```

**Beispiele:**

| BP-Name                 | Filename                    |
| ----------------------- | --------------------------- |
| `MS Excel VBO`          | `MS Excel VBO.xml`          |
| `Utility - Environment` | `Utility - Environment.xml` |
| `Prozess: Test`         | `Prozess_ Test.xml`         |
| `Path/Test`             | `Path_Test.xml`             |
| `Trailing. `            | `Trailing.xml`              |

### Folder-Hierarchie

`BPATree` → `BPAGroup` → `BPAGroupGroup` (nested) → `BPAGroupProcess` (M:N):

- **Trees**: nur `"Processes"` (id=2) und `"Objects"` (id=3) materialisieren. Andere Trees (Tiles, Queues, Resources, users) werden ignoriert.
- **Groups**: jeder `BPAGroup` mit `treeid IN (2, 3)` wird zu einem Folder.
- **Nested Groups**: `BPAGroupGroup(groupid, memberid)` für Folder-in-Folder (Schema vorhanden, Demo-DB hat 0 Rows).
- **Memberships**: `BPAGroupProcess` ist M:N — derselbe Process kann in mehreren Groups liegen (Datei-Duplikation akzeptiert).

### Filename-Extraktion

Regex auf `BPAProcess.processxml` (Root-Element):

```
^\s*<(process|object)\s+[^>]*\bname\s*=\s*"([^"]+)"
```

Beispiel: `<process name="MP - Subprocess A" ...>` → `"MP - Subprocess A"`

**Wichtig:** Vor Regex-Match `StripLeadingXmlComments` anwenden (per #6277) — BP Studio kann Leading Comments in processxml schreiben, die das Regex brechen.

## processid-Mapping (per #6311)

**Kern-Insight:** `BPAProcess.processid` (Tabellen-PK, UNIQUEIDENTIFIER) ist NICHT in `BPAProcess.processxml` enthalten. Root-Tag `<process name="...">` hat kein id-Attribut. XML enthält nur Sub-Element-IDs (Main Window, Buttons, Stages).

**Mapping-Lösung:** Lookup zur Laufzeit über `git diff` (alter + neuer Pfad verfügbar via R/M/A/D-Status) + DB-Query auf `BPAProcess.name`.

### Processid-Auflösung pro Operation

| Git-Diff-Status      | Alter Name (Filename) | Neuer Name (XML-Root) | BP-Aktion                                                                                                                        |
| -------------------- | --------------------- | --------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| `M`                  | "X"                   | "X" (gleich)          | DB-Lookup `WHERE name='X'` → processid → `/import /forceid /overwrite <file>`                                                    |
| `M` (Rename via XML) | "Old"                 | "New" (≠)             | DB-Lookup `WHERE name='New'` (0 Treffer), dann `WHERE name='Old'` (1 Treffer) → processid → `/import /forceid /overwrite <file>` |
| `A`                  | —                     | "New"                 | DB-Lookup `WHERE name='New'` (0 erwartet) → `/import <file>` (BP legt neuen Prozess an)                                          |
| `D`                  | "Gone"                | —                     | DB-Lookup `WHERE name='Gone'` → wenn 1 Treffer: Prozess löschen                                                                  |
| `R` (git mv)         | "Old"                 | "New"                 | Wie Modify-Rename-Fall, danach Pull-Normalisierung                                                                               |

### Rename-Walkthrough (komplett)

**Phase 1 — User renamed via XML im Worktree:**

1. User öffnet `processes/Objects/Default/Old Name.xml`, ändert `<process name="Old Name">` → `<process name="New Name">`. **Speichert unter gleichem Filename** (kein `git mv`).
2. `git add . && git commit -m "rename Old -> New" && git push`
3. Server `pre-receive`:
   - `git diff oldrev..newrev -- processes/` zeigt **Modify** auf `Old Name.xml`
   - XML-Root: `<process name="New Name">` → name="New Name"
   - Filename: `Old Name.xml` → alter Name implizit: "Old Name"
   - DB-Lookup `WHERE name='New Name'` → 0 Treffer
   - DB-Lookup `WHERE name='Old Name'` → processid `42b5169c-...`
   - **`/import /forceid 42b5169c-... /overwrite <tmpfile>`**
   - BP aktualisiert `BPAProcess.name="New Name"` + `processxml=<...New Name...>`, schreibt BPAAuditEvent (sCode=P006)
4. Server `post-receive` schreibt canonical Filename:
   - Schreibt `processes/Objects/Default/New Name.xml`
   - Löscht `processes/Objects/Default/Old Name.xml`
   - Git committet dies als **Auto-Rename** (Similarity-Match), History bleibt erhalten

**Phase 2 — User renamed in BP Studio:**

1. User benennt Prozess in BP Studio: "Old Name" → "New Name"
2. BP aktualisiert `BPAProcess.name` + `processxml`, schreibt BPAAuditEvent (sCode=P006)
3. User `git pull`
4. Server `post-checkout` Hook:
   - Liest `BPAProcess.name="New Name"` → schreibt `New Name.xml`
   - Löscht `Old Name.xml`
   - Git committet als **Auto-Rename** (Similarity-Match)

**Phase 3 — git mv (manuell, unerwünscht):**

1. User `git mv Old Name.xml Renamed.xml` (kein XML-Content-Edit)
2. Push
3. Server `pre-receive`:
   - Sieht **R100** (pure Rename, kein Content-Change)
   - Alter Name (aus old path): "Old Name"
   - Neuer Name (aus XML-Root): "Old Name" (unverändert)
   - DB-Lookup `WHERE name='Old Name'` → processid
   - `/import /forceid <pid> /overwrite Renamed.xml`
4. Server `post-receive` normalisiert:
   - Schreibt canonical `Old Name.xml`
   - Löscht `Renamed.xml`
   - `git mv` wird effektiv rückgängig gemacht

## Bridge-Architektur (git ↔ BP-DB)

bpgit-git-server übersetzt zwischen zwei Welten:

- **VS Code / git**: datei-basiert, Working-Tree, Hashes, Commits, Diffs
- **BP-DB**: SQL-basiert, `BPAProcess` / `BPARelease` / `BPA*`-Tabellen, Identity-PKs (`UNIQUEIDENTIFIER`)

**DB → XML-Dateien** (Pull): Post-Checkout-Hook liest BP-DB via SqlCommand, schreibt XML-Dateien mit canonical Filenames.

**XML → DB-UPSERT** (Push): Pre-Receive-Hook parsed `git diff`, ermittelt processid via DB-Lookup, ruft `/import /forceid /overwrite`.

VS Code muss nichts von BP wissen — es sieht einen normalen Git-Worktree mit XML-Dateien. Der Adapter läuft serverseitig als automatischer Sync-Layer, transparent für den User.

### Naming-Strategie

**`by-name`** (verbindlich, per #6289): `processes/<TreeName>/<GroupName(s)>/<sanitize(BPAProcess.name)>.xml`.

- Menschenlesbar
- Deterministisch (BP-Name + Folder-Pfad → Filename)
- Renames werden via Post-Receive-Normalisierung behandelt

`by-uuid` wird **nicht** mehr unterstützt — hätte eine Registry erfordert (processid → Pfad-Mapping), die mit der "kein snapshot.json"-Direktive unvereinbar wäre.

### Sync-Sicherheit

- **`BPAProcessLock`** wird vor jedem `/import` geprüft → Lock aktiv → Push ablehnen mit Hinweis auf Lock-Owner.
- **`lastmodifieddate` als Optimistic-Lock** (MVP2) — wenn der DB-Stand vom Snapshot abweicht → Konflikt-Meldung.
- **Override-Flag:** `--force` für Admin-Override (Lock + Stale ignoriert).
- **Atomare Transaktion pro Process** — Rollback bei XML-/Schema-Validierungsfehler.

### VS-Code-Integration

| Phase        | Mechanismus                                                                                                                                                           | Aufwand                     |
| ------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------- |
| **Jetzt v1** | Worktree als VS-Code-Ordner öffnen, Standard-git-Integration, Standard-XML-Syntax-Highlighting.                                                                       | ✓ fertig, kein Code         |
| **Phase 2**  | Custom Diff-Driver via `.gitattributes`: `*.xml diff=bp-xml-clean` → git nutzt `bpgit diff-xml` für semantische Diffs (Stage-Order, Inputs/Outputs) statt Text-Diffs. | ~50 LoC                     |
| **Phase 3**  | VS-Code-Extension: Snippets für BP-Stages, `BPAValCheck`-Validierung, Inline-Vorschau der Stage-Effekte.                                                              | separates Extension-Projekt |

## Datenfluss

### Pull-Flow (git clone, git pull)

`post-checkout` Hook:

```
SqlCommand öffnet (localdb)\BluePrismLocalDB (Win-Integrated-Auth)
     ↓
Dapper-Mapping BPAProcess → List<Process>
     ↓
Für jeden Process:
  - Filename aus processxml extrahieren (Regex)
  - Sanitize + Path ableiten
  - XML zu worktree schreiben (canonical Filename)
     ↓
Alte Files im processes/ löschen (canonical Filename-Normalisierung)
     ↓
git add . && git commit (auto-detected Renames)
```

### Push-Flow (git push)

`pre-receive` Hook:

```
git diff oldrev..newrev -- processes/
     ↓
Für jede Änderung (R/M/A/D):
  - processid via DB-Lookup (siehe Tabelle oben)
  - AutomateC.exe /import /forceid <pid> /overwrite <tmpfile>
  - Bei Fehler: Push ablehnen
     ↓
post-receive Hook:
  - BP-DB pollen, canonical Filenames schreiben
  - Alte Files löschen
```

**Performance-Hinweis** (per Martin #6285): NIEMALS AutomateC.exe `/export` für Pull — zu langsam. SqlCommand direkt ist Pflicht.

## Sicherheitsgrenzen

- **Credentials ausschließen:** `BPACredentials`, `BPAKeyStore`, `BPAPassword`, alle Spalten mit `encryptid` als FK — niemals in Worktree ausgeben (Whitelist via `ignore_tables` in `config.toml`).
- **Kein bpgit.exe auf Client-Workstations** — nur Server (OpenClawPC) hat DB-Credentials + AutomateC.exe.
- **Windows-Integrated-Auth** für Server-Zugriff — BP-DB-Login = Windows-User (Audit-Trail via BPAAuditEvents.gSrcUserID).
- **Keine impliziten Mutationen** — Adapter mutiert BP-DB nur auf expliziten User-Befehl (`git push`).
- **Audit-Trail** — jeder `/import` schreibt BPAAuditEvent (sCode=P006) mit Windows-User als `gSrcUserID`.

## Konfiguration: `~/.bpgit/config.toml`

```toml
[bp]
# Auth-Modus A: SSPI (Windows Integrated Auth; funktioniert automatisch
# mit NTLM lokal und mit Kerberos in AD-Domänen-SSO)
# Default-Modus — wenn keine sql_username-Eintraege gesetzt sind.
connection_string = "Server=(localdb)\\BluePrismLocalDB;Integrated Security=SSPI;Database=BluePrism"

# Auth-Modus B: SQL-Auth (für CI oder wenn keine Windows-Identity verfügbar)
# Aktiv, sobald `sql_username` gesetzt ist. Credentials stehen direkt
# in config (kein env-var-Lookup).
# sql_username = "bpgit_readonly"
# sql_password = "..."

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
  "BPAScreenshot"
]

[paths]
# Wo XML-Dateien im Worktree liegen
processes_root = "processes"

[worktree]
# Sanitization-Regex für Filename-Derivation
filename_invalid_chars = '<>:"/\\|?*'

# Naming-Strategie (aktuell nur "by-name" unterstützt)
naming = "by-name"
```

## Out-of-Scope

- **Live-Editing von BP-Processes im Adapter** (zu riskant; immer Studio/automateC)
- **Multi-Instance-Replikation**
- **Performance-Optimierung für >10k Processes**
- **Encryption-Layer für exportierte XMLs** (optional, später)
- **Client-side Hooks** (per #6295 obsolet — Hooks laufen serverseitig)
- **`bpgit.exe` auf Client-Workstations** (per #6295 — nur `git` lokal nötig)

## Mitgeltende Specs

- [`SPEC-target-environment.md`](./SPEC-target-environment.md) — Windows 11, .NET 10, BP 7.5.1, OpenClawPC
- [`context/SPEC-git-server.md`](../context/SPEC-git-server.md) — **git-server-Architektur, Hooks, Auth, Deployment (autoritativ für Server-Aspekte)**
- [`context/bp-cli-reference-7.5.1.md`](../context/bp-cli-reference-7.5.1.md) — AutomateC.exe CLI-Referenz
- [`context/bp-database-schema.md`](../context/bp-database-schema.md) — BP-Schema-Dokumentation
