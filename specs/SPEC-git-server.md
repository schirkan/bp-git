# SPEC-git-server — Git-konformer Endpoint fuer Blue Prism Adapter

**Status:** v1.2 (server-only binary). BP→Git-Synchronisierung vor Ref-Anzeige bei clone/fetch/pull und vor Receive-Pack; Git→BP-Import nach Receive-Pack. Pre-/Post-Receive-Logik läuft als C# im HTTP-Handler, kein Client-CLI und kein `post-checkout`.

**Datum:** 2026-10-26 (BP→Git-Preflight vor Fetch und Push, automatische serverseitige Sync-Commits)
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
| `D` | "Gone" | — | Prozesslöschung via Git ist nicht implementiert; der DB-Prozess wird beim nächsten BP→Git-Abgleich wieder materialisiert. |
| `R` (git mv) | "Old" | "New" | Wird vom Tree-Walker als Delete + Add behandelt; `git mv` ist kein unterstützter Rename-Workflow. |

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
4. Server `post-receive` materialisiert den neuen Namen im Server-Worktree.
5. Beim nächsten `git pull` übernimmt der BP→Git-Abgleich den kanonischen Dateinamen als serverseitigen Commit und entfernt den alten Pfad.

**Phase 2 — User renamed in BP Studio:**

1. User benennt Prozess in BP Studio: "Old Name" → "New Name"
2. BP aktualisiert `BPAProcess.name` + `processxml`, schreibt BPAAuditEvent (sCode=P006)
3. Beim nächsten `git pull` synchronisiert der Server den neuen DB-Namen in Git und committet den kanonischen Dateipfad.

**Phase 3 — git mv (manuell, unerwuenscht):**

