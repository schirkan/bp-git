# SPEC-git-server — Git-konformer Endpoint fuer Blue Prism Adapter

**Status:** v1.0 (post-CLI-Reduktion, Hooks-stripped, single-server-architecture). Architektur final: Pre-/Post-Receive-Logik als C# im bpgit-server HTTP-Handler, post-checkout gestrichen (Commit `82a5e84`), CLI reduziert auf `bpgit init` (Commit `cbcb604`). Server macht BP-DB-Sync atomar mit Push via `/import /forceid /overwrite`.

**Datum:** 2026-09-11 (Architektur-Finalisierung: CLI auf `init` reduziert, Hooks als C# im HTTP-Handler, Cache obsolet, post-checkout gestrichen, BpSyncService.ImportAsync via separatem Server-AutomateCRunner)
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
4. `git-upload-pack` liefert canonical-named Files direkt an den Client (kein server-seitiges Post-Checkout mehr, siehe §9 + §11)
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

## 7. Pre-/Post-Receive-Logik im bpgit-server (keine Git-Hook-Scripts)

**Architektur-Wechsel (Stand 2026-09-10):** Keine Git-Hook-Scripts in `<bare-repo>/hooks/`, keine `core.hooksPath`, kein `bpgit install-hooks`. **Alle Logik läuft als C# im `bpgit-server`-HTTP-Handler**, typsicher, atomar mit dem HTTP-Request, eine Codebase, eine Teststrategie.

| Handler | Trigger | Aktion | Status |
|---|---|---|---|
| `PreReceiveHandler` | in HTTP-Handler vor ref-apply (side-effect post-apply per Spec §9) | parse `git diff`, name-Lookup via SQL, `/import /forceid /overwrite` pro Änderung | ✅ **wired** (`GitHttpHandler.HandleReceivePackAsync` ruft `PreReceiveHandler.HandleAsync` pro ref-update auf, **kann Push nicht ablehnen** bei BP-DB-Sync-Fehler — siehe MVP-1-Limitation) |
| `PostReceiveHandler` | in HTTP-Handler nach ref-apply | BP-DB-Sync (`WorktreeSyncService.MaterializeAsync` — canonical-Filenames schreiben, stale-Files löschen) | ✅ **wired** (`GitHttpHandler.HandleReceivePackAsync` ruft `PostReceiveHandler.HandleAsync` nach ref-apply + PreReceive auf) |
| ~~`PostCheckoutHandler`~~ | — | — | **gestrichen 2026-09-10** — Filename = `sanitize(BPAProcess.name) + ".xml"` per #6311, kein Client-Rename nötig, kein post-checkout-Hook mehr. File `PostCheckoutHandler.cs` kann gelöscht werden. |


> **Status-Disclaimer:** Die Handler sind im HTTP-Lifecycle verdrahtet (nicht als Git-Hook-Scripts). `PushOrchestrator` sitzt zwischen HTTP-Request-Body und `git receive-pack --stateless-rpc`: parst Ref-Update-Pkt-Lines, ruft nach ref-apply `PreReceiveHandler.HandleAsync` pro Ref-Update (außer Delete → übersprungen, solange `BpSyncService.DeleteAsync` NotImplemented), dann `PostReceiveHandler.HandleAsync` für Worktree-Materialization. Side-effect post-apply per Spec §9: Pre-Receive **kann** Push nicht ablehnen, wenn BP-DB-Sync fehlschlägt — Konsistenz-Garantie über BP-DB-Sync + stderr-Log, nicht über Push-Reject. Side-band-64k + ofs-delta Pack-Encoding bleibt obsolet (Hybrid-Ansatz aus §1.3 verworfen 2026-09-10, Spec §2/§3 in SPEC-pre-receive-wiring.md als "obsolete" markiert).

**Implementierung:** C# DelegatedHandler in bpgit.exe (NICHT Shell-Scripts — bessere Testbarkeit, typsicherer, atomare Transaktion mit HTTP-Request).

**Warum keine Git-Hook-Scripts:**

- Kein zusätzlicher Installations-Schritt beim `bpgit-server init` (nur Bare-Repo anlegen)
- Kein `core.hooksPath`-Konflikt
- Eine Codebase (C#), eine Teststrategie
- Atomare Transaktion: HTTP-Request-Lifecycle umfasst Validation + Apply + Post-Sync
- `post-checkout` ist obsolet, weil das Filename-Format bereits kanonisch ist (per #6311)

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

**Stand 2026-09-11:** **Single-Stage.** CLI auf nur `init` reduziert (siehe §11), post-checkout komplett entfällt:

1. **Single Stage:** `git clone` / `git pull` ruft `git-upload-pack` auf dem Server. Server liefert Pack + Refs. Client-Worktree enthaelt **kanonisch benannte XML-Files** (per #6311, weil Filename = `sanitize(BPAProcess.name) + ".xml"` und der Developer diese Files bereits so commited hat). **BP-DB ist bereits synchron** - der pre-receive `/import /forceid` hat den BP-DB-Stand bei jedem Push mit den Git-Objekten gleichgezogen, daher bekommt der Client auf Pull die Files, die exakt dem aktuellen BP-DB-Stand entsprechen.

Kein Client-seitiger Sync noetig. Kein post-checkout. Kein `bpgit pull`. Kein `bpgit refresh`. Nur Standard-`git`.

(Falls die lokale BP-DB jemals neuer ist als der Git-Stand - z.B. durch direkte BP-Studio-Edits ohne Push - ist das ein **Anti-Pattern** in dieser Architektur. Die korrekte Vorgehensweise ist: Export aus BP Studio -> Commit -> Push. Der pre-receive macht den Rest.)


1. **Stage 1 (git-seitig):** `git clone` / `git pull` ruft `git-upload-pack` auf dem Server. Server liefert Pack + Refs. Client-Worktree enthält **kanonisch benannte XML-Files** (per #6311, weil Filename = `sanitize(BPAProcess.name) + ".xml"` und der Developer diese Files bereits so commited hat).
**Performance-Hinweis** (per Martin #6285): NIEMALS `AutomateC.exe /export` fuer Pull - zu langsam. SqlCommand direkt ist Pflicht.

**Worktree-Invariante** (per Martin #6311): `filename = sanitize(BPAProcess.name) + ".xml"` - derived, niemals manuell editierbar. Worktree enthaelt pure XML + git (kein `snapshot.json`, kein `folders.json`, keine Registry).

**Hinweis seit CLI-Reduktion (2026-09-11):** `git clone` ohne weitere Schritte liefert den vollstaendigen Stand. BP-DB-Sync passiert automatisch im pre-receive bei jedem Push. Kein Client-Workflow braucht BP-DB-Zugriff.

### Push-Flow (git push)

**Architektur-Update Phase 4b-follow-up**: Smart-HTTP receive-pack delegiert an `git -C <bare-repo> receive-pack --stateless-rpc` (libgit2 0.32.0 hat keine public Server-seite fuer receive-pack; native CLI delegiert pkt-line parsing + ref-update + pack-index/apply + report-status). pre-receive laeuft aktuell als **side-effect post-apply**, nicht pre-emptive validate-then-apply.

`pre-receive` Hook (siehe #7 Pseudocode) ist Pre-Receive-Validation:

1. Parse `git diff oldrev..newrev -- processes/` (LibGit2Sharp manueller Tree-Walker)
2. Pro Aenderung (M/A/D/R) → processid-Lookup + `AutomateC.exe /import /forceid <guid> /overwrite`
3. Bei Fehler (Lock/Conflict): Push ablehnen mit klarer Fehlermeldung + Owner-Info
4. Bei Erfolg: `BPAAuditEvents` mit `sCode=P006` wird automatisch von BP-Runtime geschrieben

`post-receive` Hook:

1. Ruft `WorktreeSyncService.MaterializeAsync(targetRoot)` auf (server-seitiges Monitoring; nicht der Import-Pfad, der laeuft im pre-receive via `BpSyncService.ImportAsync`)
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

## 11. CLI-Reduktion (per #6295, final 2026-09-11)

### Subcommands - End-State

Nach Architektur-Switch zu server-side BP-DB-Sync (`PreReceiveHandler` macht `/import /forceid /overwrite` atomar mit jedem Push) ist die CLI auf **ein einziges Subcommand** reduziert — alle anderen Funktionen laufen über die Git-Smart-HTTP-API. Der Server hält die Single Source of Truth für BP-DB und Git, der Client braucht keinen eigenen BP-DB-Zugriff.

| Subcommand | Status | Zweck |
|---|---|---|
| `bpgit init` | Admin | Initialisiert Bare-Repo + Worktree auf Server (einmalig pro Repo) |

**Alle anderen Subcommands wurden gestrichen** (Martin-Entscheid 2026-09-11, Konsequenz aus §7 + §9 Architektur):

| Ehemals | Ersetzt durch |
|---|---|
| `bpgit commit --force` (Worktree → BP-DB via AutomateC.exe `/import`) | Standard `git commit && git push` — Server-`PreReceiveHandler` macht `/import /forceid /overwrite` atomar mit dem Push (per #6274) |
| `bpgit pull` (Worktree-Materialisierung gegen lokale BP-DB) | Standard `git pull` — Client bekommt canonical-named Files direkt aus Bare-Repo; BP-DB ist nach jedem Push via PreReceive synchron |
| `bpgit diff` / `bpgit status` / `bpgit log` | Standard `git diff` / `git status` / `git log` |
| `bpgit server start/stop/status` | Kestrel-Server läuft als separate Binary `bpgit-server.exe` (eigener Entry-Point in `src/BPGit.Server/Program.cs`) |
| `bpgit hook install` | Obsolet — alle Hooks sind im HTTP-Handler integriert, kein Shell-Script |

### CLI-Executable

- `bpgit.exe` bleibt im PATH **nur auf dem Server** (OpenClawPC)
- User benötigt KEIN `bpgit.exe` lokal — nur Standard-`git`
- Kestrel-Server läuft als separate Binary `bpgit-server.exe` (eigener Entry-Point)

### Architektur-Begründung

Der Server macht die DB-Synchronisierung im pre-receive **atomar mit dem Push** (kein Client-Sync nötig):

```
Developer pusht XML-Änderung
     ↓
HTTP POST /git-receive-pack
     ↓
PushOrchestrator
     ├─► PreReceiveHandler (C# im HTTP-Handler)
     │   ├─ parsed git diff
     │   ├─ SQL: BPAProcess WHERE name = @name
     │   ├─ Lock-Check
     │   └─ AutomateC.exe /import /forceid /overwrite  ← BP-DB SCHREIBEN
     ├─► git receive-pack --stateless-rpc              ← Git-Objekte SCHREIBEN
     └─► PostReceiveHandler (C# im HTTP-Handler)
         └─ WorktreeSyncService.MaterializeAsync       ← Server-Monitoring (optional)

Beim nächsten git pull:
     ↓
HTTP POST /git-upload-pack
     ↓
git upload-pack --stateless-rpc                       ← Client bekommt canonical-named Files
```

Es gibt keinen Workflow, der Client-seitig BP-DB-Zugriff bräuchte — daher kein Bedarf für eine Workstation-CLI.

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
git pull  # Client bekommt canonical-named Files direkt aus Bare-Repo (kein server-seitiges Post-Checkout noetig)
```

---

## 13. Migration Path

### Stand 2026-09-11 (post-CLI-Reduktion)

CLI ist komplett gestrichen bis auf `bpgit init` (Admin-Tool). Alle Developer-Workflows laufen ueber Standard-`git`. Kein Migrations-Pfad noetig — die Architektur ist final.

### Historische Migrationen (abgeschlossen)

- **Workstation-Shell-Hooks entfernt** (2026-08-30, Spec §13): `bpgit hook install`, `--install-hooks`-Flag, `InstallGitHooksAsync` vollstaendig entfernt (per #6295, Martin-Entscheid).
- **CLI auf `init` reduziert** (2026-09-11, Commit `cbcb604`): `bpgit commit`/`pull`/`diff`/`status`/`log` ersetzt durch Standard-`git` + Server-seitige Pre-/Post-Receive-Logik.
- **Hooks in HTTP-Handler integriert** (2026-09-10, Commit `82a5e84`): `pre-receive` und `post-receive` als C# im HTTP-Handler, `post-checkout` gestrichen.

### Phase 2c ist obsolet

Hooks laufen server-side. `bpgit hook install` wurde nie gebaut (Card `98e9d43f`).

## 14. Open Questions

| Frage | Kontext | Entscheidung noetig |
|---|---|---|
| HTTPS statt HTTP? | Remote-Zugriff noetig? | nach MVP1 |
| Port-Wahl | 8181 (BP-Default Resource-PC) vs 80/443 | Martin |
| Multi-User-MVP2? | BP-Lizenz erlaubt concurrent users? | nach MVP1 |
| Lock-Strategie | Optimistic vs pessimistic | MVP1: optimistic via lastmodifieddate |
| Branch-Strategie | main + feature-branches? | Standard-git, **resolved**: `main` als Default, Feature-Branches pro Developer |
| Tag-Strategie | Tags fuer Releases? | Optional, Git-Standard |
| Release-Integration mit BPARelease | git tag → BPARelease? | Nicht MVP1 |
| Filename-Conflict-Strategie bei Rename + Edit | Similarity < 50% | Martin: ggf. -M30 Threshold, oder User-Commit-Marker |

---

## 15. Implementation Roadmap

### Abgeschlossen (2026-09-11)

- Phase 4a: Kestrel HTTP-Handler + LibGit2Sharp
- Phase 4b: Pre-Receive-Logik (processid-Lookup + /import /forceid /overwrite)
- Phase 4b-follow-up: git --stateless-rpc Delegation
- Phase 4c: PostReceive-Logik (WorktreeSyncService.MaterializeAsync, server-seitig)
- Phase 5+ (Hooks-Integration): Pre-/Post-Receive als C# im HTTP-Handler, post-checkout gestrichen (Commit `82a5e84`)
- Phase 5+ (CLI-Reduktion): CLI auf nur `bpgit init` reduziert (Commit `cbcb604`)
- xunit-Tests-Welle (12 Test-Commits, 65 gruen + 4 skipped)

### Offen (Backlog siehe §14)

- `DeleteAsync`-Implementation (Phase 4b-follow-up, per #6401)
- HOLDLOCK-Migration + Race-Tests
- SQL-Performance-Index `IX_BPAProcess_Name`
- Multi-User-MVP2 (BP-Lizenz-abhaengig)
- IDE-Integration (VS Code Extension, separates Projekt)

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
