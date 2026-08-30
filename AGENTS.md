# BP-Git

## Zweck

Git-Workflows für Blue Prism Processes und Objects via self-hosted C#-Server (Kestrel + LibGit2Sharp) auf OpenClawPC. Developer-Workstations benutzen ausschliesslich Standard-`git` — kein `bpgit.exe` lokal.

## Aktueller Status

- **Architektur-Switch zu Git-Server** (Martin #6295, 18:17): Hooks laufen serverseitig, kein Client-Bloat.
- **processid-Mapping final** (Martin #6311, 21:42): Filename = `sanitize(BPAProcess.name)`, abgeleitet nicht autoritativ. Mapping via `git diff`-Status (R/M/A/D) + `BPAProcess.name`-DB-Lookup.
- **Kein** `snapshot.json`, **kein** `folders.json` im Worktree. Pure XML + git.
- **Specs + Doku up-to-date** (Stand 2026-08-12, Martin #6401-Doku-Pass):
  - `specs/SPEC-git-server.md` (Martin #6429: verschoben von context/ fuer konsistente specs/-Convention) — **v0.3 Draft** (Phase-4c + 4b-follow-up + xunit-Tests + LibGit2Sharp-0.32.0-Limitationen; Kapitel 9 added `cbaa279`)
  - `specs/SPEC-adapter-architecture.md` — **v4** (Worktree-Layout, processid-Mapping, XML-Serialisierung; Phase 4c + xunit-Tests)
  - `specs/SPEC-target-environment.md` — **v2** (OpenClawPC, .NET 10, BP 7.5.1; verifiziert nach xunit-Tests-Welle)
  - `README-bpgit-git.md` — End-User-Quickstart (Footer mit Test-Stand + Spec-Versionen angereichert 2026-08-12)
- **Implementation: Phasen 4a (Kestrel) + 4b (pre-receive) + 4b-follow-up (git-CLI receive-pack + upload-pack delegation) + 4c (PostReceive/PostCheckout Hooks + WorktreeSyncService) done.**
- **xunit-Tests-Welle done** (12 Test-Commits, 65 gruen + 4 skipped in 3 Test-Projekten).
- **Backlog Phase 5+** (Martin #6401 nach Doku-Pass): (i) 3 PreReceive HEAD-Tracking-Tests scheitern mit `Assert.Single() collection empty` (Issue-#802 workaround in commit `2fa730d`; tree.Count vs parents[0].tree.Count vergleichen); (ii) LibGit2Sharp 0.32.0 ist einzige stabile Version auf NuGet (libgit2-v1.8.6-Security-Release in Vorbereitung, noch nicht stable); (iii) MVP1-Deployment (`bpgit-server.exe` als Windows-Service + SPN + Firewall) + Demo-DB-Cleanup (1 `BPARelease`-Row `releaseid=2` + 35 `BPAReleaseEntry`-Rows DELETE) — gestrichen per #6385.
- **Hook-Status (Code-Review 2026-08-30):** `PreReceiveHandler` / `PostReceiveHandler` / `PostCheckoutHandler` existieren als Library-Handler (commits `37fc525`, `f399ad1`), voll getestet, **aber NICHT verdrahtet** in `GitHttpHandler.HandleReceivePackAsync` / `HandleUploadPackAsync`. Grund: libgit2 0.32.0 hat keine Server-side receive-pack-API. Workaround-Karte `bp-git-pre-receive-wiring` (urgent, Workboard-ID `866e5346`). Konsequenz: `git push` schreibt unkontrolliert; `git pull` materialisiert nicht aus BP-DB. Worktree-Shell-Hooks aus Phase 2a wurden 2026-08-30 entfernt (Spec §13).

## Project Files

- `AGENTS.md` — Projekt-Übersicht, Status, Git-Sektion (diese Datei)
- `README-bpgit-git.md` — End-User-Doku (Quickstart, Workflows, Filename-Regeln)
- `specs/SPEC-adapter-architecture.md` — Adapter-Layer-Architektur (Worktree, Mapping)
- `specs/SPEC-target-environment.md` — OpenClawPC, .NET 10, BP 7.5.1
- `specs/SPEC-git-server.md` (Martin #6429: verschoben von context/ fuer konsistente specs/-Convention) — Git-Server-Architektur, Hooks, Auth, Deployment
- `context/bp-cli-reference-7.5.1.md` — AutomateC.exe CLI-Referenz 7.5.1
- `context/bp-database-schema.md` — BP-Schema-Doku
- `context/bp-existing-solutions.md` — Bestehende Alternativen (BP-Diff, etc.)
- `context/automatec-help-7.5.1.txt` — Roh-Help-Output von AutomateC.exe
- `src/BPGit.Cli/` — Adapter-CLI (für Admin-Tasks am Server: `init`, `server start/stop`, `log`)
- `src/BPGit.Data/` — Data-Layer (POCOs, Dapper, Repository)
- `src/BPGit.Format/` — XML-Serialisierung
- `tests/` — xunit-Tests (`BPGit.Server.Tests`, `BPGit.Data.Tests`, `BPGit.Cli.Tests`)

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
- `bp-git-pre-receive-wiring` (Karte `866e5346`, priority **urgent**, Board bp-git) — Pre-/Post-Receive/Checkout-Hooks im Server verdrahten. Library-Handler existieren (Phase 4b/4c, commits `37fc525` + `f399ad1`), sind aber nicht aufgerufen. Root Cause: libgit2 0.32.0 hat keine Server-side receive-pack-API. Specs-Work zuerst (Pack-Format, Locking, Fork-Strategie); dann Implementation + xunit-Integration gegen echtes BP-DB-Smoke-Setup.

Erledigt:
- Phase 1 MVP (commit `b6d7e02`)
- Phase 2a `bpgit commit` via AutomateC.exe (`2b53c4f`, `663a07f`)
- Phase 2b `bpgit log` + `bpgit diff` (`41842c7`, `76da584`)
- Phase 3 SnapshotEntry + PullCommand folder-aware (`e0e0109`) — **durch Git-Server-Architektur obsolet**
- Phase 2c Hooks (`98e9d43f`) — **obsolet** per #6295 (Hooks laufen serverseitig)
- Doku-Round v0.2 (SPEC-git-server, SPEC-adapter-architecture, README-bpgit-git)
- **Phase 4a** bpgit-git-server Kestrel + LibGit2Sharp (commit `9c53960`)
- **Phase 4b** pre-receive Hook (processid-Lookup + `/import /forceid /overwrite`) (commits `d0f87b3`, `37fc525`)
- **Phase 4b-follow-up** receive-pack + upload-pack delegation via `git -C <bare> ... --stateless-rpc` (commits `18ec5db`, `f7dc718`)
- **Phase 4c** WorktreeSyncService + PostReceive/PostCheckout Hooks (BP-DB Sync) (commits `d2fd04f`, `f399ad1`)
- **xunit-Tests-Welle** (Martin #6385+#6401): xunit scaffold (`8b4ee35`), `IBpDbService` extrahieren + `MaterializeAsync` Tests (`666e6f7`), `IBpSyncService` extrahieren + PreReceive-Tests (`4c8c8e9`), `BpSyncService` `IBpDbService` ctor + Fehler-Pfad-Tests (`e194869`), `AssemblyInfo` + `StripLeadingXmlComments` internal Helpers (`0a668e1`), `PreReceiveHandler`-internals (`4de3138`), `ServerConfig.Load` (`a2e2b32`), `Pkt` pkt-line (`0141ff9`), `IsZeroSha` nullable + `ConnectionFactory` (`47b2417`), `Data.Tests` `ProjectReference` (`226fc38`), `Cli.Tests` scaffold + `SnapshotStore` V2 (`e7de7bb`), `PreReceive` HEAD-Tracking mit Issue-#802 workaround skip-attributed (`2fa730d`).

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
| 2026-08-12 | BP-Passwort aus env in `bpgit-server.json` (kein env-var mehr) | Martin (impliziert aus #6359) |
| 2026-08-12 | LibGit2Sharp 0.32.0: `ObjectDatabase.CreateCommit(Signature, Signature, string, Tree, Commit[], bool)` 6-arg positional; `CreateBlob(Stream)`; `TreeDefinition.Add(path, blob, Mode)`; `Refs.Add(string, ObjectId)` + `Refs.UpdateTarget(Reference, ObjectId)` | Diagnose-Befund |
| 2026-08-12 | MVP1-Deployment + Demo-DB-Cleanup gestrichen | Martin #6385 |
| 2026-08-12 | Doku + Tests + Refactoring komplettieren (Phase 1/2/3) | Martin #6401 |
| 2026-08-30 | Workstation-Shell-Hooks (`InstallGitHooksAsync`) entfernt; `--install-hooks` als deprecated no-op | Spec §13 umgesetzt (Martin #6295) |
| 2026-08-30 | Hook-Libraries `PreReceive`/`PostReceive`/`PostCheckout` als "Library vorhanden, NICHT gewired" dokumentiert | Code-Review #1, Spec §7 + §9 + Doc-Anfang-Disclaimer, Backlog-Karte `866e5346` |