1. User `git mv Old Name.xml Renamed.xml` (kein XML-Content-Edit)
2. Push
3. Der Server behandelt die Pfadänderung als Delete + Add. Der Löschpfad ist nicht implementiert; ein Add mit bereits vorhandenem BP-Namen schlägt fehl.
4. Beim nächsten Fetch überschreibt der DB→Git-Abgleich den nicht-kanonischen Pfad mit dem aus `BPAProcess.name` abgeleiteten Namen.
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
2. Bei `git-upload-pack`-Discovery materialisiert der Server BP-DB in seinen Worktree und committet Änderungen auf den Default-Branch.
3. Kestrel annonciert danach die aktualisierten Refs; `git-upload-pack` liefert Git-Objekte aus dem Bare-Repo.
4. Der Client erhält den synchronisierten BP-Stand als normalen Git-Worktree.

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
2. Vor Receive-Pack gleicht der Server BP→Git ab; veraltete Updates auf den Default-Branch werden von Git abgelehnt.
3. `PushOrchestrator` parst nach erfolgreichem Ref-Update `git diff oldrev..newrev` (siehe #4):
   - Pro Aenderung: processid-Lookup, dann `AutomateC.exe /import /forceid /overwrite`
   - BPAAuditEvents wird von BP-Runtime geschrieben (sCode=P006)
4. `PostReceiveHandler`: BP-DB pollen, neue XML-Dateien schreiben, alte loeschen (canonical Filename-Normalisierung)

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
| `PreReceiveHandler` | in HTTP-Handler nach ref-apply | parse `git diff`, name-Lookup via SQL, `/import /forceid /overwrite` pro Änderung | ✅ **wired** (`PushOrchestrator` ruft `PreReceiveHandler.HandleAsync` nach erfolgreichem receive-pack auf; kann Push nicht nachträglich ablehnen) |
| `PostReceiveHandler` | in HTTP-Handler nach ref-apply | Materialisiert den aktuellen BP-DB-Stand im Server-Worktree (`WorktreeSyncService.MaterializeAsync`); Git-Commits aus BP-Änderungen entstehen vor Fetch/Push durch `BpToGitSyncService` | ✅ **wired** (`PushOrchestrator` ruft `PostReceiveHandler.HandleAsync` nach ref-apply + PreReceive auf) |
| ~~`PostCheckoutHandler`~~ | — | — | **gelöscht 2026-09-10** — kein `post-checkout`-Hook oder Client-Sync |


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
                // The tree walker reports path changes as delete + add.
                // Git path renames are not a supported BP rename workflow.
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
    await _worktreeSync.MaterializeAsync(serverWorktree); // BP-DB -> server worktree
    // Bare-Repo wird nicht hier geändert; BP->Git-Commit erfolgt vor Fetch/Push.
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

### Fetch-Flow (git clone, git fetch, git pull)

Before advertising refs for `git-upload-pack`, `BpToGitSyncService` reads BP process XML and folder assignments, materializes the configured server worktree, and commits differences under `processes/` to the default branch using the `bpgit-server` identity. The ref advertisement and upload-pack therefore include changes made directly in BP Studio. The initial clone also receives the current DB state without a manual export.

### Push-Flow (git push)

`PushOrchestrator` delegates pack and ref application to `git receive-pack --stateless-rpc`. Only after receive-pack exits successfully does it run the BP synchronization handlers:

1. `PreReceiveHandler` compares the old and new commit trees under `processes/` and passes changed XML files to `BpSyncService`.
2. Adds and modifications are imported through `AutomateC.exe /import`. Process deletion is currently unsupported by design.
3. `PostReceiveHandler` materializes the current BP-DB state into the configured server worktree.

This is **side-effect post-apply**, not an actual Git pre-receive gate. A BP import or delete failure is logged and cannot reject or roll back a Git ref update that has already succeeded. BP is authoritative on the next fetch: the server commits the current DB state to the default branch.

### Initial repository

`bpgit init` creates the bare repository with an initial `.gitignore` commit. The first `clone`/`fetch`/`pull` synchronizes the current BP database into Git automatically.

---

## 10. Conflicts and synchronization limits

- Git receive-pack serializes ref updates. The server applies each accepted Git update before attempting BP synchronization.
- BP-to-Git synchronization runs before refs are advertised for fetch; an import failure after push cannot reject the already-applied update.
- The server serializes its own push and fetch synchronization operations. Concurrent edits in BP Studio after the preflight sync remain a race; no `BPAProcessLock` check is performed in the receive flow.
- Deleting a process from Git does not delete it from BP-DB; `DeleteAsync` is intentionally not implemented because of Blue Prism referential dependencies.
- No `--force` option or client-side BP sync command exists in the server binary.

---
## 11. Server binary and initialization

The former client/admin CLI was reduced to `bpgit init` in commit `cbcb604`. That command only created the configured worktree directory; `WorktreeSyncService.MaterializeAsync` already creates it when needed. The CLI project and its empty test project were therefore removed on 2026-10-26. BP import, Git receive-pack orchestration, and worktree materialization remain server-side.

- Running `bpgit` starts the Kestrel server; `--serve`, `/s`, and `-s` remain compatibility aliases.
- `bpgit init [repo-name]` initializes the server bare repository with its initial `.gitignore` commit.
- Developers use only standard `git`; no client-side bpgit CLI or BP database connection is required.
- Before advertising upload-pack refs and before receive-pack, the server synchronizes BP→Git. After a receive-pack ref update, it imports Git→BP and materializes the current DB state.
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
git pull  # Client bekommt canonical-named Files direkt aus Bare-Repo
```

---

## 13. Migration Path

The former client CLI was removed on 2026-10-26. Its only remaining command created a worktree directory that the server materializer already creates. The server binary retains `init` for bare-repository setup; developer workstations use standard Git only.
### Historische Migrationen (abgeschlossen)

- **Workstation-Shell-Hooks entfernt** (2026-08-30, Spec §13): `bpgit hook install`, `--install-hooks`-Flag, `InstallGitHooksAsync` vollstaendig entfernt (per #6295, Martin-Entscheid).
- **CLI entfernt** (2026-10-26): Das verbliebene CLI-`init` legte nur das Worktree-Verzeichnis an, das der Server selbst erzeugt. Der Bare-Repo-Bootstrap bleibt als Server-Subcommand verfügbar.
- **Hooks in HTTP-Handler integriert** (2026-09-10, Commit `82a5e84`): `pre-receive` und `post-receive` als C# im HTTP-Handler, `post-checkout` gestrichen.

## 14. Open Questions

| Frage | Kontext | Entscheidung noetig |
|---|---|---|
| HTTPS statt HTTP? | Remote-Zugriff noetig? | nach MVP1 |
| Port-Wahl | 8181 (BP-Default Resource-PC) vs 80/443 | Martin |
| Multi-User-MVP2? | BP-Lizenz erlaubt concurrent users? | **resolved 2026-09-12 (Martin)**: Lizenz irrelevant — concurrent Pushes über Git Smart-HTTP API (`receive-pack` serialisiert Ref-Updates atomar pro Receive-Request) |
| Lock-Strategie | Optimistic vs pessimistic | `HOLDLOCK`/`BPAProcessLock` vor `/import` verworfen. BP→Git-Preflight vor Fetch und Push ist implementiert; ein `lastmodifieddate`-CAS ist noch nicht implementiert, daher bleibt ein Race mit zeitgleichen BP-Studio-Änderungen möglich. |
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
- Server-only binary cleanup: obsolete CLI, serializer, and unused Data repository removed (2026-10-26)
- Server xUnit suite (96 tests; no local Blue Prism installation required)

### Offen (Backlog siehe §14)

- SQL-Performance-Index `IX_BPAProcess_Name` — Performance ab >1000 BP-Prozessen, `bpgit-server init` Migrations-Step (Martin)

### Out of scope / obsolet per Martin 2026-09-12 (zur Info, nicht mehr offen)

- `DeleteAsync`-Implementation — referenzielle Verbindungen zu `BPAProcessBackup`/`BPAObject`/`BPARelease` (kein Cascade-Delete ohne Risiko), gibt `DeleteResult.NotImplemented()` by design zurück
- BP-DB-`lastmodifieddate`-CAS — noch nicht implementiert; Preflight-Synchronisierung reduziert, aber beseitigt zeitgleiche BP-Studio-Races nicht (siehe §10)
- Multi-User-MVP2 — Lizenz-Frage irrelevant, Git Smart-HTTP API reicht (siehe §14)
- IDE-Integration (VS Code Extension) — separates Projekt obsolet, Workstation-Editor/Standard-IDE mit Git-Integration reicht
- Workboard-Karte `bp-git-pre-receive-wiring` (`866e5346`) — done per Commit `82a5e84` (gestrichen)

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
