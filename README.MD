# bp-git — Blue Prism Processes in Git

Git-Workflow für Blue Prism Processes und Objects. Standard-git-Befehle, keine Spezial-Tools nötig. Läuft als self-hosted Server auf OpenClawPC — kein IIS, keine bpgit.exe-Installation auf deiner Workstation. Der Server heißt `bpgit.exe` (ein einziges Binary, dient via `--serve` auch als Kestrel-HTTP-Server und via direkter Command-Line als Admin-CLI für Worktree-Sync).

## Quickstart

```bash
# 1. Repo klonen (einmalig)
#    LAN:    http://openclawpc:8181/bp-git
#    lokal:  http://localhost:8181/bp-git    (wenn Server auf derselben Maschine laeuft)
git clone http://openclawpc:8181/bp-git
cd bp-git

# 2. Worktree durchsuchen
ls processes/Processes/Default/
ls processes/Objects/Default/

# 3. Process in VS Code editieren
code "processes/Objects/Default/Utility - Environment.xml"

# 4. Änderungen committen + pushen
git add .
git commit -m "Update Utility - Environment: add error handling"
git push
```

**That's it.** Der Server schreibt deine Änderungen automatisch in die Blue-Prism-Datenbank.

## Wie es funktioniert

```
DEINE WORKSTATION                   SERVER (OpenClawPC)              BLUE PRISM
=================                   ===================              ==========

                                      `bpgit --serve` (Kestrel
                                      + LibGit2Sharp +
                                      AutomateC.exe)
VS Code / Editor                             │                            │
        │                                    │                            │
        │  $EDITOR xml                       │                            │
        │ ──────────────────────────────────►│                            │
        │                                    │                            │
        │  git push                          │                            │
        │ ──────────────────────────────────►│                            │
        │      (HTTP, Win-Auth)              │                            │
        │                                    │  /import /forceid           │
        │                                    │  /overwrite                 │
        │                                    │ ─────────────────────────► │
        │                                    │                            │
        │                                    │  ◄───── BPAAuditEvent ──── │
        │  push OK                           │                            │
        │ ◄─────────────────────────────────│                            │
```

## Funktionsumfang (Stand 2026-08-30)

| Operation | Aus Server-Sicht | Status |
|---|---|---|
| `git clone` | Worktree wird einmalig aus BP-DB materialisiert | ✅ Phase 4c (commit `f399ad1`) |
| `git fetch` / `git pull` | Server liest nach erfolgreichem `git-upload-pack` aus BP-DB und materialisiert Worktree via `PostCheckoutHandler` → `WorktreeSyncService.MaterializeAsync(cfg.WorktreePath)` | ✅ **wired (side-effect post-apply per Spec §9)** |
| `git push` | Server parst Ref-Update-Pkt-Lines, ruft `git receive-pack --stateless-rpc` (ref-update + pack-apply), danach `PreReceiveHandler` (processid-Lookup + Lock-Check + `/import`) und `PostReceiveHandler` (Worktree-Materialization). Side-effect post-apply: **kann Push nicht ablehnen** bei BP-DB-Sync-Fehler. | ✅ **wired (side-effect post-apply per Spec §9)** |
| Process-Edit in BP Studio → Worktree | Worktree wird beim nächsten `git fetch` / `git pull` durch `PostCheckoutHandler` aktualisiert (server-side Hook) | ✅ **wired** |

> ✅ **Hook-Status (Stand 2026-09-01):** Die drei Hooks `PreReceive`, `PostReceive`, `PostCheckout` sind produktiv verdrahtet (Hybrid-Ansatz per SPEC-pre-receive-wiring.md §1.3). `git receive-pack` delegiert an nativen `git --stateless-rpc` (Phase 4b-follow-up, commit `18ec5db`); davor läuft `PushOrchestrator.PreReceive`-Gate und danach `WorktreeSyncService.MaterializeAsync` per `PostReceive`. **Wichtiger Trade-off (Side-effect post-apply per Spec §9):** `PreReceive` läuft NACH `git receive-pack` ref-update, nicht davor. Push kann **nicht** abgelehnt werden, wenn BP-DB-Sync fehlschlägt — Konsistenz-Garantie via BP-DB-Sync + stderr-Log, nicht via Push-Reject. Pre-emptive Validate-then-Apply bleibt Phase 5+ Backlog (Karte `866e5346`).

