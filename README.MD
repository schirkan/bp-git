# bp-git — Blue Prism Processes in Git

Git-Workflow für Blue Prism Processes und Objects. Standard-git-Befehle, keine Spezial-Tools nötig. Läuft als self-hosted Server auf OpenClawPC — kein IIS, keine bpgit.exe-Installation auf deiner Workstation.

## Quickstart

```bash
# 1. Repo klonen (einmalig)
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

VS Code / Editor                    bpgit-git-server                 BP-DB
        │                                  │                            │
        │  $EDITOR xml                     │                            │
        │ ─────────────►                   │                            │
        │                                  │                            │
        │  git push                        │                            │
        │ ─────────────►                   │                            │
        │      (HTTP, Win-Auth)            │                            │
        │                                  │  /import /forceid           │
        │                                  │  /overwrite                 │
        │                                  │ ─────────────────────────► │
        │                                  │                            │
        │                                  │  ◄───── BPAAuditEvent ──── │
        │  push OK                         │                            │
        │ ◄─────────────                   │                            │
```

**Was der Server macht:**
- **Bei `git push`**: liest deine geänderten XML-Dateien, schreibt sie via `AutomateC.exe /import /forceid /overwrite` in die BP-DB.
- **Bei `git pull`**: liest die BP-DB, schreibt XML-Dateien mit canonical Filenames ins Worktree.
- **Bei `git clone`**: einmalige Materialization — du bekommst ein fertiges Worktree mit allen Processes.

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

Du brauchst sie nicht. Der `bpgit-git-server` läuft auf OpenClawPC und macht die ganze Arbeit. Du arbeitest nur mit Standard-`git`.

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

`bpgit` ist das Admin-CLI fÃ¼r Server-Setup und BP-Sync-Diagnose. Es lÃ¤uft auf OpenClawPC (nicht auf Workstations).

### Globale Optionen

| Option | Beschreibung |
|---|---|
| `-o, --output <dir>` | Worktree output directory (default: aktuelles Verzeichnis) |
| `--install-hooks` | Installiert git-hooks fÃ¼r drift-detection (nur bei `init`) |
| `--force` | Erforderlich fÃ¼r `commit` (expliziter Write) |
| `-n, --limit N` | Limit rows fÃ¼r `log` (default 50) |
| `--processid <guid>` | Filter by processid fÃ¼r `log` |
| `--since YYYY-MM-DD` | Nur EintrÃ¤ge mit eventdatetime >= since fÃ¼r `log` |
| `--event <sCode>` | Filter by event-type code (z.B. P006, L001) fÃ¼r `log` |
| `-h, --help` | Show help message |

### Commands

#### `bpgit init`

Initialisiert bp-git-Worktree (legt `.bpgit/config.toml` an).

```bash
bpgit init
bpgit --install-hooks init   # mit git-hooks
```

#### `bpgit pull`

Exportiert BP-Processes aus der DB in den Worktree (canonical Filenames aus `BPAProcess.name`).

```bash
bpgit pull
bpgit -o /path/to/worktree pull
```

#### `bpgit status`

Zeigt Diff zwischen Worktree und Snapshot (welche Files wurden lokal geÃ¤ndert, welche sind in BP-DB neuer).

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

Schreibt Worktree-Ã„nderungen zurÃ¼ck in die BP-DB (benÃ¶tigt `--force`).

```bash
bpgit --force commit
```

## Weitere Dokumentation

- **Architektur**: [`context/SPEC-git-server.md`](context/SPEC-git-server.md) — Server-Architektur, Hooks, Auth
- **Adapter-Layer**: [`specs/SPEC-adapter-architecture.md`](specs/SPEC-adapter-architecture.md) — Worktree-Layout, processid-Mapping
- **BP-CLI-Referenz**: [`context/bp-cli-reference-7.5.1.md`](context/bp-cli-reference-7.5.1.md) — AutomateC.exe-Befehle
- **BP-DB-Schema**: [`context/bp-database-schema.md`](context/bp-database-schema.md) - Tabellen-Referenz
- **Test-Stand** (Martin #6385+#6401): 12 Test-Commits (xunit-Welle), 65 gruen + 4 skipped in 3 Test-Projekten (Server 53+4, Data 3, Cli 9). 3 PreReceive HEAD-Tracking-Tests skip-attributed mit Issue-#802 workaround (commit `2fa730d`); 1 HeadTrackingDiagnosticTest skip-attributed (libgit2 0.32.0 API-Inkonsistenz). Phase 5+-Diagnose pending: tree.Count vs parents[0].tree.Count vergleichen.
