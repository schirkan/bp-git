# BP-Git

## Zweck

Git-Workflows für Blue Prism Processes und Objects via self-hosted C#-Server (Kestrel + LibGit2Sharp) auf OpenClawPC. Developer-Workstations benutzen ausschliesslich Standard-`git` — kein `bpgit.exe` lokal.

## Aktueller Status

- **Architektur-Switch zu Git-Server** (Martin #6295, 18:17): Hooks laufen serverseitig, kein Client-Bloat.
- **processid-Mapping final** (Martin #6311, 21:42): Filename = `sanitize(BPAProcess.name)`, abgeleitet nicht autoritativ. Mapping via `git diff`-Status (R/M/A/D) + `BPAProcess.name`-DB-Lookup.
- **Kein** `snapshot.json`, **kein** `folders.json` im Worktree. Pure XML + git.
- **Specs/Doku komplett** (2026-08-11 21:55):
  - `context/SPEC-git-server.md` — Git-Server-Architektur, Hooks, Auth, Deployment (autoritativ für Server-Aspekte)
  - `specs/SPEC-adapter-architecture.md` — Worktree-Layout, processid-Mapping, XML-Serialisierung
  - `README-bpgit-git.md` — End-User-Quickstart (Standard-git-Befehle)
- **Implementation ausstehend** — wartet auf Martins Freigabe nach Doku-Review (#6313).

## Project Files

- `AGENTS.md` — Projekt-Übersicht, Status, Git-Sektion (diese Datei)
- `README-bpgit-git.md` — End-User-Doku (Quickstart, Workflows, Filename-Regeln)
- `specs/SPEC-adapter-architecture.md` — Adapter-Layer-Architektur (Worktree, Mapping)
- `specs/SPEC-target-environment.md` — OpenClawPC, .NET 10, BP 7.5.1
- `context/SPEC-git-server.md` — Git-Server-Architektur, Hooks, Auth, Deployment
- `context/bp-cli-reference-7.5.1.md` — AutomateC.exe CLI-Referenz 7.5.1
- `context/bp-database-schema.md` — BP-Schema-Doku
- `context/bp-existing-solutions.md` — Bestehende Alternativen (BP-Diff, etc.)
- `context/automatec-help-7.5.1.txt` — Roh-Help-Output von AutomateC.exe
- `src/BPGit.Cli/` — Adapter-CLI (für Admin-Tasks am Server: `init`, `server start/stop`, `log`)
- `src/BPGit.Data/` — Data-Layer (POCOs, Dapper, Repository)
- `src/BPGit.Format/` — XML-Serialisierung
- `tests/` — xunit-Tests

## Git

- **Repo-Typ:** lokal ohne GitHub
- **Pfad / URL:** `C:\Users\Admin\.openclaw\workspace\projects\bp-git`
- **Remote(s):** _(keine)_
- **Standard-Branch:** `main`
- **`.gitignore`-Status:** vorhanden (excluded: `.bpgit/`, `*.bak`, `temp/`)

## Workboard

Backlog enthält:
- `bpgit-git-server-impl` — Implementation des Kestrel + LibGit2Sharp-Servers (Phase 4)
- `bp-git-tests` — xunit-Tests für Adapter-CLI + Data-Layer
- `bp-git-mvp1-deployment` — Deployment auf OpenClawPC + End-to-End-Test
- `bp-git-demo-db-cleanup` — Demo-DB Cleanup (1 zusätzliche BPARelease-Row aus früherem `/importrelease`-Test)

Erledigt:
- Phase 1 MVP (commit `b6d7e02`)
- Phase 2a `bpgit commit` via AutomateC.exe (`2b53c4f`, `663a07f`)
- Phase 2b `bpgit log` + `bpgit diff` (`41842c7`, `76da584`)
- Phase 3 SnapshotEntry + PullCommand folder-aware (`e0e0109`) — **durch Git-Server-Architektur obsolet**
- Phase 2c Hooks (`98e9d43f`) — **obsolet** per #6295 (Hooks laufen serverseitig)
- Doku-Round (SPEC-git-server, SPEC-adapter-architecture, README-bpgit-git)

## Mitgeltende Docs

- `projects/PROJECT-RULES.md` — Projekt-Modus, Workboard-Konventionen
- `~/.openclaw/workspace/MEMORY.md` — Lessons Learned (PowerShell, BP-SSO, Git, TOML, Encoding)

## Decisions (chronologisch)

| Datum | Decision | Quelle |
|---|---|---|
| 2026-08-09 | Stack: Windows 11 + Git + VS Code + .NET 10 / C# 13 | Martin |
| 2026-08-09 | License: MIT | Martin |
| 2026-08-10 | Initial-Pull via SqlCommand direkt (kein CLI `/export`) | Martin #6285 |
| 2026-08-10 | Folder-aware Worktree (BPATree + BPAGroup + BPAGroupProcess) | Martin #6287 |
| 2026-08-10 | Worktree-Layout: `<TreeName>/<GroupName>/<sanitize(name)>.xml`, kein meta.json | Martin #6289 |
| 2026-08-10 | Write-Path via AutomateC.exe `/import /forceid /overwrite` (audit-konform) | Martin #6274 |
| 2026-08-10 | StripLeadingXmlComments vor Regex-Extraktion | Martin #6277 |
| 2026-08-11 | History via BPAAuditEvents (nicht BPAProcessBackup) | Martin #6280 |
| 2026-08-11 | Architektur-Switch zu Git-Server (self-hosted, kein IIS) | Martin #6295 |
| 2026-08-11 | processid-Mapping via git-diff R/M/A/D + DB-Lookup by name | Martin #6309 |
| 2026-08-11 | Filename = sanitize(BPAProcess.name), User editiert XML nicht Filename | Martin #6311 |
| 2026-08-11 | Erst Doku/Specs, dann Implementation | Martin #6313 |
