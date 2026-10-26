# SPEC-pre-receive-wiring — Pre-/Post-Receive-Logik im bpgit-server (ohne Git-Hooks)

**Status:** v0.3 — C#-Integration im HTTP-Handler; BP→Git-Preflight vor Upload-Pack und Receive-Pack; DB-CAS-Risiko dokumentiert.
**Datum:** 2026-10-26
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
  → PushOrchestrator.ExecuteAsync (RepositoryOperationLock)
    → BpToGitSyncService.SyncUnderRepositoryLockAsync
       (BP-DB materialisieren, Änderungen auf Default-Branch committen)
    → Request-Body puffern und Ref-Updates parsen
    → git receive-pack --stateless-rpc (Pack- und Ref-Apply)
    → PreReceiveHandler.HandleAsync nach erfolgreichem Ref-Apply
       (pro Ref-Update: Tree-Diff, Prozessname aus XML, DB-Lookup,
        BP `/import /forceid /overwrite`; Fehler können den Push nicht zurückweisen)
    → PostReceiveHandler.HandleAsync
       (BP-DB in den Server-Worktree materialisieren)
  → HTTP 200 + report-status
```

`git pull` / `git fetch` / `git clone` triggern keine Receive-Handler. Vor der Upload-Pack-Ref-Anzeige synchronisiert `BpToGitSyncService` jedoch die BP-Datenbank in den Default-Branch; der Client erhält diese Änderungen als normale Git-Commits.

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
| BP-Studio-Änderung zwischen BP→Git-Preflight und `/import` | Receive-Pack wird vorab mit dem DB-Stand abgeglichen; eine Änderung während des Push bleibt ohne DB-CAS ein Restrisiko, siehe §3 |
| `DeleteAsync` ist `NotImplemented` | Out of scope per Design (referenzielle Abhängigkeiten), siehe `SPEC-git-server.md` §10 |
| Processname-Unique-Constraint nicht erzwungen | BP-DB erzwingt uniqueness auf `name` per Konvention; `TOP 1` ohne `ORDER BY` ist non-deterministic bei theoretischen Duplikaten — in Praxis 1:1 |

---

---

## 3. Locking/Fork-Strategie

**Status:** BPAProcessLock-Prüfung entfernt. Der Git-Server serialisiert seine eigenen Receive- und Fetch-Syncs; BP→Git läuft vor Receive-Pack. Ein zeitgleiches BP-Studio-Update ist nicht durch einen DB-CAS abgesichert.

**Aktueller Stand:**
- `BpSyncService.ModifyAsync`: Name-Lookup → `/import` mit `/forceid`; keine `BPAProcessLock`-Abfrage.
- `BpSyncService.DeleteAsync`: `NotImplemented` by design.

**Git-Preflight:** Vor `git receive-pack` und vor Upload-Pack-Ref-Anzeige gleicht der Server BP→Git ab. Wenn der DB-Stand den Default-Branch fortschreibt, weist Git einen veralteten Push über den normalen Ref-/Fast-Forward-Check ab.

**Verbleibendes Risiko:** Eine BP-Studio-Änderung kann nach dem Preflight und vor dem AutomateC-Import erfolgen. Es gibt derzeit weder einen `lastmodifieddate`-CAS noch eine `BPAProcessLock`-Prüfung; ein solcher Race ist daher nicht ausgeschlossen. Bei späteren Abrufen ist der Datenbankstand maßgeblich und wird zurück nach Git synchronisiert.

---

## 4. Spec-Sub-Tasks (aktualisiert 2026-09-10)

| # | Sub-Task | Aufwand | Spec-Datei | Status |
|---|---|---|---|---|
| 1 | Library-Recherche + Entscheidung (§1) | 0.5 Tag | SPEC-pre-receive-wiring.md §1 | **done (2026-08-31, re-decided 2026-09-10)** |
| ~~2~~ | ~~Pack-Format-Spec (§2)~~ | — | — | **obsolete (Hybrid-Ansatz verworfen 2026-09-10)** |
| 3 | Locking/Fork-Strategie (§3) | 0.5 Tag | SPEC-pre-receive-wiring.md §3 | **spec done, impl open** |
| ~~4~~ | ~~Pre-Receive-Gate-Implementation~~ | — | — | **obsolete** |
| 5 | Pre-Receive-Wiring (C# im HTTP-Handler) | 0.5 Tag | `GitHttpHandler.HandleReceivePackAsync` | **done (2026-09-10, Phase 4b-follow-up commit `18ec5db`)** |
| 6 | Post-Receive-Wiring (Server-Worktree-Materialisierung) | 0.5 Tag | `GitHttpHandler.HandleReceivePackAsync` | **done (2026-09-10)** |
| ~~6b~~ | ~~Post-Checkout-Wiring (Client-Hook)~~ | — | — | **obsolete — Hook gestrichen 2026-09-10** |
| ~~7~~ | ~~Delete-Implementation (Phase 4b-follow-up)~~ | — | `BpSyncService.DeleteAsync` mit SqlCommand | **out of scope (Martin 2026-09-12): referenzielle Verbindungen zu `BPAObject`/`BPAProcessBackup`/`BPAAssociate`. Wird in `BpSyncService.DeleteAsync` als `NotImplemented by design` zurückgegeben und in `AGENTS.md` dokumentiert. Doku-Markierung explizit "not implemented by design".** |
| 8 | BP-DB-`lastmodifieddate`-CAS | offen | `BpSyncService` / `BpDbService` | Preflight synchronisiert BP→Git vor Push; CAS bleibt nötig, um ein BP-Studio-Update zwischen Preflight und `/import` atomar zu erkennen (siehe §3). |
| 9 | SQL-Index `IX_BPAProcess_Name` Migration | 0.25 Tag | `bpgit-server init` Migrations-Schritt | open |
| 10 | xunit-Integration gegen echtes BP-DB-Smoke | 1-2 Tage | smoke-test-script | open |

**Gesamt-Aufwand (verbleibend):** SQL-Index, CAS und Smoke-Tests. Delete + HOLDLOCK bleiben out of scope.

---

## 5. Status Quo (Phase 5+ shipped 2026-09-10)

**Stand 2026-10-26:** Pre-/Post-Receive-Logik läuft als C# im HTTP-Handler (`PushOrchestrator` + `PreReceiveHandler`/`PostReceiveHandler`). `BpToGitSyncService` gleicht vor Fetch-Ref-Anzeige und Receive-Pack die BP-Datenbank mit dem Default-Branch ab. `post-checkout` ist ersatzlos gestrichen (Commit `82a5e84`).

---

## 6. Verweise

- **Workboard-Karte `bp-git-pre-receive-wiring` (`866e5346`):** done per Commit `82a5e84` (2026-09-10), Karte aus Workboard-Backlog entfernt (Martin 2026-09-12).
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
| ~~HOLDLOCK vs `lastmodifieddate`-CAS?~~ | ~~Martin - Spec §3~~ | **Design resolved 2026-09-12:** HOLDLOCK verworfen, CAS gewählt. CAS-Implementierung bleibt offen; bis dahin schützt BP→Git-Preflight vor den meisten veralteten Pushes, aber nicht vor zeitgleichen BP-Studio-Änderungen. |
| SQL-Index `IX_BPAProcess_Name` Migration: in `bpgit-server init` oder separates Migrations-Script? | Martin — Spec §1.3 |
| `PostCheckoutHandler.cs` Source-File löschen oder als Stub behalten? | Martin — Cleanup |
| AGENTS.md Backlog-Block (iv) auf "done" setzen? | Martin — Doku-Hygiene |