**Was der Server heute tatsächlich macht:**
- `/info/refs`: korrekt per LibGit2Sharp-`Repository.Refs` durch LibGit2Sharp-API (Smart-HTTP v2, voll funktional) — `git clone` + `git fetch` funktionieren.
- `/git-upload-pack`, `/git-receive-pack`: Delegation an nativen `git --stateless-rpc` mit dazwischengeschalteter `PushOrchestrator`-Pipeline. Bei `/git-upload-pack` (clone/fetch) folgt `PostCheckoutHandler`-Hook; bei `/git-receive-pack` (push) folgt Pre-Receive + Post-Receive side-effect post-apply per Spec §9.
- `/healthz`: anonymous Service-Health-Check.

## Filename-Regeln (WICHTIG)

**Der Filename ist abgeleitet aus dem XML-Namen.** Du editierst nur die XML-Datei (Inhalt), nicht den Filename.

### Beim Umbenennen eines Processes

**RICHTIG** — nur den Namen in der XML ändern:

```xml
<!-- Vorher: processes/Objects/Default/Old Name.xml -->
<process name="Old Name" version="...">
  ...
</process>

<!-- Nachher (gespeichert unter gleichem Filename Old Name.xml): -->
<process name="New Name" version="...">
  ...
</process>
```

```bash
git add .
git commit -m "Rename: Old Name -> New Name"
git push   # Server erkennt Rename via XML-Inhalt, BP-Process wird umbenannt
git pull   # Server schreibt canonical Filename "New Name.xml", löscht "Old Name.xml"
```

**FALSCH** — `git mv` zum Umbenennen:

```bash
git mv "processes/Objects/Default/Old Name.xml" "processes/Objects/Default/New Name.xml"
```

Das funktioniert zwar technisch, aber der Server normalisiert beim nächsten Pull automatisch auf den aus dem XML-Namen abgeleiteten Filename — dein `git mv` wird effektiv rückgängig gemacht.

### Sonderzeichen

Windows verbietet diese Zeichen in Dateinamen: `< > : " / \ | ? *`

Wenn dein BP-Process so heißt, ersetzt der Server sie durch `_`:

| BP-Name | Filename |
|---|---|
| `Prozess: Test` | `Prozess_ Test.xml` |
| `Path/Test` | `Path_Test.xml` |

## Workflows

### Process-Änderung

```bash
# 1. Aktuelle BP-DB-Version holen
git pull

# 2. In VS Code editieren
code "processes/Processes/Default/My Process.xml"

# 3. Diff ansehen
git diff

# 4. Commit + Push
git add .
git commit -m "Add validation to My Process"
git push
```

### Neuen Process anlegen

**In Blue Prism Studio** (Standard-Workflow):

1. Erstelle den Process in BP Studio wie gewohnt.
2. `git pull` → Server materialisiert den neuen Process als XML-Datei.
3. `git add . && git commit -m "Add new Process X"`.

**Alternativ via Worktree**:

1. Kopiere eine existierende XML-Datei und benenne sie um (im Filename + im XML-Root).
2. Passe den Inhalt an.
3. `git push` → Server legt neuen Process in BP-DB an.

### Process umbenennen

```bash
# In VS Code: nur den Namen in der XML ändern (nicht den Filename!)
code "processes/Processes/Default/Old Name.xml"
# Ändere <process name="Old Name"> → <process name="New Name">
# Speichere unter gleichem Filename "Old Name.xml"

git add .
git commit -m "Rename: Old Name -> New Name"
git push
git pull   # Filename wird automatisch zu "New Name.xml" normalisiert
```

### Process löschen

```bash
git rm "processes/Processes/Default/My Process.xml"
git commit -m "Remove My Process"
git push   # Server löscht Process aus BP-DB
```

> **Achtung**: Process-Löschung ist endgültig. BPAAuditEvents bleiben für die Historie erhalten, aber der Process ist nicht mehr ausführbar.

### Konflikt (BP-DB wurde extern geändert)

Wenn ein anderer User zwischen deinem `pull` und `push` denselben Process in BP Studio geändert hat, lehnt der Server deinen Push ab. Lösung:

```bash
git pull   # Server merged die Änderungen aus BP-DB
# Konflikte manuell in VS Code auflösen
git add .
git commit -m "Merge BP-DB changes"
git push
```

## Häufige Fragen

### Warum sehe ich keine `bpgit.exe`?

