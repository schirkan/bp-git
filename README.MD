# bp-git — Blue Prism Processes in Git

Git-Workflow für Blue Prism Processes und Objects. Standard-git-Befehle, keine Spezial-Tools nötig. Läuft als self-hosted Server auf OpenClawPC — kein IIS und keine `bpgit.exe`-Installation auf deiner Workstation. Auf dem Server ist `bpgit.exe` das Kestrel-HTTP-Server-Binary; BP-Import und Worktree-Materialisierung laufen serverseitig.

## Quickstart

Der Server synchronisiert den aktuellen Blue-Prism-Datenbankstand vor jeder Ref-Anzeige für `clone`, `fetch` und `pull` in Git. Änderungen aus BP Studio erscheinen dadurch beim nächsten Git-Abruf als serverseitiger Commit; ein manueller Export ist nicht nötig.

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

## Funktionsumfang (Stand 2026-10-26)

| Operation | Aus Server-Sicht | Status |
|---|---|---|
| `git clone` | Synchronisiert BP→Git und klont danach den aktuellen Stand | ✅ |
| `git fetch` / `git pull` | Synchronisiert BP→Git vor der Ref-Anzeige; Änderungen werden als `bpgit-server`-Commit sichtbar | ✅ |
| `git push` | Gleicht BP→Git vor dem Empfang ab und importiert danach akzeptierte Änderungen in Blue Prism | ✅ |
| BP-Studio-Edit → Git | Wird beim nächsten `clone`, `fetch` oder `pull` automatisch übernommen | ✅ |

> **Synchronisationsregel:** Blue Prism ist maßgeblich. Der Server vergleicht den DB-Stand vor Fetch-Ref-Anzeige und vor Receive-Pack mit dem Default-Branch und erstellt nur bei Änderungen einen Commit mit der Identität `bpgit-server`. Ein veralteter Push auf den Default-Branch wird von Git anhand des aktualisierten Refs abgelehnt; ein Importfehler nach einem akzeptierten Push wird beim nächsten Abruf durch den aktuellen DB-Stand korrigiert.

**Was der Server heute tatsächlich macht:**
- `/info/refs`: korrekt per LibGit2Sharp-`Repository.Refs` durch LibGit2Sharp-API (Smart-HTTP v2, voll funktional) — `git clone` + `git fetch` funktionieren.
- `/git-upload-pack`, `/git-receive-pack`: Delegation an natives `git --stateless-rpc`; vor Fetch-Ref-Anzeige und Receive-Pack synchronisiert der Server BP→Git, nach einem akzeptierten Receive-Pack Git→BP.
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
2. `git pull` → der Server übernimmt den neuen Process als Commit in den Default-Branch.
3. Deine lokale Worktree-Version enthält den neuen Process.

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

Das Löschen eines Processes per Git-Push ist derzeit nicht implementiert. Wird ein Process in BP Studio gelöscht, entfernt der Server die XML-Datei beim nächsten `git pull` automatisch aus dem Default-Branch.

### Konflikt (BP-DB wurde extern geändert)

Beim nächsten `git pull` übernimmt der Server BP-Studio-Änderungen als Git-Commit. Wenn du dieselbe Datei lokal geändert hast, löst Git den daraus entstehenden Merge-Konflikt wie gewohnt:

```bash
git pull   # Server merged die Änderungen aus BP-DB
# Konflikte manuell in VS Code auflösen
git add .
git commit -m "Merge BP-DB changes"
git push
```

## Häufige Fragen

### Warum sehe ich keine `bpgit.exe`?

Auf deiner Workstation brauchst du sie nicht. `bpgit.exe` ist das Server-Binary auf OpenClawPC. Push-Importe und Worktree-Materialisierung werden dort ausgeführt; du arbeitest auf der Workstation ausschließlich mit Standard-`git`.

### Was ist, wenn ich offline bin?

`git commit`, `git diff`, `git log`, `git status`, `git branch` funktionieren offline. Nur `git push`, `git pull`, `git clone` brauchen Verbindung zum Server.

### Wo finde ich die BP-History?

```bash
git log processes/Processes/Default/"My Process.xml"
```

Oder direkt in Blue Prism Studio (Rechtsklick → Audit History). BPAAuditEvents enthält jeden `/import`-Aufruf mit Zeitstempel und User.

### Was ist, wenn ich den Process in BP Studio statt im Worktree editiere?

Der Server übernimmt BP-Studio-Änderungen beim nächsten `git clone`, `git fetch` oder `git pull` automatisch als Commit in den Default-Branch. Ein manueller Export ist nicht erforderlich.

### Ich habe `git mv` gemacht — was passiert?

Ein `git mv` wird als Datei-Löschung und -Neuanlage behandelt; Prozesslöschung in BP ist absichtlich nicht implementiert. Für ein BP-Rename ändere stattdessen den Namen im XML und behalte den Git-Pfad bei.

### BP-Synchronisierung nach einem Push fehlgeschlagen?

Git hat den Push bereits angenommen, bevor der Server die Blue-Prism-Synchronisierung ausführt. Bei XML- oder Importfehlern bleibt BP die maßgebliche Quelle: Beim nächsten `git pull` gleicht der Server den Default-Branch mit der Datenbank ab und committet den DB-Stand. Prüfe die Server-Logs, falls die Synchronisierung weiterhin fehlschlägt.

Für Details: Server-Logs auf OpenClawPC (`C:\bpgit\logs\`).

## Tipps für VS Code

- **Workspace-Empfehlungen**: Extension "XML Tools" für Syntax-Highlighting + Auto-Format.
- **Diff-Viewer**: `Ctrl+Shift+G` → Source-Control-Panel zeigt XML-Diffs inline.
- **Auto-Pull**: Extension "Git Pull" für Auto-Pull in regelmäßigen Abständen (optional).

## Server-Commands

`bpgit.exe` ist ein serverseitiges Binary; auf Developer-Workstations genügen Standard-`git`-Befehle. Es startet standardmäßig den Kestrel-Server. Die Flags `--serve`, `/s` und `-s` bleiben als Aliase verfügbar.

```bash
# Standardkonfiguration aus <exe-dir>/bpgit.json laden und Server starten
bpgit

# Explizit im Server-Modus starten
bpgit --serve

# Benutzerdefinierte Konfiguration
bpgit --serve /path/to/config.json

# Bare-Repo initialisieren
bpgit init
bpgit init my-bp-project
```

`bpgit init` initialisiert das Bare-Repo unter `repoRoot/{repoName}.git`. Eine optionale Config-Datei kann vor `init` angegeben werden: `bpgit C:\path\bpgit.json init`.

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
- **Test-Stand** (2026-10-26): `dotnet test BPGit.sln` führt 96 Server-xUnit-Tests aus. Tests verwenden Fakes und temporäre Repositories; Blue Prism muss lokal nicht installiert sein.
