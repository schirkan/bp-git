# SPEC-pre-receive-wiring — Pre-/Post-Receive-Logik im bpgit-server (ohne Git-Hooks)

**Status:** v0.2 — Hooks entfernt, C#-Integration in bpgit-server HTTP-Handler, post-checkout gestrichen, SQL-Query-Verifikation abgeschlossen.
**Datum:** 2026-09-10
**Autor:** bpgit-Projekt
**Bezug:** Workboard-Karte `bp-git-pre-receive-wiring` (ID `866e5346`, priority urgent), SPEC-git-server §7/§9, AGENTS.md Backlog (iv)
**Mitgeltend:** `SPEC-git-server.md`, `SPEC-adapter-architecture.md`, `SPEC-target-environment.md`

**Architektur-Wechsel:** Vorher war ein Hybrid-Ansatz mit Shell-Hook-Scripts geplant (`pre-receive`/`post-receive`/`post-checkout` als Git-Hook-Files im `<bare-repo>/hooks/`). **Entschieden ist:** **Keine Git-Hooks.** Alle Logik läuft als C# im `bpgit-server`-HTTP-Handler. `post-checkout` entfällt komplett, weil das Filename-Format (`sanitize(BPAProcess.name) + ".xml"`, per Martin #6311) bereits kanonische Namen liefert — kein Client-Rename nötig.

---

## 1. Entscheidung

### 1.1 Status quo (Stand 2026-09-10)

`src/BPGit.Server/GitHttp/PreReceiveHandler.cs` und `PostReceiveHandler.cs` existieren als Library-Handler und sind via DI als Singleton registriert. (`PostCheckoutHandler.cs` wurde 2026-09-10 geloescht, Commit `82a5e84`.)

**Aktuelle Verdrahtung** (Stand 2026-09-10):
- `GitHttpHandler.HandleReceivePackAsync` ruft die Library-Handler direkt aus dem HTTP-Request-Lifecycle auf (kein Git-Hook-Script-Bridge mehr)
- `PostCheckoutHandler` ist **nicht mehr im Einsatz** — gestrichen, da das Filename-Format bereits kanonische Namen liefert
- Ref-Apply läuft weiterhin über `git --stateless-rpc` (commit `18ec5db`, Phase 4b-follow-up), weil LibGit2Sharp 0.32.0 keine Server-side `receive-pack`-API hat