Auf deiner Workstation brauchst du sie nicht. Das `bpgit.exe` ist das **Server-Binary** auf OpenClawPC — ein einziges Programm, das per `bpgit --serve` als Kestrel-HTTP-Server läuft und per direktem Aufruf (`bpgit init`, `bpgit pull`, `bpgit status`, …) als Admin-CLI fuer Worktree-Sync und Diagnose dient. Du arbeitest auf der Workstation nur mit Standard-`git`.

### Was ist, wenn ich offline bin?

`git commit`, `git diff`, `git log`, `git status`, `git branch` funktionieren offline. Nur `git push`, `git pull`, `git clone` brauchen Verbindung zum Server.

### Wo finde ich die BP-History?

```bash
git log processes/Processes/Default/"My Process.xml"
```

Oder direkt in Blue Prism Studio (Rechtsklick → Audit History). BPAAuditEvents enthält jeden `/import`-Aufruf mit Zeitstempel und User.

### Was ist, wenn ich den Process in BP Studio statt im Worktree editiere?

**Beides ist OK.** Der Server syncronisiert in beide Richtungen:
- Worktree → BP-DB: bei `git push`
- BP-DB → Worktree: bei `git pull`

Wichtig: nach einer Änderung in BP Studio immer `git pull`, damit dein Worktree aktuell ist.

### Ich habe `git mv` gemacht — was passiert?

Funktioniert, aber der Server normalisiert beim nächsten Pull auf den canonical Filename (vom XML-Namen abgeleitet). Dein manueller Rename wird also effektiv rückgängig gemacht. **Lieber den Process-Namen in der XML ändern.**

### Der Server hat meinen Push abgelehnt — warum?

Mögliche Gründe:
- **BPAProcessLock** aktiv (jemand editiert den Process gerade in BP Studio) → warten oder `--force`
- **Optimistic-Lock** (BP-DB wurde extern geändert seit deinem letzten pull) → `git pull` zuerst
- **XML-Parse-Fehler** (deine XML-Datei ist kaputt) → Validierung in VS Code
- **Process nicht gefunden** (rename-detection fehlgeschlagen, weil Similarity < 50%) → manuell prüfen

