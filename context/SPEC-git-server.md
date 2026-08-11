# SPEC-git-server — Git-konformer Endpoint fuer Blue Prism Adapter

**Status:** Draft v0.1
**Datum:** 2026-08-11
**Autor:** bpgit-Projekt
**Bezug:** Martin-Direktive #6295 (18:17), #6289 (17:28), #6287 (16:37), #6285 (16:29)
**Mitgeltend:** `SPEC-target-environment.md`, `SPEC-adapter-architecture.md`, `context/bp-cli-reference-7.5.1.md`, `context/bp-database-schema.md`

---

## 1. Goals & Non-Goals

### Goals

- **Git-konformer Workflow**: User nutzt ausschliesslich Standard-git-Befehle (`clone`, `pull`, `push`, `branch`, `merge`, `log`, `diff`, `status`, `fetch`, `reset`, `revert`).
- **Lokaler MVP1-Betrieb** auf OpenClawPC (gleicher Rechner wie Blue Prism Studio).
- **SSO-Authentifizierung** via Windows-Integrated-Auth — BP-Studio-Login = BP-DB-Login.
- **Folder-aware Worktree** mit BP-Tree- und BP-Group-Struktur (per #6287, #6289).
- **Atomare BP-DB-Writes** mit korrekten BPAAuditEvents (via AutomateC.exe `/import`).
- **Read-Performance**: Initial-Pull nutzt SqlCommand direkt gegen BPAProcess + BPATree + BPAGroup + BPAGroupProcess (per Martin #6285).

### Non-Goals

- **Client-side Hooks** (post-checkout/post-merge im Worktree) — obsolet per Martin #6295.
- **Multi-User-Sync** in MVP1 (single-user auf OpenClawPC).
- **Remote-Zugriff ueber Internet** (nur lokal auf OpenClawPC, MVP1).
- **Branch-basiertes Release-Management** via git — kann spaeter via BP-eigene Release-Mechanismen kommen.

---

## 2. Architektur-Uebersicht

```
+-------------------+    git clone/push/pull       +-----------------------+    AutomateC.exe    +------------------+
|                   |   (HTTP, Win-Auth/SSO)    |                       |    /import /        |                  |
|   Developer       | <------------------------> |   bpgit-git-server    |    importrelease   |  Blue Prism      |
|   Workstation     |     standard-git-protocol  |   (OpenClawPC)        | <----------------> |  Database        |
|   (kein bpgit)    |                            |                       |    SqlCommand       |  (localdb)       |
+-------------------+                            +-----------------------+                     +------------------+
                                                          |
                                                          | Hooks (server-side):
                                                          |   post-receive -> bpgit commit --force pro XML
                                                          |   post-checkout -> bpgit pull (Materialize)
```

### Komponenten

1. **Developer Workstation**: Standard `git` CLI oder Git-GUI. Auth via Windows-Integrated-Auth. **KEIN bpgit.exe lokal noetig**.
2. **bpgit-git-server**: HTTP-Server auf OpenClawPC. Implementiert git-smart-HTTP-Protocol (via git-http-backend CGI), Windows-Auth, Server-side-Hooks fuer BP-DB-Sync.
3. **Blue Prism Database**: SQL Server Express (localdb) auf OpenClawPC.

---

## 3. Worktree-Layout (final, per #6289)

```
<worktree>/                                  # git working tree
|
+-- processes/                               # Folder-aware BP-Worktree
|   +-- Processes/                           # BPATree id=2 (gefiltert)
|   |   +-- Default/                         # BPAGroup name="Default"
|   |   |   +-- MP - Subprocess A.xml        # filename = BPAProcess.name + ".xml"
|   |   |   +-- Test Process.xml
|   |   +-- dummy/                           # BPAGroup name="dummy"
|   |   |   +-- bp demo.xml
|   |   +-- System Update/                   # BPAGroup name="System Update"
|   |       +-- Microsoft Store.xml          # Process in mehreren Groups -> Duplikat
|   +-- Objects/                             # BPATree id=3 (gefiltert)
|       +-- Default/
|       |   +-- Data - SQL Server.xml
|       |   +-- Email - POP3-SMTP-IMAP.xml
|       +-- ...
+-- .bpgit/
|   +-- config.toml                          # BP connection + [cli] auth section
|   +-- snapshot.json                        # processid -> {hash, name, type, path}
|   +-- folders.json                         # BPATree + BPAGroup + BPAGroupGroup hierarchy
+-- .git/                                    # Standard git internals
+-- .gitignore                               # excludes .bpgit/, *.bak, temp files
```

### Filename-Sanitisierung

Windows verbietet `<>:"/\|?*` in Dateinamen:

```
sanitize(name):
    return re.sub(r'[<>:"/\\|?*]', '_', name).rstrip('. ')
```

**Name-Collision-Handling** (theoretisch, Demo-DB hat 0 Duplikate): bei zwei Processes mit gleichem Namen in derselben Group-Pfad → Suffix ` (guid-short)`, z.B. `MP - Subprocess A (42b5169c).xml`.

**Filename-Extraktion** aus BPAProcess.processxml (Root-Element):
```
Regex: ^\s*<(process|object)\s+[^>]*\bname\s*=\s*"([^"]+)"
Beispiel: <process name="MP - Subprocess A" ...>  ->  "MP - Subprocess A"
```

**Nested Folders** via `BPAGroupGroup(groupid, memberid)` fuer verschachtelte Group-Hierarchie. Aktuell 0 Rows in Demo-DB, Schema vorhanden.

---

## 4. Git-Server Stack

| Schicht | Komponente |
|---|---|
| HTTP-Frontend | `git-http-backend` (in git.exe enthalten) als CGI-Wrapper |
| Webserver | Apache oder IIS (Windows-nativ) als Reverse Proxy |
| Auth | Windows-Integrated-Auth (mod_auth_windows oder IIS integrated) |
| Backend | bpgit-git-server (.NET 10) fuer Hooks + Worktree-Materialization |
| BP-Integration | AutomateC.exe `/import` (writes) + SqlCommand direkt (reads) |

### Endpoints

| Endpoint | Methode | Zweck | Auth |
|---|---|---|---|
| `/info/refs` | GET | git-discovery (refs) | Win-Auth |
| `/git-upload-pack` | POST | git-fetch/clone | Win-Auth |
| `/git-receive-pack` | POST | git-push | Win-Auth |
| `/bpgit/status` (optional) | GET | BP-Status, Last-Sync-Zeit | Win-Auth |
| `/bpgit/log` (optional) | GET | BPAAuditEvents-Filter | Win-Auth |

### Beispiel: git clone

```bash
git clone http://win-user@openclawpc:8181/bp-git
cd bp-git
ls processes/Processes/Default/   # folder-aware materialisiert
```

Server-Flow:
1. Auth via Windows-Integrated-Auth (Domänen-Credentials)
2. git-http-backend servt initial git-protocol (refs, upload-pack)
3. Server-Hook init-clone: `bpgit pull` materialisiert Worktree aus BP-DB
4. User erhaelt Worktree mit folder-aware Layout

### Beispiel: git push

```bash
$EDITOR "processes/Processes/Default/MP - Subprocess A.xml"
git add .
git commit -m "Update MP - Subprocess A"
git push
```

Server-Flow:
1. Auth + push via git-receive-pack
2. Server-Hook post-receive: fuer jede geaenderte XML-Datei:
   - Lese Worktree-XML
   - Look up processid via snapshot.json
   - StripLeadingXmlComments (per #6277)
   - `AutomateC.exe /import <tmpfile> /forceid <guid> /overwrite`
   - BPAAuditEvents wird von BP-Runtime automatisch geschrieben (sCode=P006)
3. Server aktualisiert snapshot.json + folders.json
4. User sieht Push-Confirmation

---

## 5. Auth-Modell (MVP1)

**Windows-Integrated-Auth** (SSO via Domaenen-Credentials):

- **Apache**: `mod_auth_windows` mit `SspiOn` Konfiguration
- **IIS**: integrated Windows Authentication (Negotiate/NTLM)
- **Kein Passwort-Prompt** fuer Domaenen-User

**Keine separate User-Verwaltung**:
- BP-Studio-Login = BP-DB-Login (Windows User wird via SSPI an SQL Server weitergereicht)
- bpgit-git-server leitet Windows-User an BP-DB weiter (`[cli] auth = "sso"`)
- BP-Audit-Log (BPAAuditEvents.gSrcUserID) zeigt den Windows-User

**MVP1-Limitation**: single-user auf OpenClawPC. Multi-User-Sync erfordert BP-Lizenz-Erweiterung + Locking-Strategie (MVP2+).

---

## 6. Server-Side Hooks (ersetzen Client-side Hooks)

| Hook | Trigger | Aktion |
|---|---|---|
| `init-clone.sh` | einmalig nach `git clone` | `bpgit pull` materialisiert Worktree |
| `post-receive.sh` | nach jedem `git push` | pro geaenderte XML: `bpgit commit --force` |
| `post-checkout.sh` | nach `git checkout` (Branch-Wechsel) | `bpgit pull` refresht Worktree |

**Keine Client-Side Hooks** im Worktree — `bpgit.exe` ist nicht im Worktree noetig, nur am Server. Phase 2c (`bpgit hook install`) wird obsolet (Card `98e9d43f-...` als obsolet markiert).

### Beispiel: post-receive.sh

```bash
#!/bin/bash
# C:/bpgit/repos/bp-git/hooks/post-receive.sh
while read oldrev newrev refname; do
    # Diff old..new, finde geaenderte XML-Dateien
    changed=$(git diff --name-only $oldrev..$newrev | grep '\.xml$')
    for f in $changed; do
        # bpgit commit --force liest Worktree-XML und schreibt via AutomateC.exe
        bpgit commit --force "$f"
    done
done
```

---

## 7. Snapshot-Format

Erweitert um `path`-Field fuer processid → worktree-path-Mapping:

```json
{
  "version": 2,
  "extractedAt": "2026-08-11T18:00:00Z",
  "processes": {
    "42b5169c-1fde-4a1a-b912-4d1249805188": {
      "hash": "sha256:682123330adbfef6765781fa4209ccbe1525e9f08efafbd4e2c5bcfe6bd5ea1c",
      "name": "MP - Subprocess A",
      "type": "P",
      "path": "processes/Processes/Default/MP - Subprocess A.xml"
    },
    "e83e413e-b3c7-493b-9f52-3d9d0818b15c": {
      "hash": "sha256:ad2c957cc1728722c9814834a7c201ca41e166f6b935c0db14ec4c124a7cbfed",
      "name": "MP - System Update",
      "type": "P",
      "path": "processes/Processes/Default/MP - System Update.xml"
    }
  },
  "folders": {
    "trees": [
      {"id": 2, "name": "Processes"},
      {"id": 3, "name": "Objects"}
    ],
    "groups": [
      {"id": "FCEF128D-09E9-4AD4-A6F0-37B673BC300A", "treeId": 2, "name": "Default"},
      {"id": "CCC17E80-9C06-49F6-88C1-98421E09A7D4", "treeId": 3, "name": "dummy"}
    ],
    "nestedGroups": [],
    "memberships": [
      {"groupId": "FCEF128D-...", "processId": "42b5169c-..."},
      {"groupId": "CCC17E80-...", "processId": "4AFB82B2-..."}
    ]
  }
}
```

`folders`-Section ist optional (kann auch nur via BP-Schema on-the-fly ermittelt werden).

---

## 8. BP-Synchronization-Flow

### Pull-Flow (git clone, git pull)

`bpgit pull` (server-side, von post-checkout Hook aufgerufen):

1. SqlCommand direkt gegen `BPAProcess` (alle Rows, Filter optional)
2. SqlCommand gegen `BPATree` (Filter: name IN ('Processes', 'Objects'))
3. SqlCommand gegen `BPAGroup` + `BPAGroupGroup` (rekursiv fuer nested folders)
4. SqlCommand gegen `BPAGroupProcess` (M:N-Mapping)
5. Fuer jeden Process:
   - Filename aus `BPAProcess.processxml` extrahieren (Regex `<(process|object) ... name="..."`)
   - Path = `<TreeName>/<GroupName>/.xml`
   - Sanitize filename (Windows-Inkompatible Zeichen ersetzen)
   - Write XML zu worktree
   - Update snapshot.json mit path-Field
6. folders.json mit Hierarchy aktualisieren

**Performance-Hinweis** (per Martin #6285): NIEMALS AutomateC.exe `/export` fuer Pull — zu langsam. SqlCommand direkt ist Pflicht.

### Push-Flow (git push)

`post-receive Hook`:

1. `git diff --name-only oldrev..newrev` → Liste geaenderter XML-Dateien
2. Fuer jede geaenderte XML:
   - Read snapshot.json → processid
   - StripLeadingXmlComments (BP-Parser toleriert keine Leading Comments — siehe #6277)
   - Write temp file
   - `AutomateC.exe /import <tmpfile> /forceid <guid> /overwrite`
   - BPAAuditEvents wird von BP-Runtime geschrieben (sCode=P006)
3. Fuer geloeschte XMLs: warnen (CLI unterstuetzt kein Process-Delete, manuell in BP Studio)

### Initial-Push (leeres Repo)

1. `bpgit init` erstellt Worktree mit folder-aware Layout aus BP-DB
2. `git add . && git commit -m "Initial import"`
3. `git push origin main` → Server-Hook ruft `bpgit commit` fuer alle XML-Dateien

---

## 9. Conflicts & Lock-Handling

### Optimistic Locking via lastmodifieddate

- `bpgit commit` liest `lastmodifieddate` aus snapshot.json (zum Zeitpunkt des letzten Pull)
- Vergleicht mit aktuellem Wert in BPAProcess
- Wenn abweichend → Konflikt → User muss `git pull` zuerst

### BPAProcessLock

- Vor `bpgit commit` → Check `BPAProcessLock.userid`
- Wenn Lock vorhanden → Lock-Owner anzeigen, bpgit commit abbrechen
- `--force` Flag → Lock ignorieren (mit Warnung)

### Multi-User (MVP2, nicht MVP1)

- Pessimistic Locking via `BPAProcessLock` (BP-Studio setzt automatisch beim Edit)
- bpgit commit respektiert Locks
- Konflikt-Resolution: User A locked, User B wartet oder benutzt `--force`

---

## 10. Deployment (MVP1)

### Voraussetzungen

- Windows 10/11 (OpenClawPC, gleicher Rechner wie BP Studio)
- BP Studio + `(localdb)\BluePrismLocalDB` installiert
- Git for Windows (mit git-http-backend in `mingw64/libexec/git-core/`)
- Apache oder IIS als Reverse-Proxy
- .NET 10 (fuer bpgit-git-server)

### Schritte

1. **Apache/IIS installieren** (falls nicht vorhanden).
2. **Reverse-Proxy konfigurieren** fuer `git-http-backend`:
   ```apache
   # Apache example (Windows)
   SetEnv GIT_PROJECT_ROOT "C:/bpgit/repos"
   SetEnv GIT_HTTP_EXPORT_ALL
   ScriptAliasMatch \
       "(?x)^/(.*/(HEAD|info/refs|objects/(info/[^/]+|[0-9a-f]{2}/[0-9a-f]{38}|pack/pack-[0-9a-f]{40}\.(pack|idx)))$" \
       "C:/Program Files/Git/mingw64/libexec/git-core/git-http-backend.exe/$1"
   ```
3. **Windows-Auth aktivieren** (Apache `mod_auth_windows` / IIS integrated).
4. **bpgit-git-server Binary** installieren nach `C:\bpgit\bin\`.
5. **PATH-Variable** erweitern um `C:\bpgit\bin\` (fuer AutomateC.exe + bpgit).
6. **Git-Repository initialisieren**:
   ```bash
   cd "C:/bpgit/repos/bp-git"
   git init --bare
   bpgit init
   ```
7. **Hooks installieren** in `C:/bpgit/repos/bp-git/hooks/` (siehe #6).

### Beispiel-Aufruf (Developer)

```bash
# Initial clone (einmalig)
git clone http://win-user@openclawpc:8181/bp-git
cd bp-git

# Worktree ist folder-aware materialisiert
ls processes/Processes/Default/

# Edit + push (Standard-git)
$EDITOR "processes/Processes/Default/MP - Subprocess A.xml"
git add .
git commit -m "Update MP - Subprocess A"
git push  # server-side bpgit commit --force schreibt in BP-DB

# Pull (Standard-git, refresht von BP-DB)
git pull  # server-side bpgit pull materialisiert Updates
```

---

## 11. CLI-Reduktion (per #6295)

### Subcommands — End-State

| Subcommand | Status | Zweck |
|---|---|---|
| `bpgit init` | Admin | Initialisiert `.bpgit/config.toml` in Worktree (post-clone) |
| `bpgit server start/stop` | Admin | Startet/stoppt bpgit-git-server |
| `bpgit server status` | Admin | Server-Health (letzte Pull-Zeit, pending Hooks) |
| `bpgit pull` | Internal | Server-side materialization (von Hook aufgerufen) |
| `bpgit commit` | Internal | Server-side write (von Hook aufgerufen) |
| `bpgit log` | Diagnostic | BPAAuditEvents aus BP-DB (per-User-Audit) |
| `bpgit status` | Deprecated | Nutze stattdessen `git status` |
| `bpgit diff` | Deprecated | Nutze stattdessen `git diff` |
| `bpgit hook install` | **Obsolet** | Server-side Hooks via git-http-backend |

### CLI-Executable

- `bpgit.exe` bleibt im PATH **nur auf dem Server** (OpenClawPC)
- User benoetigt KEIN `bpgit.exe` lokal — nur `git`

---

## 12. Migration Path

### Bestehende CLI-User → Git-Server

1. **Git-Server deployen** (per #10)
2. **Initial-Repo erstellen** auf einem Workstation: `git clone <server-url>` → Worktree wird materialisiert
3. **Bestehende Worktrees committen**: `git add . && git commit -m "Initial import"` → `git push`
4. **Andere User migrieren**: `git remote add bp-server <url> && git push bp-server main`

### Ein-Weg-Migration

- CLI-Workflow wird deprecated (nicht entfernt)
- Neue Projekte starten direkt mit Git-Server
- Bestehende Projekte migrieren schrittweise

### Phase 2c ist obsolet

Da Hooks server-side laufen, entfaellt die komplette `bpgit hook install`-Implementation (Card `98e9d43f-...`). `--install-hooks`-Flag in `InitCommand` wird deprecated.

---

## 13. Open Questions

| Frage | Kontext | Entscheidung noetig |
|---|---|---|
| HTTPS statt HTTP? | Remote-Zugriff noetig? | nach MVP1 |
| Port-Wahl | 8181 (BP-Default Resource-PC) vs 80/443 | Martin |
| Multi-User-MVP2? | BP-Lizenz erlaubt concurrent users? | nach MVP1 |
| Lock-Strategie | Optimistic vs pessimistic | MVP1: optimistic via lastmodifieddate |
| Branch-Strategie | main + feature-branches? | Standard-git, User-Entscheidung |
| Tag-Strategie | Tags fuer Releases? | Optional, Git-Standard |
| Release-Integration mit BPARelease | git tag → BPARelease? | Nicht MVP1 |

---

## 14. Implementation Roadmap

| Schritt | Status | Aufwand |
|---|---|---|
| **c) SPEC-git-server.md** (dieses Dokument) | **dieser Schritt** | done |
| **a) SnapshotEntry + PullCommand folder-aware** | offen | 2-3h |
| bpgit-git-server Implementation in C# (.NET 10) | offen | 1-2 Wochen |
| Apache/IIS-Config fuer git-http-backend + Windows-Auth | offen | 2-3h |
| Hook-Scripts in `C:/bpgit/repos/bp-git/hooks/` | offen | 1-2h |
| MVP1-Deployment auf OpenClawPC | offen | 1 Tag |
| End-to-End-Test: clone → edit → commit → push → verify in BP Studio | offen | 1-2 Tage |
| Dokumentation fuer End-User (`README-bpgit-git.md`) | offen | 1 Tag |

---

## 15. References

- **Martin-Direktive #6295** (18:17): Git-Server > CLI + Hooks
- **Martin-Direktive #6289** (17:28): Worktree-Layout (kein meta.json, filename = process.name, kein per-Process-Subfolder)
- **Martin-Direktive #6287** (16:37): Folder-Struktur + git-server lokal auf BP-Studio-Maschine (SSO moeglich)
- **Martin-Direktive #6285** (16:29): Folder-Struktur existiert, Initial-Pull DB-direct (kein CLI-Export)
- **Martin-Direktive #6277** (12:43): `StripLeadingXmlComments` Helper in CommitCommand.cs (Leading-Comments brechen BP's /import-Parser)
- **Martin-Direktive #6274** (11:42): bpgit commit via AutomateC.exe /import (audit-konform)
- **Martin-Direktive #6271** (10:00): CLI-Doku 7.5.1 in context/ (siehe `context/bp-cli-reference-7.5.1.md`)
- `SPEC-target-environment.md` (OpenClawPC, .NET 10, git, BP)
- `SPEC-adapter-architecture.md` (DB-direct write, BP-Cli-Bridge-Architecture)
- `context/bp-cli-reference-7.5.1.md` (AutomateC.exe CLI-Referenz)
- `context/bp-database-schema.md` (BP-Schema-Dokumentation)