**Was der User (Martin) am 2026-09-10 entschieden hat:**
1. Keine Git-Hook-Scripts in `<bare-repo>/hooks/` — keine `core.hooksPath`, kein `bpgit install-hooks`
2. `pre-receive` + `post-receive` als C# im bpgit-server HTTP-Handler
3. `post-checkout` komplett entfernen
4. Filename = `sanitize(BPAProcess.name) + ".xml"` (bereits per #6311 etabliert, keine Änderung)
5. processid-Lookup über direkte SQL-Query (`BPAProcess WHERE name = @name`), **kein** separater denormalisierter Cache in bpgit-DB
6. SQL-Performance-Optimierung: Nonclustered-Index auf `BPAProcess(name)` als pre-receive-Optimization

### 1.2 Architektur-Flow (Ist-Stand)

```
HTTP POST /<repo>/git-receive-pack
  → GitHttpHandler.HandleReceivePackAsync
    → PreReceiveHandler.HandleAsync(repo, oldrev, newrev, refname)
       (parsed Tree-Diff, pro File:
          - XmlNameRegex extrahiert name aus XML-Content
          - BpDbService.LookupProcessIdByNameAsync(name)  ← SQL-Query
          - BpSyncService.AddAsync/ModifyAsync  → /import /forceid /overwrite
          - BpSyncService.DeleteAsync  → NotImplemented (Phase 4b-follow-up)
       )
    → git receive-pack --stateless-rpc  (Ref-Apply + Pack-Apply)
    → PostReceiveHandler.HandleAsync
       (BP `/import` Sync, WorktreeSyncService.MaterializeAsync)
  → HTTP 200 + report-status
```

`git pull` / `git fetch` / `git clone` triggern keinen pre/post-receive auf dem Server — sie machen nur lesend. Materialisierung ins lokale Worktree entfällt, weil das Filename-Format bereits kanonische Namen liefert (Server-Push → Bare-Repo → Client-Clone ohne Rename).

### 1.3 Processid-Lookup-SQL (verifiziert 2026-09-10)

**Query** (`BpDbService.LookupProcessIdByNameAsync`):
```sql
SELECT TOP 1 processid FROM BPAProcess WHERE name = @name
```

**Verifikation:**
- ✓ Parametrisiert (kein SQL-Injection, Plan-Reuse via `@name`)
- ✓ `TOP 1` Early-Exit
- ✓ Null-Handling (`result is Guid g`)
- ✓ Default-Collation `SQL_Latin1_General_CP1_CI_AS` ist case-insensitive, treatiert Trailing-Spaces gleich

**Performance:**
- `BPAProcess.name` hat aktuell **keinen** expliziten Index (Schema-Doku `bp-database-schema.md` listet nur FKs, keine Indexes)
- Frischinstallation: 0 Rows → egal
- Produktion mit >1000 Prozessen → Clustered-Index-Scan, langsam

**Empfohlene Schema-Erweiterung** (in `bpgit-server init` als Migrations-Schritt):
```sql
CREATE NONCLUSTERED INDEX IX_BPAProcess_Name ON BPAProcess(name)
  WHERE name IS NOT NULL AND name <> '';
```
Der Filter auf `name IS NOT NULL` spart Platz, weil BP auch Rows mit `NULL`/Leerstring enthalten kann (siehe `GetAllProcessesAsync`-Where-Clause).

**Cache-Entscheidung:** Kein separater `bpgit-server`-DB-Cache nötig. Die BP-DB selbst ist Single Source of Truth. Die Query ist deterministisch + idempotent + ausreichend schnell mit Index.

### 1.4 Warum keine Git-Hooks (Begründung)

| Hook-Alternative | Problem |
|---|---|
| Shell-Script `pre-receive` in `<bare-repo>/hooks/` | Zusätzlicher Installationsschritt beim `bpgit-server init`, zweite Fehlerquelle (Shell vs. C#), keine atomare Transaktion mit dem HTTP-Request |
| Shell-Script `post-receive` in `<bare-repo>/hooks/` | Gleiche Probleme wie pre-receive; zudem kein direkter Zugriff auf `BPGit.Server`-DI-Container |
| Client-Side `post-checkout` Hook | Martin-Entscheid (2026-09-10): **keine Client-Hooks**. Worktree-Materialisierung entfällt, weil Filename bereits kanonisch. |
| `core.hooksPath` Symlink (BP-DB-Materialisierung lokal) | Selbe Argumentation — Client-Automation nicht gewünscht |

**Ergebnis:** Alle Logik im C#-HTTP-Handler. Keine Hook-Files, keine `core.hooksPath`, kein `bpgit install-hooks`. `bpgit-server init` wird schlanker.

### 1.5 Risiken / Trade-offs (aktualisiert)

| Risiko | Mitigation |
|---|---|
| SQL-Query ohne Index → langsam bei vielen Prozessen | Nonclustered-Index `IX_BPAProcess_Name` als Migrations-Schritt (siehe §1.3) |
| TOCTOU zwischen Lock-Check und `/import` in `BpSyncService` | Bekanntes Finding #7 (Code-Review 2026-08-30); Mitigation via HOLDLOCK oder `lastmodifieddate`-CAS, siehe §3 |
| `DeleteAsync` ist `NotImplemented` | Phase 4b-follow-up: SqlCommand DELETE + BPAAuditEvent sCode=P005 |
| Processname-Unique-Constraint nicht erzwungen | BP-DB erzwingt uniqueness auf `name` per Konvention; `TOP 1` ohne `ORDER BY` ist non-deterministic bei theoretischen Duplikaten — in Praxis 1:1 |

---

---

## 3. Locking/Fork-Strategie

**Status:** aktiv — TOCTOU-Race bekannt, Mitigation offen.

**Aktueller Stand:**
- `BpSyncService.ModifyAsync`: Lookup → Lock-Check → `/import` mit `/forceid` — TOCTOU zwischen Lock-Check und Import (Finding #7 aus Code-Review 2026-08-30)
- `BpSyncService.DeleteAsync`: `NotImplemented` (Phase 4b-follow-up)

**Strategie-Optionen (zur Entscheidung):**
- **HOLDLOCK** via `BPAProcessLock`-Tabelle: vor `/import` einfügen, danach wieder löschen. Blockiert andere Sessions für die Dauer des Imports.
- **`lastmodifieddate`-CAS**: vor `/import` lesen, nach `/import` nochmal vergleichen, bei Differenz Push ablehnen. Keine Lock-Tabelle nötig, aber komplexer.

**Empfehlung:** vorerst HOLDLOCK (existierende `BPAProcessLock`-Tabelle wird bereits read-only abgefragt in `BpDbService.GetProcessLockAsync`). Schema-Erweiterung minimal, Tests gegen lokale localdb aussagekräftig.

**Sub-Task Phase 5+:** HOLDLOCK-Implementation + Race-Condition-Tests (`tests/BPGit.Server.Tests/PreReceiveRaceTests.cs`).

---

## 4. Spec-Sub-Tasks (aktualisiert 2026-09-10)

| # | Sub-Task | Aufwand | Spec-Datei | Status |
|---|---|---|---|---|
| 1 | Library-Recherche + Entscheidung (§1) | 0.5 Tag | SPEC-pre-receive-wiring.md §1 | **done (2026-08-31, re-decided 2026-09-10)** |
| ~~2~~ | ~~Pack-Format-Spec (§2)~~ | — | — | **obsolete (Hybrid-Ansatz verworfen 2026-09-10)** |
| 3 | Locking/Fork-Strategie (§3) | 0.5 Tag | SPEC-pre-receive-wiring.md §3 | **spec done, impl open** |
| ~~4~~ | ~~Pre-Receive-Gate-Implementation~~ | — | — | **obsolete** |
| 5 | Pre-Receive-Wiring (C# im HTTP-Handler) | 0.5 Tag | `GitHttpHandler.HandleReceivePackAsync` | **done (2026-09-10, Phase 4b-follow-up commit `18ec5db`)** |
| 6 | Post-Receive-Wiring (BP-DB-Sync) | 0.5 Tag | `GitHttpHandler.HandleReceivePackAsync` | **done (2026-09-10)** |
| ~~6b~~ | ~~Post-Checkout-Wiring (Client-Hook)~~ | — | — | **obsolete — Hook gestrichen 2026-09-10** |
| 7 | Delete-Implementation (Phase 4b-follow-up) | 1 Tag | `BpSyncService.DeleteAsync` mit SqlCommand | open |
| 8 | HOLDLOCK-Implementation + Race-Tests | 1 Tag | `tests/BPGit.Server.Tests/PreReceiveRaceTests.cs` | open |
| 9 | SQL-Index `IX_BPAProcess_Name` Migration | 0.25 Tag | `bpgit-server init` Migrations-Schritt | open |
| 10 | xunit-Integration gegen echtes BP-DB-Smoke | 1-2 Tage | smoke-test-script | open |

**Gesamt-Aufwand (verbleibend):** ~3-4 Tage (Delete + HOLDLOCK + Index-Migration + Tests).

---

## 5. Status Quo (Phase 5+ shipped 2026-09-10)

**Stand 2026-09-11:** Pre-/Post-Receive-Logik läuft als C# im `bpgit-server`-HTTP-Handler (`PushOrchestrator` + `PreReceiveHandler`/`PostReceiveHandler`). `post-checkout` ist ersatzlos gestrichen (Commit `82a5e84`). Der ehemalige `bpgit pull`-Workaround entfällt, weil der Pull-Flow jetzt Single-Stage ist (Standard `git pull` reicht — siehe `SPEC-git-server.md` §9).

---

## 6. Verweise

- **Workboard-Karte:** `bp-git-pre-receive-wiring` (ID `866e5346`, priority urgent)
- **Specs:**
  - `specs/SPEC-git-server.md` §5 (Git-Server Stack) + §7 (Pre-/Post-Receive-Logik, **umbenennen von "Server-Side Hooks"**) + §9 (Push-Flow)
  - `specs/SPEC-adapter-architecture.md` (Worktree-Layout, processid-Mapping)
- **Code:**
  - `src/BPGit.Server/GitHttp/PreReceiveHandler.cs` (Library, gewired)
  - `src/BPGit.Server/GitHttp/PostReceiveHandler.cs` (Library, gewired)
  - `src/BPGit.Server/GitHttp/PostCheckoutHandler.cs` (**gestrichen — File löschen**)
  - `src/BPGit.Server/GitHttp/GitHttpHandler.cs` (delegiert an native git-CLI + Library-Handler)
  - `src/BPGit.Server/Services/BpDbService.cs` (SQL-Lookup via `LookupProcessIdByNameAsync`)
  - `src/BPGit.Server/Services/BpSyncService.cs` (processid-Resolution + `/import`-Sync)
  - `src/BPGit.Server/Services/WorktreeSyncService.cs` (BP-DB → Worktree Materialization)
- **Tests:**
  - `tests/BPGit.Server.Tests/PreReceiveHandlerTests.cs` (12 Tests grün)
  - `tests/BPGit.Server.Tests/WalkTreeEntriesTests.cs` (4 Tests grün)
  - `tests/BPGit.Server.Tests/BpSyncServiceTests.cs` (mit FakeBpDbService)
- **Code-Review 2026-08-30:** Finding #5 (Delete notImpl) + #7 (TOCTOU-Race) — **Finding #1 (Hooks tot) resolved 2026-09-10**
- **AGENTS.md:** Backlog-Block (iv) Hook-Wiring — **Block kann auf "done" gesetzt werden, da Hooks gestrichen**

---

## 7. Open Questions

| Frage | Entscheidung nötig |
|---|---|
| HOLDLOCK vs `lastmodifieddate`-CAS? | Martin — Spec §3 |
| SQL-Index `IX_BPAProcess_Name` Migration: in `bpgit-server init` oder separates Migrations-Script? | Martin — Spec §1.3 |
| `PostCheckoutHandler.cs` Source-File löschen oder als Stub behalten? | Martin — Cleanup |
| AGENTS.md Backlog-Block (iv) auf "done" setzen? | Martin — Doku-Hygiene |