Für Details: Server-Logs auf OpenClawPC (`C:\bpgit\logs\`).

## Tipps für VS Code

- **Workspace-Empfehlungen**: Extension "XML Tools" für Syntax-Highlighting + Auto-Format.
- **Diff-Viewer**: `Ctrl+Shift+G` → Source-Control-Panel zeigt XML-Diffs inline.
- **Auto-Pull**: Extension "Git Pull" für Auto-Pull in regelmäßigen Abständen (optional).

## CLI-Commands

`bpgit.exe` ist das **Admin-CLI für Server-Setup und BP-Sync-Diagnose**. Es läuft auf OpenClawPC (nicht auf Workstations). Das gleiche Binary startet per `bpgit --serve` auch den Kestrel-Git-Server (siehe [Server-Mode](#server-mode)).

Standard-Config: `<exe-dir>/bpgit.json` (wird beim Publish mit-exportiert). Override via `bpgit --serve /path/to/config.json`.

### Globale Optionen

| Option | Beschreibung |
|---|---|
| `--force` | Erforderlich für `commit` (expliziter Write) |
| `-n, --limit N` | Limit rows für `log` (default 50) |
| `--processid <guid>` | Filter by processid für `log` |
| `--since YYYY-MM-DD` | Nur Einträge mit eventdatetime >= since für `log` |
| `--event <sCode>` | Filter by event-type code (z.B. P006, L001) für `log` |
| `-h, --help` | Show help message |

Worktree-Pfade (`./processes`, `bpgit-snapshot.json`, `C:\Program Files\Blue Prism Limited\Blue Prism Automate\AutomateC.exe`, Auth-Modus) werden aus `bpgit.json` gelesen — **kein** separater CLI-Output-Flag mehr.

### Commands

#### `bpgit init`

Bootstrap CLI-Worktree aus BP-DB (Liest `bpgit.json`, materialisiert Worktree am konfigurierten `worktreeDir`, schreibt `bpgit-snapshot.json` daneben). **Kein** `.bpgit/`-Verzeichnis wird angelegt — die Unified-Config ist Single Source of Truth.

```bash
bpgit init
```

#### `bpgit pull`

Re-exportiert BP-Processes aus der DB in den Worktree (canonical Filenames aus `BPAProcess.name`).

```bash
bpgit pull
```

#### `bpgit status`

Zeigt Diff zwischen Worktree und Snapshot (welche Files wurden lokal geändert, welche sind in BP-DB neuer).

```bash
bpgit status
```

#### `bpgit diff [<processid>]`

Hash-basierter Drift-Report (Worktree vs Snapshot). Optional per processid filtern.

```bash
bpgit diff
bpgit diff <processid-guid>
```

#### `bpgit log`

Zeigt BP per-edit Audit History aus `BPAAuditEvents`. Filter via `--processid`, `--since`, `--event`, `--limit`.

```bash
bpgit log
bpgit log --limit 10
bpgit log --processid <guid>
bpgit log --since 2026-08-01
bpgit log --event P006
```

#### `bpgit commit`

Schreibt Worktree-Änderungen zurück in die BP-DB (benötigt `--force`).

```bash
bpgit --force commit
```

## Server-Mode

`bpgit --serve` startet den Kestrel-HTTP-Server auf OpenClawPC. Das gleiche Binary, das auch als Admin-CLI dient — Dispatcher in `Program.cs` checkt `--serve` / `/s` / `-s` als erstes Argument und routet entsprechend.

```bash
# Default: lädt <exe-dir>/bpgit.json
bpgit --serve

# Custom config
bpgit --serve /path/to/config.json

# Aliases für Windows-Shell-Gewohnheiten
bpgit /s
bpgit -s
```

### Server-Sub-Commands

#### `bpgit --serve init <repo-name>`

Initialisiert das Bare-Git-Repo mit XML-only `.gitignore`. Läuft einmal und beendet.

```bash
bpgit --serve init                    # nutzt repoName aus bpgit.json (default: bp-git)
bpgit --serve init my-bp-project      # Repo-Name override
```

Das Repo wird unter `repoRoot/{repoName}.git` angelegt (Default: `C:\bpgit\repos\bp-git.git`). Der Initial-Commit enthält die `<exe-dir>/bpgit.json` (per `Content Update` beim Publish) und ein `.gitignore` mit XML-Whitelist.

### Publish-Profile

`src/BPGit.Server/Properties/PublishProfiles/`:

| Profile | Output | Größe |
|---|---|---|
| `FolderProfile` | `bin/publish/win-x64/bpgit.exe` + `bpgit.json` + alle DLLs | ~0.15 MB exe (framework-dependent) |
| `SelfContainedSingleFile` | `bin/publish/win-x64-self-contained/bpgit.exe` | ~52 MB (single-file, inkl. .NET-Runtime) |

```bash
# Framework-dependent (für OpenClawPC mit installiertem .NET 10)
dotnet publish src/BPGit.Server/BPGit.Server.csproj /p:PublishProfile=FolderProfile

# Self-contained (für Zielsysteme ohne .NET-Runtime)
dotnet publish src/BPGit.Server/BPGit.Server.csproj /p:PublishProfile=SelfContainedSingleFile
```

## Weitere Dokumentation

- **Architektur**: [`specs/SPEC-git-server.md`](specs/SPEC-git-server.md) — Server-Architektur, Hooks, Auth
- **Adapter-Layer**: [`specs/SPEC-adapter-architecture.md`](specs/SPEC-adapter-architecture.md) — Worktree-Layout, processid-Mapping
- **BP-CLI-Referenz**: [`context/bp-cli-reference-7.5.1.md`](context/bp-cli-reference-7.5.1.md) — AutomateC.exe-Befehle
- **BP-DB-Schema**: [`context/bp-database-schema.md`](context/bp-database-schema.md) - Tabellen-Referenz
- **Test-Stand** (Martin #6385+#6401+#6415+#6427+#6462): README wurde umbenannt zu README.MD (Konvention), Inhalt identisch, 12 xunit-Test-Commits (75 gruen + 1 skipped in 3 Projekten: Server 53+1, Data 3, Cli 9, plus 9 gruen ausgewaehlt). 3 PreReceive HEAD-Tracking-Tests skip-attributed mit Issue-#802 workaround (commit `2fa730d`); 1 HeadTrackingDiagnosticTest skip-attributed (libgit2 0.32.0 API-Inkonsistenz). Phase 5+-Diagnose pending: tree.Count vs parents[0].tree.Count vergleichen.
