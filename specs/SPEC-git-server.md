# SPEC-git-server — Git-konformer Endpoint fuer Blue Prism Adapter

**Status:** v0.4 Draft (Phase-4c + 4b-follow-up + xunit-Tests-Welle + LibGit2Sharp-0.32.0-API-Limitationen + Unified Binary bpgit.exe) -- v0.4 Unified Binary (Martin #6462 -- bpgit.exe + bpgit.json, no .bpgit/)

> ⚠️ **WICHTIG — Hook-Status (Stand 2026-08-30, Workaround-Karte `bp-git-pre-receive-wiring`, Phase 5+):**
>
> Die in §7 spezifizierten Hooks `pre-receive`, `post-receive`, `post-checkout` existieren im Code als Library-Handler in `src/BPGit.Server/GitHttp/{PreReceiveHandler,PostReceiveHandler,PostCheckoutHandler}.cs` (verdrahtet via DI, voll getestet mit 65+ xunit-Tests), sind aber **NICHT** an `GitHttpHandler.HandleReceivePackAsync` / `HandleUploadPackAsync` aufgerufen. Grund: libgit2 0.32.0 hat keine public API für Server-seitiges `receive-pack`. Phase 4b-follow-up (commit `18ec5db`) delegiert deshalb an den nativen `git --stateless-rpc`-Prozess.
>
> **Konkret heißt das für MVP1 (Stand 2026-08-30):**
>
> - `git push` schreibt direkt in den Bare-Repo. Es findet **keine** `/import`-Validierung statt, **kein** Lock-Check, **kein** processid-Lookup. BP-DB wird nicht aktualisiert vom Push.
> - `git pull` fetched nur den letzten gepushten Stand. Es findet **keine** Materialization aus BP-DB statt. BP-Studio-Edits propagieren nicht in Worktrees.
> - Workstation-Shell-Hooks (Phase 2a, Martin #6295) wurden 2026-08-30 vollständig entfernt (Spec §13 umgesetzt).
>
> Workaround: nach jedem BP-Studio-Edit manuell `bpgit pull` auf OpenClawPC. Push läuft durch, aber BP-DB-Update erfolgt **erst** nach PreReceive-Wiring in Phase 5+.
**Datum:** 2026-08-15 (Phase-4c PostReceive/PostCheckout Hooks done + Phase-4b-follow-up git-CLI receive-pack/upload-pack delegation done + xunit-Tests-Welle 12 Test-Commits done + LibGit2Sharp-0.32.0 Issue-#802 workaround fix-kompiliert aber Tests scheitern noch mit "Assert.Single() collection empty", Phase 5+ Diagnose pending)
**Autor:** bpgit-Projekt
**Bezug:** Martin-Direktive #6295, #6313, #6311, #6309, #6307, #6289, #6287, #6285
**Mitgeltend:** `SPEC-target-environment.md`, `SPEC-adapter-architecture.md`, `context/bp-cli-reference-7.5.1.md`, `context/bp-database-schema.md`

---

## 1. Goals & Non-Goals

### Goals

- **Git-konformer Workflow**: User nutzt ausschliesslich Standard-git-Befehle (`clone`, `pull`, `push`, `branch`, `merge`, `log`, `diff`, `status`, `fetch`, `reset`, `revert`).
- **Self-hosted C# Server** auf OpenClawPC (kein IIS, kein Apache). Kestrel + LibGit2Sharp.
- **SSO-Authentifizierung** via Windows-Integrated-Auth (BP-Studio-Login = BP-DB-Login).
- **Atomare BP-DB-Writes** mit korrekten BPAAuditEvents (via AutomateC.exe `/import /forceid`).
- **Pure-XML-Worktree**: keine Metadata-Files (kein `snapshot.json`, kein `folders.json`).
- **Filename = derived**: Worktree-Filename ist `sanitize(BPAProcess.name)`, niemals manuell editierbar.

### Non-Goals

- **Client-side Hooks** (post-checkout/post-merge im Worktree) — obsolet per #6295.
- **Multi-User-Sync** in MVP1 (single-user auf OpenClawPC).
- **Remote-Zugriff ueber Internet** (nur lokal auf OpenClawPC, MVP1).
- **Branch-basiertes Release-Management** via git — kann spaeter via BP-eigene Release-Mechanismen kommen.
- **Manuelle Filename-Renames** — Renames passieren via XML-Content-Edit, nicht via `git mv` (per #6311).

---

## 2. Architektur-Uebersicht

```
+-------------------+    git clone/push/pull       +-------------------------+    AutomateC.exe    +------------------+
|                   |   (HTTP, Win-Auth/SSO)    |                         |    /import /        |                  |
|   Developer       | <------------------------> |   bpgit.exe      |    /forceid /       |  Blue Prism      |
|   Workstation     |     standard-git-protocol  |   (OpenClawPC)          |    /overwrite       |  Database        |
|   (kein bpgit)    |                            |                         | <-----------------> |  (localdb)       |
+-------------------+                            |  - Kestrel HTTP         |    SqlCommand       +------------------+
                                                |  - LibGit2Sharp         |
                                                |  - System.Data.SqlClient|
                                                |  - pre-/post-receive Hooks|
                                                +-------------------------+
```

### Komponenten

1. **Developer Workstation**: Standard `git` CLI oder Git-GUI. Auth via Windows-Integrated-Auth. **KEIN bpgit.exe lokal noetig**.
2. **bpgit.exe** (C#/.NET 10): Kestrel HTTP, LibGit2Sharp fuer Git-Smart-HTTP-Protocol, server-side Hooks fuer BP-Sync.
3. **Blue Prism Database**: SQL Server Express (localdb) auf OpenClawPC.

### Hook-Skizze

- **pre-receive**: Parse `git diff oldrev..newrev -- processes/`, fuer jede Aenderung: processid-Lookup + `/import /forceid /overwrite` (Push → BP-DB).
- **post-receive**: BP-DB pollen, neue XML-Dateien in Bare-Repo schreiben (BP-DB → Push-Confirmation).
- **post-checkout**: BPAProcess lesen, Worktree refresh (Branch-Wechsel).

---

## 3. Worktree-Layout (final, per #6289, #6311)

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
|   +-- config.toml                          # BP connection only (no snapshot)
+-- .git/                                    # Standard git internals
+-- .gitignore                               # excludes .bpgit/, *.bak, temp files
```

### Worktree-Invariante (per #6311)

**Filename = sanitize(BPAProcess.name).xml** — abgeleitet aus dem XML-Root-`name`-Attribut, niemals manuell editierbar.

- BP-Name ist truth (Single Source of Truth).
- Filename wird beim Pull automatisch normalisiert.
- User editiert NUR die XML-Datei (Inhalt), nicht den Filename.
- Manuelle `git mv`-Operationen werden vom Server toleriert, beim naechsten Pull aber rueckgaengig gemacht.

### Filename-Sanitisierung

Windows verbietet `<>:"/\|?*` in Dateinamen, fuehrende/trailing Spaces sind unpraktisch:

```
sanitize(name):
    return re.sub(r'[<>:"/\\|?*]', '_', name).rstrip('. ')
```

**Beispiele:**

| BP-Name | Filename |
|---|---|
| `MS Excel VBO` | `MS Excel VBO.xml` |
| `Utility - Environment` | `Utility - Environment.xml` |
| `Prozess: Test` | `Prozess_ Test.xml` |
| `Path/Test` | `Path_Test.xml` |
| `Trailing. ` | `Trailing.xml` |

### Name-Collision-Handling

Theoretisch, Demo-DB hat 0 Duplicates: bei zwei Processes mit gleichem Namen in derselben Group-Pfad → Suffix ` (guid-short)`, z.B. `MP - Subprocess A (42b5169c).xml`.

**Praktisch irrelevant**, weil `BPAProcess.name` in Blue Prism semantisch eindeutig sein muss (sonst scheitern Studio-Operationen).

---

## 4. processid-Mapping (per #6311)

**Kern-Insight:** `BPAProcess.processid` (Tabellen-PK) ist NICHT in `BPAProcess.processxml` enthalten. XML enthaelt nur Sub-Element-IDs (Main Window, Buttons, Stages). Root-Tag `<process name="...">` hat KEIN id-Attribut.

**Mapping-Loesung:** Lookup zur Laufzeit via DB-Query auf `BPAProcess.name`.

### Processid-Aufloesung pro Operation

| Git-Diff-Status | Alter Name (Filename) | Neuer Name (XML-Root) | BP-Aktion |
|---|---|---|---|
| `M` | "X" | "X" (gleich) | DB-Lookup `WHERE name='X'` → processid → `/import /forceid /overwrite <file>` |
| `M` (Rename via XML) | "Old" | "New" (≠) | DB-Lookup `WHERE name='New'` (0 Treffer), dann `WHERE name='Old'` (1 Treffer) → processid → `/import /forceid /overwrite <file>` (Name + Content-Update in einem Schritt) |
| `A` | — | "New" | DB-Lookup `WHERE name='New'` (0 erwartet) → `/import <file>` (BP legt neuen Prozess an, /forceid NICHT noetig) |
| `D` | "Gone" | — | DB-Lookup `WHERE name='Gone'` → wenn 1 Treffer: Prozess loeschen (BPAAuditEvent sCode=P005 manuell, oder direkt DELETE) |
| `R` (git mv) | "Old" | "New" | Wie Modify-Rename-Fall: processid via old-name-Lookup → `/forceid /overwrite <new-path>`; Pull-Normalisierung schreibt spaeter canonical Filename |

### Rename-Walkthrough (komplett)

**Phase 1 — User renamed via XML im Worktree:**

1. User oeffnet `processes/Objects/Default/Old Name.xml`, aendert `<process name="Old Name">` → `<process name="New Name">`. Speichert unter gleichem Filename.
2. `git add . && git commit -m "rename: Old Name -> New Name" && git push`
3. Server `pre-receive`:
   - `git diff oldrev..newrev -- processes/` zeigt **Modify** auf `Old Name.xml`
   - XML-Root: `<process name="New Name">` → name="New Name"
   - Filename: `Old Name.xml` → alter Name implizit: "Old Name"
   - DB-Lookup `WHERE name='New Name'` → 0 Treffer
   - DB-Lookup `WHERE name='Old Name'` → processid `42b5169c-...`
   - **`/import /forceid 42b5169c-... /overwrite <tmpfile>`**
   - BP aktualisiert `BPAProcess.name="New Name"` + `processxml=<...New Name...>`, schreibt BPAAuditEvent (sCode=P006)
4. Server `post-receive` schreibt canonical Filename:
   - Schreibt `processes/Objects/Default/New Name.xml` (mit neuem XML)
   - Loescht `processes/Objects/Default/Old Name.xml`
   - Git committet dies als **Auto-Rename** (Similarity-Match), History bleibt erhalten

**Phase 2 — User renamed in BP Studio:**

1. User benennt Prozess in BP Studio: "Old Name" → "New Name"
2. BP aktualisiert `BPAProcess.name` + `processxml`, schreibt BPAAuditEvent (sCode=P006)
3. User `git pull`
4. Server `post-checkout` Hook (oder post-receive falls Push-getriggert):
   - Liest `BPAProcess.name="New Name"` → schreibt `New Name.xml`
   - Loescht `Old Name.xml`
   - Git committet als **Auto-Rename** (Similarity-Match)

**Phase 3 — git mv (manuell, unerwuenscht):**

1. User `git mv Old Name.xml Renamed.xml` (kein XML-Content-Edit)
2. Push
3. Server `pre-receive`:
   - Sieht **R100** (pure Rename, kein Content-Change)
   - Alter Name (aus old path): "Old Name"
   - Neuer Name (aus XML-Root): "Old Name" (unveraendert)
   - DB-Lookup `WHERE name='Old Name'` → processid
   - `/import /forceid <pid> /overwrite Renamed.xml`
4. Server `post-receive` normalisiert:
   - Schreibt canonical `Old Name.xml` (weil XML-Name immer noch "Old Name")
   - Loescht `Renamed.xml`
   - `git mv` wird effektiv rueckgaengig gemacht
5. Optional: Warning-Log fuer Diagnostics

---

## 5. Git-Server Stack (Self-hosted C#)

| Schicht | Komponente |
|---|---|
| HTTP-Frontend | **Kestrel** (.NET 10 in-process HTTP-Server) |
| Git-Protocol | **LibGit2Sharp** (managed Git-Library) |
| Auth | **Windows-Integrated-Auth** via Kestrel + Negotiate/NTLM |
| BP-Integration | AutomateC.exe `/import /forceid /overwrite` (writes) + SqlCommand direkt (reads) |
| Config | `C:\bpgit\bpgit-server.json` (DB-Connection, Listen-URL, Hook-Config) |

### Endpoints (git-smart-HTTP)

| Endpoint | Methode | Zweck | Auth |
|---|---|---|---|
| `/info/refs` | GET | git-discovery (refs advertisement) | Win-Auth |
| `/git-upload-pack` | POST | git-fetch/clone | Win-Auth |
| `/git-receive-pack` | POST | git-push | Win-Auth |
| `/bpgit/status` (optional) | GET | BP-Status, Last-Sync-Zeit | Win-Auth |
| `/bpgit/log` (optional) | GET | BPAAuditEvents-Filter | Win-Auth |

### Beispiel: git clone

```bash
# LAN (anderer Rechner im Subnetz):
git clone http://win-user@openclawpc:8181/bp-git
# Lokal auf OpenClawPC selbst (Server laeuft dort):
git clone http://localhost:8181/bp-git
cd bp-git
ls processes/Processes/Default/   # folder-aware materialisiert
```

> **Wichtig**: `0.0.0.0` in der Default-`listenUrls` ist ein **Bind-Adresse** (lauscht auf allen Interfaces). Clients koennen sich nicht zu `0.0.0.0` verbinden — Windows antwortet `Address not available`. Lokal immer `localhost`/`127.0.0.1`, remote den Hostnamen/die IP des Servers.

Server-Flow:
1. Auth via Windows-Integrated-Auth (Domaenen-Credentials)
2. Kestrel handled HTTP-Request, ruft LibGit2Sharp fuer git-smart-HTTP
3. Bare-Repo servt initial git-protocol (refs, upload-pack)
4. Server-Hook post-checkout: `bpgit pull` materialisiert Worktree aus BP-DB (alle Processes + Folder-Layout + canonical Filenames)
5. User erhaelt Worktree mit folder-aware Layout

### Beispiel: git push

```bash
$EDITOR "processes/Processes/Default/MP - Subprocess A.xml"
# aendere XML-Inhalt (z.B. <process name="..."> oder Sub-Elemente)
git add .
git commit -m "Update MP - Subprocess A"
git push
```

Server-Flow:
1. Auth + push via git-receive-pack (LibGit2Sharp)
2. Server-Hook pre-receive: parse `git diff oldrev..newrev` (siehe #4):
   - Pro Aenderung: processid-Lookup, dann `AutomateC.exe /import /forceid /overwrite`
   - BPAAuditEvents wird von BP-Runtime geschrieben (sCode=P006)
3. Server-Hook post-receive: BP-DB pollen, neue XML-Dateien schreiben, alte loeschen (canonical Filename-Normalisierung)

---

## 6. Auth-Modell (MVP1)

**Windows-Integrated-Auth** (SSO via Domaenen-Credentials):

- Kestrel konfiguriert mit `Authentication.Schemes = Negotiate | NTLM`
- BP-Studio-Login = BP-DB-Login (Windows User wird via SSPI an SQL Server weitergereicht)
- bpgit.exe leitet Windows-User an BP-DB weiter (`auth = "sso"`)
- BP-Audit-Log (BPAAuditEvents.gSrcUserID) zeigt den Windows-User

**Keine separate User-Verwaltung** im MVP1.

**MVP1-Limitation**: single-user auf OpenClawPC. Multi-User-Sync erfordert BP-Lizenz-Erweiterung + Locking-Strategie (MVP2+).

---

## 7. Server-Side Hooks

| Hook | Trigger | Aktion | Status |
|---|---|---|---|
| `post-checkout` | nach `git clone` oder `git checkout` | `bpgit pull` materialisiert/refreshed Worktree (canonical Filenames) | **Library vorhanden, NICHT gewired** (`PostCheckoutHandler.cs`, Phase 5+, siehe Workaround-Karte `bp-git-pre-receive-wiring`) |
| `pre-receive` | vor `git push` (Push-Validierung) | parse `git diff`, processid-Lookup, `/import /forceid /overwrite` pro Änderung | **Library vorhanden, NICHT gewired** (`PreReceiveHandler.cs`, voll getestet in `tests/BPGit.Server.Tests/PreReceiveHandlerTests.cs`, Phase 5+) |
| `post-receive` | nach erfolgreichem Push | BP-DB pollen, canonical Filenames schreiben, alte Files löschen | **Library vorhanden, NICHT gewired** (`PostReceiveHandler.cs`, Phase 5+) |


> **Status-Disclaimer:** Hooks in der rechten Spalte als "Library vorhanden, NICHT gewired" markiert. Die Handler existieren als Libraries + sind via DI-Singleton verdrahtet, werden aber von `GitHttpHandler.HandleReceivePackAsync` / `HandleUploadPackAsync` **nicht aufgerufen**. Grund: libgit2 0.32.0 hat keine public Server-side receive-pack-API; Phase 4b-follow-up delegiert deshalb an nativen `git --stateless-rpc`. `git push` schreibt direkt ins Bare-Repo, `git pull` lädt nur den letzten gepushten Stand, kein BP-DB-Read. Workaround: manuell `bpgit pull` auf OpenClawPC. Siehe Disclaimer am Doc-Anfang und Workaround-Karte `bp-git-pre-receive-wiring` (Phase 5+).
**Hook-Implementierung**: C# DelegatedHandler in bpgit.exe (NICHT Shell-Scripts — bessere Testbarkeit, typsicherer).

### Beispiel: pre-receive (Pseudocode)

```csharp
async Task<PreReceiveResult> HandlePreReceiveAsync(string oldrev, string newrev, string refname)
{
    var diff = await _gitService.GetDiffAsync(oldrev, newrev, "processes/");
    foreach (var change in diff)
    {
        switch (change.Status)
        {
            case "M":
                var xmlContent = await _gitService.GetBlobAsync(newrev, change.NewPath);
                var newName = ExtractProcessName(xmlContent);
                var oldName = Path.GetFileNameWithoutExtension(change.OldPath);
                var processId = await _bpService.LookupProcessIdAsync(newName)
                                ?? await _bpService.LookupProcessIdAsync(oldName);
                if (processId is null) { /* Skip or Error */ continue; }
                await _bpService.ImportAsync(processId.Value, xmlContent, force: true);
                break;

            case "A":
                var addName = ExtractProcessName(await _gitService.GetBlobAsync(newrev, change.NewPath));
                await _bpService.ImportNewAsync(addName, xmlContent);
                break;

            case "D":
                var deleteName = Path.GetFileNameWithoutExtension(change.OldPath);
                var deleteId = await _bpService.LookupProcessIdAsync(deleteName);
                if (deleteId is not null) await _bpService.DeleteAsync(deleteId.Value);
                break;

            case "R":
                // Wie M mit Rename-Logik (old-name → new-name)
                // Server-Pull normalisiert spaeter auf canonical Filename
                break;
        }
    }
    return PreReceiveResult.Ok();
}
```

### Hook-Implementierung: post-receive (Pseudocode)

```csharp
async Task HandlePostReceiveAsync(string oldrev, string newrev, string refname)
{
    await _bpService.SyncToGitAsync();  // BPAProcess lesen, canonical Filenames schreiben
    // Git erkennt Auto-Renames automatisch (Similarity-Match)
}
```

---

## 8. Snapshot-Format

**Kein Snapshot im Worktree.** Processid-Mapping erfolgt zur Laufzeit via DB-Lookup.

**Kein `.bpgit/snapshot.json`** im Worktree, **kein `.bpgit/folders.json`**.

`config.toml` (in `.bpgit/`) enthaelt nur BP-Connection-Config (kein processid-Tracking):

```toml
[bp]
server = "(localdb)\\BluePrismLocalDB"
database = "BluePrism"
auth = "sso"  # Windows-Integrated-Auth

[paths]
processes_root = "processes"  # Wo XML-Dateien im Worktree liegen
```

**Kein Bloat im Worktree** — pure git, pure XML.

---

## 9. BP-Synchronization-Flow

### Pull-Flow (git clone, git pull)

`post-checkout` Hook (oder initial-clone Handler) ruft `WorktreeSyncService.MaterializeAsync(targetRoot, ct)` auf. Phase-4c-Algorithmus (Commits `d2fd04f`, `f7dc718`):

1. SqlCommand gegen `BPAProcess` (alle Rows, liest `name` + `processxml`)
2. SqlCommand gegen `BPATree` (Filter: `id IN (2, 3)` — nur Processes + Objects; andere Trees per #6287 ausgeschlossen)
3. SqlCommand gegen `BPAGroup` (fuer Trees 2, 3)
4. SqlCommand gegen `BPAGroupProcess` (M:N-Mapping)
5. **Snapshot existing XML files** unter `targetRoot` (fuer stale-Detection, kein Full-Reinit)
6. Pro Process:
   - Skip wenn `name` leer oder keine Folder-Membership
   - **Sanitize filename** via `Path.GetInvalidFileNameChars()` + `TrimEnd('.', ' ')` — deckt ALLE Windows-inkompatiblen Zeichen ab inkl. / \ : * ? " < > |
   - **StripLeadingXmlComments** vor jedem Write (per #6277, BP `/import`-Parser bricht sonst mit "Failed to create ... already exists" ab)
   - **M:N-Duplikation**: Process in mehreren Groups → File in jedem Folder
   - Path = `<TreeName>/<GroupName>/<sanitized(name)>.xml`
   - Write XML zu worktree — Skip wenn Content identisch (kein Re-Write noetig)
7. **Delete stale XML files**: alles in Snapshot aber nicht in kept-set loeschen (Renames/Deletes in BP-DB propagieren automatisch in Worktree)

**Performance-Hinweis** (per Martin #6285): NIEMALS `AutomateC.exe /export` fuer Pull — zu langsam. SqlCommand direkt ist Pflicht.

**Worktree-Invariante** (per Martin #6311): `filename = sanitize(BPAProcess.name) + ".xml"` — derived, niemals manuell editierbar. Worktree enthaelt pure XML + git (kein `snapshot.json`, kein `folders.json`, keine Registry).

### Push-Flow (git push)

**Architektur-Update Phase 4b-follow-up**: Smart-HTTP receive-pack delegiert an `git -C <bare-repo> receive-pack --stateless-rpc` (libgit2 0.32.0 hat keine public Server-seite fuer receive-pack; native CLI delegiert pkt-line parsing + ref-update + pack-index/apply + report-status). pre-receive laeuft aktuell als **side-effect post-apply**, nicht pre-emptive validate-then-apply.

`pre-receive` Hook (siehe #7 Pseudocode) ist Pre-Receive-Validation:

1. Parse `git diff oldrev..newrev -- processes/` (LibGit2Sharp manueller Tree-Walker)
2. Pro Aenderung (M/A/D/R) → processid-Lookup + `AutomateC.exe /import /forceid <guid> /overwrite`
3. Bei Fehler (Lock/Conflict): Push ablehnen mit klarer Fehlermeldung + Owner-Info
4. Bei Erfolg: `BPAAuditEvents` mit `sCode=P006` wird automatisch von BP-Runtime geschrieben

`post-receive` Hook:

1. Ruft `WorktreeSyncService.MaterializeAsync(targetRoot)` auf (gleicher Service wie `post-checkout`)
2. BPAAuditEvents wurden bereits geschrieben (pre-receive)
3. Server-seitige Auto-Rename-Erkennung via `git diff --find-renames` (alter Pfad-Name → neuer XML-`process name`)

### Initial-Push (leeres Repo)

1. `bpgit init` (server-side Admin-Tool) erstellt Bare-Repo + Bare-Repo-Worktree mit folder-aware Layout
2. `git add . && git commit -m "Initial import"`
3. User clone: `git clone http://openclawpc:8181/bp-git` (lokal auf OpenClawPC: `git clone http://localhost:8181/bp-git`)

---

## 10. Conflicts & Lock-Handling

### Optimistic Locking via lastmodifieddate (MVP1)

- `pre-receive` Hook liest `BPAProcess.lastmodifieddate` zum Zeitpunkt des letzten Pulls (aus serverseitigem Cache, optional)
- Vergleicht mit aktuellem Wert in BPAProcess
- Wenn abweichend → Konflikt → Push ablehnen mit Hinweis "BP process was modified outside bpgit, please pull first"

### BPAProcessLock

- Vor `pre-receive` → Check `BPAProcessLock.userid`
- Wenn Lock vorhanden und nicht von current-user → Lock-Owner anzeigen, Push ablehnen
- Optional: `--force` Flag fuer Admin-Override

### Multi-User (MVP2, nicht MVP1)

- Pessimistic Locking via `BPAProcessLock` (BP-Studio setzt automatisch beim Edit)
- bpgit.exe respektiert Locks
- Konflikt-Resolution: User A locked, User B wartet oder benutzt `--force`

---

## 11. CLI-Reduktion (per #6295)

### Subcommands — End-State

| Subcommand | Status | Zweck |
|---|---|---|
| `bpgit server start` | Admin | Startet bpgit.exe (Kestrel) |
| `bpgit server stop` | Admin | Stoppt bpgit.exe |
| `bpgit server status` | Admin | Server-Health (letzte Pull-Zeit, pending Hooks) |
| `bpgit init` | Admin | Initialisiert Bare-Repo auf Server (einmalig) |
| `bpgit pull` | Internal | Server-side Materialization (von Hook aufgerufen) |
| `bpgit log` | Diagnostic | BPAAuditEvents aus BP-DB (per-User-Audit) |
| `bpgit status` | Deprecated | Nutze stattdessen `git status` (Worktree-vs-Snapshot-Drift-Detection ist jetzt Standard-`git`-Funktionalität) |
| `bpgit diff` | Deprecated | Nutze stattdessen `git diff` (Hash-basierter Drift-Report entspricht dem nativen `git diff` für den BP-XML-Worktree) |
| `bpgit commit` | Deprecated | Nutze stattdessen `git push` -- **aber: in MVP1 hat `git push` KEINE Server-seitige `/import`-Validierung** (Hooks nicht gewired, Phase 5+, Karte `866e5346`). Bis dahin manuell `bpgit status` + `bpgit pull` triggern um Worktree-Synchronisation zu erzwingen. |
| `bpgit hook install` | **Obsolet** | Server-side Hooks via bpgit.exe (kein Shell-Script noetig) |

### CLI-Executable

- `bpgit.exe` bleibt im PATH **nur auf dem Server** (OpenClawPC)
- User benoetigt KEIN `bpgit.exe` lokal — nur `git`

---

## 12. Deployment (MVP1)

### Voraussetzungen

- Windows 10/11 (OpenClawPC, gleicher Rechner wie BP Studio)
- BP Studio + `(localdb)\BluePrismLocalDB` installiert
- Git for Windows (fuer Clients — kein git-http-backend noetig, alles in C#)
- .NET 10 SDK + ASP.NET Core Runtime
- bpgit.exe Binary (self-contained .NET 10 Publish)

### Schritte

1. **bpgit.exe installieren** nach `C:\bpgit\bin\bpgit-server.exe`
2. **Konfiguration** in `C:\bpgit\bpgit-server.json`:
   ```json
   {
     "ListenUrls": ["http://0.0.0.0:8181"],
     "BpServer": "(localdb)\\BluePrismLocalDB",
     "BpDatabase": "BluePrism",
     "BpAuth": "sso",
     "RepoRoot": "C:\\bpgit\\repos",
     "RepoName": "bp-git"
   }
   ```

   > **Bind vs. Connect**: `0.0.0.0:8181` hoert auf allen Interfaces, ist aber kein verbindbarer Host. Lokale Tests: `http://localhost:8181`. LAN: `http://<hostname>:8181` oder die IP direkt. Fuer produktiven Einsatz kann die Default-Liste z.B. auf `["http://10.0.0.5:8181"]` gehaertet werden.
3. **Bare-Repo initialisieren**:
   ```bash
   cd "C:/bpgit/repos"
   bpgit-server init bp-git   # erstellt bare repo + initial materialization
   ```
4. **bpgit.exe starten** als Windows-Service oder manuell:
   ```bash
   bpgit-server start
   ```
5. **Windows-Firewall**: Port 8181 (oder gewaehlter Port) fuer lokales Subnetz freigeben.

### Beispiel-Aufruf (Developer)

```bash
# Initial clone (einmalig)
git clone http://win-user@openclawpc:8181/bp-git
cd bp-git

# Worktree ist folder-aware materialisiert
ls processes/Processes/Default/

# Edit + push (Standard-git)
$EDITOR "processes/Processes/Default/MP - Subprocess A.xml"
# aendere nur XML-Inhalt, NICHT den Filename
git add .
git commit -m "Update MP - Subprocess A"
git push  # server-side bpgit.exe pre-receives und ruft /import /forceid

# Pull (Standard-git, refresht von BP-DB)
git pull  # server-side post-checkout materialisiert Updates + canonical Filenames
```

---

## 13. Migration Path

### Bestehende CLI-User → Git-Server

1. **Git-Server deployen** (per #12)
2. **Initial-Repo erstellen**: `bpgit-server init bp-git` → Bare-Repo mit folder-aware Layout
3. **Andere User migrieren**: `git clone http://openclawpc:8181/bp-git` → Worktree wird materialisiert. Lokale Tests auf OpenClawPC selbst: `git clone http://localhost:8181/bp-git`.

### Ein-Weg-Migration

- CLI-Workflow wird deprecated (nicht entfernt)
- Neue Projekte starten direkt mit Git-Server
- Bestehende Projekte migrieren schrittweise

### Phase 2c ist obsolet

Da Hooks server-side laufen, entfaellt die komplette `bpgit hook install`-Implementation (Card `98e9d43f-...` ist nie gebaut worden). `--install-hooks`-Flag wurde 2026-08-31 vollständig aus `InitCommand` und CLI-Parser entfernt (commit siehe `AGENTS.md`-Decisions-Tabelle).

---

## 14. Open Questions

| Frage | Kontext | Entscheidung noetig |
|---|---|---|
| HTTPS statt HTTP? | Remote-Zugriff noetig? | nach MVP1 |
| Port-Wahl | 8181 (BP-Default Resource-PC) vs 80/443 | Martin |
| Multi-User-MVP2? | BP-Lizenz erlaubt concurrent users? | nach MVP1 |
| Lock-Strategie | Optimistic vs pessimistic | MVP1: optimistic via lastmodifieddate |
| Branch-Strategie | main + feature-branches? | Standard-git, User-Entscheidung |
| Tag-Strategie | Tags fuer Releases? | Optional, Git-Standard |
| Release-Integration mit BPARelease | git tag → BPARelease? | Nicht MVP1 |
| Filename-Conflict-Strategie bei Rename + Edit | Similarity < 50% | Martin: ggf. -M30 Threshold, oder User-Commit-Marker |

---

## 15. Implementation Roadmap

| Schritt | Status | Aufwand |
|---|---|---|
| SPEC-git-server.md (dieses Dokument) | done | — |
| SPEC-adapter-architecture.md updaten (Worktree-Layout + processid-Mapping) | offen | 30 min |
| README-bpgit-git.md (End-User-Doku) | offen | 1 h |
| AGENTS.md Status-Update | offen | 10 min |
| Workboard-Cards fuer Doku-Review + Impl-Phasen | offen | 20 min |
| **bpgit.exe** Implementation in C# (.NET 10) | offen | 1-2 Wochen |
| - Kestrel HTTP + Win-Auth | | |
| - LibGit2Sharp git-smart-HTTP | | |
| - pre-receive Hook (processid-Lookup + /import) | | |
| - post-receive Hook (BP-DB-Sync + canonical Filenames) | | |
| - post-checkout Hook (Worktree-Materialization) | | |
| MVP1-Deployment auf OpenClawPC | offen | 1 Tag |
| End-to-End-Test: clone → edit → commit → push → verify in BP Studio | offen | 1-2 Tage |
| Cleanup Demo-DB (1 zusaetzliche BPARelease-Row aus /importrelease-Test) | offen | 10 min |

---

## 16. References

- **Martin-Direktive #6313** (21:49): Erst Doku/Specs schreiben, danach implementieren
- **Martin-Direktive #6311** (21:42): Filename ist abgeleitet aus XML-Name, nicht manuell editierbar
- **Martin-Direktive #6309** (21:25): processid-Mapping via git-diff (alte Filenames verfuegbar via R-Status)
- **Martin-Direktive #6307** (20:57): UI-Import ohne /forceid (Name-Lookup), CLI-Import mit /forceid moeglich
- **Martin-Direktive #6295** (18:17): Git-Server > CLI + Hooks, self-hosted
- **Martin-Direktive #6289** (17:28): Worktree-Layout (kein meta.json, filename = process.name, kein per-Process-Subfolder)
- **Martin-Direktive #6287** (16:37): Folder-Struktur + git-server lokal auf BP-Studio-Maschine (SSO moeglich)
- **Martin-Direktive #6285** (16:29): Folder-Struktur existiert, Initial-Pull DB-direct (kein CLI-Export)
- **Martin-Direktive #6277** (12:43): `StripLeadingXmlComments` Helper (Leading-Comments brechen BP's /import-Parser)
- **Martin-Direktive #6274** (11:42): bpgit commit via AutomateC.exe /import (audit-konform)
- **Martin-Direktive #6271** (10:00): CLI-Doku 7.5.1 in context/
- `SPEC-target-environment.md` (OpenClawPC, .NET 10, git, BP)
- `SPEC-adapter-architecture.md` (DB-direct write, BP-Cli-Bridge-Architecture)
- `context/bp-cli-reference-7.5.1.md` (AutomateC.exe CLI-Referenz)
- `context/bp-database-schema.md` (BP-Schema-Dokumentation)
- Empirische Befunde:
  - `processid` ist NICHT in XML (siehe `temp/probe-xml-processid.ps1`)
  - BPAAuditEvents.oldXML + newXML vorhanden (fuer Historie)
  - BP-LocalDB nutzt native Auth (`auth = "user"`), nicht SSO
