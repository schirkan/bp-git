# SPEC — BP-Git-Adapter-Architektur

**Stand:** 2026-10-26 (v7 — Server-only binary, obsolete CLI/Data/Format projects removed)
**Status:** Worktree-Layout final (per #6311), processid-Mapping via SQL-Lookup `BPAProcess.name`; pre-/post-receive as C# in the server HTTP handler; no client CLI or hooks.
**Bezieht sich auf:** [SPEC-target-environment.md](./SPEC-target-environment.md), [specs/SPEC-git-server.md](../specs/SPEC-git-server.md), [context/bp-database-schema.md](../context/bp-database-schema.md)

## Ziel

Git-konformer Read/Write-Adapter für Blue Prism (BP) v7.5. XML-Repräsentationen von Processes / Objects werden im Dateisystem sicht- und editierbar; Versionsverwaltung über Standard-Git-Befehle. Der Adapter läuft als self-hosted C#-Server (Kestrel + LibGit2Sharp) auf OpenClawPC — kein IIS, kein Apache, kein bpgit-CLI auf Developer-Workstations.

> **Detaillierte Server-Architektur, Pre-/Post-Receive-Logik im HTTP-Handler, Deployment**: siehe [`specs/SPEC-git-server.md`](../specs/SPEC-git-server.md) §7. Dieses Dokument beschreibt die **Adapter-Domain-Logik** (Worktree-Layout, processid-Mapping, XML-Serialisierung, Sanitisierung).

## High-Level-Architektur

```
+-------------------+    git clone/push/pull       +-------------------------+    AutomateC.exe    +------------------+
|                   |   (HTTP, Win-Auth/SSO)    |                         |    /import /        |                  |
|   Developer       | <------------------------> |   bpgit.exe      |    /forceid /       |  Blue Prism      |
|   Workstation     |     standard-git-protocol  |   (OpenClawPC)          |    /overwrite       |  Database        |
|   (kein bpgit)    |                            |                         | <-----------------> |  (localdb)       |
+-------------------+                            |  - Kestrel HTTP         |    SqlCommand       +------------------+
                                                |  - LibGit2Sharp         |
                                                |  - pre-/post-receive Handler im HTTP-Lifecycle (kein Git-Hook-Script, Stand 2026-09-10)|
                                                +-------------------------+
```

**Developer Workstation** führt ausschliesslich Standard-git aus — kein `bpgit.exe`, keine Hooks, kein BP-CLI. **bpgit.exe** auf OpenClawPC hält die BP-DB-Verbindung und führt alle Pre-/Post-Receive-Logik als C# im HTTP-Handler aus (Stand 2026-09-10: keine Git-Hook-Scripts mehr).

## Komponenten (.NET 10, C# 13)

### 1. bpgit.exe (`BPGit.Server`)

Kestrel-basierter HTTP-Server mit LibGit2Sharp für git-smart-HTTP-Protocol:

| Modul                | Aufgabe                                                                                   |
| -------------------- | ----------------------------------------------------------------------------------------- |
| `KestrelListener`    | HTTP-Listener auf konfigurierbarem Port (Default 8181)                                    |
| `WindowsAuthHandler` | Negotiate/NTLM-Authentifizierung                                                          |
| `GitHttpHandler`     | git-smart-HTTP (`/info/refs`, `/git-upload-pack`, `/git-receive-pack`) via LibGit2Sharp   |
| `PreReceiveHandler`  | Processid-Lookup via SQL (`BpDbService.LookupProcessIdByNameAsync`) + `AutomateC.exe /import /forceid /overwrite` — C# im HTTP-Handler |
| `PostReceiveHandler` | BP-DB → canonical Filenames schreiben (`WorktreeSyncService.MaterializeAsync`) — C# im HTTP-Handler |
| ~~`PostCheckoutHandler`~~ | **gestrichen 2026-09-10** — Worktree-Materialization entfällt, Filename = `sanitize(BPAProcess.name) + ".xml"` per #6311 ist bereits kanonisch |
| `BpDbService`        | SqlCommand-Zugriff auf BPAProcess + BPATree + BPAGroup + BPAGroupProcess + BPAAuditEvents |
| `AutomateCRunner`    | Process.Start-Wrapper für AutomateC.exe `/import /importrelease /export`                  |

### 2. Data models (`BPGit.Data`)

The data project contains shared server configuration and the folder DTOs (`Tree`,
`Group`, `ProcessMembership`, `FolderStructure`). `BpDbService` performs database
access directly with `Microsoft.Data.SqlClient`; the former Dapper repository and
standalone `BPGit.Format` serializer were unused and have been removed.

### 3. XML processing (`BPGit.Server`)

`WorktreeSyncService` writes the `BPAProcess.processxml` content to canonical XML
files, strips leading comments when required by BP import, and sanitizes filenames.
There is no separate XML serializer project.

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
+-- .git/                                    # Standard git internals
+-- .gitignore                               # XML-only allow-list, generated by server init
```

### Worktree-Invariante (per #6311)

**`filename = sanitize(BPAProcess.name) + ".xml"`** — abgeleitet aus dem XML-Root-`name`-Attribut.

- BP-Name ist Single Source of Truth.
- Vor Fetch/Push synchronisiert `BpToGitSyncService` die BP-Datenbank; kanonische Filenames erscheinen als Commit im Default-Branch.
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
| `D`                  | "Gone"                | —                     | Prozesslöschung via Git ist nicht implementiert; der BP-Prozess wird beim nächsten DB→Git-Abgleich wieder materialisiert. |
| `R` (git mv)         | "Old"                 | "New"                 | Wird als Delete + Add behandelt; `git mv` ist kein unterstützter Rename-Workflow. |

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
4. Server `post-receive` materialisiert den neuen DB-Namen in den Server-Worktree.
5. Beim nächsten `git pull` erzeugt der DB→Git-Abgleich einen Commit mit `New Name.xml` und entfernt `Old Name.xml` aus dem Default-Branch.

**Phase 2 — User renamed in BP Studio:**

1. User benennt Prozess in BP Studio: "Old Name" → "New Name"
2. BP aktualisiert `BPAProcess.name` + `processxml`, schreibt BPAAuditEvent (sCode=P006)
3. User `git pull`; der Server übernimmt den DB-Stand als serverseitigen Commit mit dem kanonischen Filename.
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

## Bridge-Architektur (Git ↔ BP-DB)

`BPGit.Server` verbindet Standard-Git mit der Blue-Prism-Datenbank:

- Git-Clients kommunizieren ausschließlich über Smart-HTTP.
- Nach einem erfolgreichen `receive-pack` importiert der Server geänderte XML-Prozesse in BP.
- `PostReceiveHandler` materialisiert den aktuellen BP-Datenbankstand in den konfigurierten **Server-Worktree**; das ist kein clientseitiger Pull-Hook.
- Vor der Ref-Anzeige für `clone`, `fetch` und `pull` sowie vor Receive-Pack synchronisiert `BpToGitSyncService` den BP-Datenbankstand in den Default-Branch. Abweichungen werden als Commit mit der Server-Identität `bpgit-server` übernommen.
- `bpgit init` erzeugt ein Bare-Repo mit initialem `.gitignore`-Commit; der erste Fetch materialisiert den aktuellen BP-Prozessbestand automatisch.

### Naming-Strategie

Prozessidentität wird über den Namen im XML und den Datenbank-Lookup auf `BPAProcess.name` abgeleitet. `WorktreeSyncService` schreibt serverseitige Materialisierungen unter `sanitize(BPAProcess.name) + ".xml"`; es ändert keine Git-Objekte oder bereits committeten Client-Pfade.

### Sync-Grenzen

- Die BP-Synchronisierung nach einem Push läuft nach dem Git-Ref-Update; ein Importfehler kann den Push nicht ablehnen oder zurückrollen.
- Vor dem Receive-Pack wird BP→Git aktualisiert, sodass ein Push auf einen veralteten Default-Branch-Ref von Git abgelehnt wird.
- BP ist beim nächsten Fetch maßgeblich: Abweichungen zwischen Git und der Datenbank werden dann als Git-Commit aus dem aktuellen DB-Stand abgeglichen.
- Prozesslöschungen aus Git werden nicht in BP ausgeführt; `DeleteAsync` ist absichtlich nicht implementiert.
- Es gibt keinen Client-CLI-Befehl, kein `--force`-Override und keinen clientseitigen BP-Datenbankzugriff.

### VS-Code-Integration

Developer öffnen den Git-Worktree in VS Code und verwenden die Standard-Git- und XML-Funktionen. Eine eigene VS-Code-Extension ist nicht vorgesehen.

## Datenfluss

### Fetch-Flow (git clone, git fetch, git pull)

1. Bei `GET /info/refs?service=git-upload-pack` liest `BpToGitSyncService` BP-Prozesse und Folder-Struktur.
2. `WorktreeSyncService` materialisiert die Datenbank in den Server-Worktree.
3. Bei Änderungen staged der Server nur `processes/`, erzeugt einen Commit auf dem Default-Branch und aktualisiert den Ref atomar.
4. Erst danach werden Refs annonciert und `git-upload-pack` liefert den synchronisierten Stand aus.

Der erste Clone erhält damit den aktuellen BP-Prozessbestand, ohne manuellen Export oder Client-CLI.

### Push-Flow (git push)

1. Vor dem Empfang gleicht `PushOrchestrator` BP→Git ab; ein veralteter Default-Branch-Ref wird durch `git receive-pack` abgelehnt.
2. `git receive-pack --stateless-rpc` wendet akzeptierte Pack- und Ref-Updates an.
3. `PushOrchestrator` ruft danach `PreReceiveHandler` für geänderte Ref-Updates auf; XML-Änderungen unter `processes/` werden zu BP-Importen.
4. Importfehler werden protokolliert. Bereits angewendete Git-Ref-Updates bleiben bestehen.
5. `PostReceiveHandler` materialisiert anschließend den aktuellen BP-Datenbankstand in den Server-Worktree; beim nächsten Fetch gleicht BP→Git Abweichungen ab.

---
## Sicherheitsgrenzen

- **Credentials ausschließen:** `BPACredentials`, `BPAKeyStore`, `BPAPassword`, alle Spalten mit `encryptid` als FK — niemals in Worktree ausgeben (Whitelist via `ignore_tables` in `config.toml`).
- **Kein bpgit.exe auf Client-Workstations** — nur Server (OpenClawPC) hat DB-Credentials + AutomateC.exe.
- **Windows-Integrated-Auth** für Server-Zugriff — BP-DB-Login = Windows-User (Audit-Trail via BPAAuditEvents.gSrcUserID).
- **Kontrollierte Mutationen** — Git→BP-Importe erfolgen nach `git push`; BP→Git-Commits werden vor Ref-Anzeige bei `clone`, `fetch` und `pull` erzeugt.
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
- [`specs/SPEC-git-server.md`](../specs/SPEC-git-server.md) — **git-server-Architektur, Hooks, Auth, Deployment (autoritativ für Server-Aspekte)**
- [`context/bp-cli-reference-7.5.1.md`](../context/bp-cli-reference-7.5.1.md) — AutomateC.exe CLI-Referenz
- [`context/bp-database-schema.md`](../context/bp-database-schema.md) — BP-Schema-Dokumentation
