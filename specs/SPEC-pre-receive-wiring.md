# SPEC-pre-receive-wiring — Pre-/Post-Receive- und Post-Checkout-Hooks im Server

**Status:** v0.1 Draft — §1 Research & Decision (Stand 2026-08-31), §2 Pack-Format + §3 Locking als Stub für Folge-Runden.
**Datum:** 2026-08-31
**Autor:** bpgit-Projekt
**Bezug:** Workboard-Karte `bp-git-pre-receive-wiring` (ID `866e5346`, priority urgent), SPEC-git-server §7/§9 Disclaimer, AGENTS.md Backlog (iv)
**Mitgeltend:** `SPEC-git-server.md`, `SPEC-adapter-architecture.md`, `SPEC-target-environment.md`

---

## 1. Library-Recherche & Entscheidung

### 1.1 Status quo (Stand 2026-08-31)

`src/BPGit.Server/GitHttp/PreReceiveHandler.cs` + `PostReceiveHandler.cs` + `PostCheckoutHandler.cs` existieren als Library-Handler (commits `37fc525`, `f399ad1`), sind via DI als Singleton registriert, aber von `GitHttpHandler.HandleReceivePackAsync` / `HandleUploadPackAsync` **nicht aufgerufen** (verifiziert 2026-08-30 via Code-Review). Grund: `LibGit2Sharp 0.32.0` (verwendete Version in `BPGit.Server.csproj`) hat keine public Server-side `receive-pack`-API. Aktuelle Implementation delegiert an native `git --stateless-rpc` (commit `18ec5db`, Phase 4b-follow-up).

### 1.2 Optionen evaluiert

| Option | Quelle | Status | Server-side receive-pack | Empfehlung |
|---|---|---|---|---|
| **LibGit2Sharp aktuell** | NuGet `LibGit2Sharp 0.32.0`, GitHub `libgit2/libgit2sharp` | maintained, libgit2-Backend v1.8.x | **Nein** (kein `Network.ReceivePack` o.ä. in 0.32.0-API; in den GitHub-Releases-Diffs der letzten 12 Monate nicht hinzugekommen) | Library bleibt für Client-/Read-Pfad; Hook-Wiring separat lösen |
| **LibGit2Sharp neuer** | NuGet: keine neuere stable Version sichtbar; GitHub-Releases-Seite erwähnt einen v1.8.6-Security-Release (libgit2-Backend) | unklar, ob auf NuGet verfügbar oder noch im PR-Stadium | **Nein** dokumentiert | Nächste Phase-5-Runde: NuGet-Pakete nach `0.32.1+` / `0.33+` durchsuchen, ggf. Update evaluieren |
| **SharpGit** | `AmpScm/SharpGit` auf GitHub (Mirror-Repo) | 1 Star, 1 Watcher, Build via vcpkg + libgit2 | Unklar (keine Doku zu Server-side-Hooks) | **Nein** — Mirror/kein klares Maintained-Signal |
| **NGit** | `mono/ngit` auf GitHub (JGit-Port, semi-auto-generiert via Sharpen) | Letzter Commit-Datums unklar, automatisch generiert | Unklar (kein Server-side-Hook-Fokus) | **Nein** — automatische Konvertierung macht API-Stabilität fragwürdig |
| **Eigener Pack-Parser** | Eigene Implementation in C# | Kontrolliert, deterministic | **Ja** (manuelles Parsen + Ref-Update via `Repository.WriteRef` oder `Refs.Add/UpdateTarget`) | **Empfehlung** — siehe §1.3 |

### 1.3 Empfehlung: Hybrid-Ansatz (libgit2-native-git + eigene Pack-Stream-Analyse)

Wir behalten die existierende `git --stateless-rpc`-Delegation für ref-update + pack-apply und fügen eine **Pre-Receive-Gate** davor ein, die den Pack-Stream liest und validiert:

**Flow:**
1. Client `POST /git-receive-pack` schickt pkt-line mit Ref-Update-Wishes + Pack.
2. Server parst den eingehenden Stream:
   - Liest `want-ref`-Liste (welche Refs will der Client updaten, alt+neu)
   - Liest Pack (Blobs, Trees, Commits, Tags) aus dem Stream
   - **PreReceiveHandler** läuft auf den Tree-Diffs **vor** Ref-Apply (processid-Lookup, Lock-Check, `/import` pro Modify/Add, DeleteNotImplemented für D)
3. Pre-Receive-Ergebnis:
   - **OK** → Stream wird an `git receive-pack --stateless-rpc` weitergereicht, der die Ref-Updates durchführt + report-status zurückgibt
   - **Fail** → Server sendet error pkt-line zurück, schließt Pipe (git-receive-pack bricht mit EPIPE ab, kein ref-update)
4. PostReceiveHandler läuft auf der Server-Seite **nach** erfolgreichem Ref-Apply (Worktree-Materialization via `WorktreeSyncService.MaterializeAsync`).
5. PostCheckoutHandler läuft auf `git-upload-pack`-Antwort (für clone/branch-checkout-Worktree-Refresh).

**Realisierungs-Detail:**
- Pre-Receive-Gate: zwei `Pipe`-Streams, einer zum Client-Request-Body, einer zum git-CLI-Stdin
- Pack-Stream-Parser: manuell, siehe §2 Stub
- Ref-Update-Logik bleibt im git-CLI (proven, alle edge-cases abgedeckt)
- Locking-Strategie: siehe §3 Stub

### 1.4 Warum nicht auf eine LibGit2Sharp-Update warten?

libgit2 v1.8.6-Security-Release ist auf der GitHub-Releases-Seite von libgit2/libgit2sharp erwähnt, aber LibGit2Sharp auf NuGet zeigt weiterhin 0.32.0 als latest. Selbst bei einem LibGit2Sharp-Update auf 0.33+ ist **nicht garantiert**, dass eine Server-side `receive-pack`-API hinzukommt — libgit2 hat historisch keine Server-side-Pack-API angeboten (Stand Spec §9 + AGENTS.md Backlog (ii)).

Eigener Pack-Parser ist deshalb der **robusteste** Pfad, unabhängig von LibGit2Sharp-Updates.

### 1.5 Risiken / Trade-offs

| Risiko | Mitigation |
|---|---|
| Pack-Encoding-Komplexität (ofs-delta, ref-delta, side-band-64k) | §2 Stub dokumentiert das vollständige Encoding; ggf. erstmal nur ofs-delta + uncompressed supporten, dann erweitern |
| Locking-Race-Conditions zwischen Pre-Receive und Ref-Apply | §3 Stub dokumentiert HOLDLOCK-Strategie; Tests gegen lokale localdb |
| Performance-Verschrechrechung (zweite Stream-Kopie) | Pre-Receive-Gate ist minimal: nur Ref-Liste + Pack-Header parsen, nicht der ganze Tree (das macht git-CLI beim Apply) |
| Kompatibilitätsbruch bei LibGit2Sharp-Update | Hybrid-Ansatz überlebt API-Änderungen, weil die libgit2-Refs-API (`Refs.Add/UpdateTarget`) seit 0.27 stabil ist |

---

## 2. Pack-Format-Handling (Stub)

**Status:** Skizze — Detail-Spec in Folge-Runde nach Library-Recherche-Entscheidung.

**TODO Phase 5+:**
- pkt-line-Format dokumentieren (siehe gitprotocol-pack.txt)
  - `0000` Flush
  - `0001` Delim
  - `<len><payload>\n` Data (len = 4 + payload-bytes, LF nicht mitgerechnet)
- Pack-Format dokumentieren (siehe git pack-format.txt)
  - Header: `PACK<version><num-objects>`
  - Object-Types: OBJ_COMMIT(1), OBJ_TREE(2), OBJ_BLOB(3), OBJ_TAG(4), OBJ_OFS_DELTA(6), OBJ_REF_DELTA(7)
  - Delta-Encoding: ofs-delta (variable-length offset), ref-delta (20-byte SHA1)
  - Trailer: SHA1 des Pack-Contents
- Side-Band-64k: pkt-line-gewrapper Channel für Progress-Messages (Channel 1 = Pack-Data, Channel 2 = Progress, Channel 3 = Error)

**Existierende Code-Basis:**
- `Pkt.WriteDataAsync` + `WriteServiceHeaderAsync` + `WriteFlushAsync` + `WriteDelimAsync` sind bereits im Code (`src/BPGit.Server/GitHttp/GitHttpHandler.cs`)
- Inverse (`ReadDataAsync`, `ReadFlushAsync`) trivial zu implementieren: `BinaryPrimitives.ReadUInt32BigEndian` + `await stream.ReadExactlyAsync(4 + len - 4)`

**Sub-Task:** Spec-Vollversion + Sample-Decoder-Implementation.

---

## 3. Locking/Fork-Strategie (Stub)

**Status:** Skizze — Detail-Spec in Folge-Runde.

**TODO Phase 5+:**
- Atomic-Lock via `BPAProcessLock`-Tabelle (existiert, wird heute read-only abgefragt in `BpDbService.GetProcessLockAsync`)
- Alternative: Optimistic-Concurrency mit `lastmodifieddate` (siehe Spec §10) — vor `/import` lesen, nach `/import` nochmal vergleichen, bei Differenz Push ablehnen
- Fork-Strategie: pre-apply-ref-checkout (validate-then-apply) statt apply-then-validate (post-apply side-effect)

**Existierende Code-Basis:**
- `BpSyncService.ModifyAsync` macht heute: Lookup → Lock-Check → `/import` mit `/forceid` — TOCTOU zwischen Lock-Check und Import (Finding #7 aus Code-Review 2026-08-30)
- `BpSyncService.DeleteAsync` ist aktuell `NotImplemented`

**Sub-Task:** Strategie-Entscheidung (HOLDLOCK vs lastmodifieddate-CAS) + Tests gegen Race-Conditions.

---

## 4. Spec-Sub-Tasks (Reihenfolge)

| # | Sub-Task | Aufwand | Spec-Datei | Status |
|---|---|---|---|---|
| 1 | Library-Recherche + Entscheidung (§1) | 0.5 Tag | SPEC-pre-receive-wiring.md §1 | **done (2026-08-31)** |
| 2 | Pack-Format-Spec (§2) | 1 Tag | SPEC-pre-receive-wiring.md §2 | stub |
| 3 | Locking/Fork-Strategie-Spec (§3) | 0.5 Tag | SPEC-pre-receive-wiring.md §3 | stub |
| 4 | Pre-Receive-Gate-Implementation | 1-2 Wochen | `src/BPGit.Server/GitHttp/PreReceiveGate.cs` | open |
| 5 | Post-Receive-Wiring | 0.5 Tag | `GitHttpHandler.HandleReceivePackAsync` erweitern | open |
| 6 | Post-Checkout-Wiring | 0.5 Tag | `GitHttpHandler.HandleUploadPackAsync` erweitern | open |
| 7 | Delete-Implementation (Phase 4b-follow-up) | 1 Tag | `BpSyncService.DeleteAsync` mit SqlCommand | open |
| 8 | Race-Condition-Tests (HOLDLOCK vs CAS) | 1 Tag | `tests/BPGit.Server.Tests/PreReceiveRaceTests.cs` | open |
| 9 | xunit-Integration gegen echtes BP-DB-Smoke | 1-2 Tage | smoke-test-script | open |

**Gesamt-Aufwand:** 4-6 Wochen (geschätzt, abhängig von Pack-Format-Komplexität).

---

## 5. Workaround bis Phase 5+ shipped

Aktueller Stand (verifiziert 2026-08-30):
- `git push` schreibt unkontrolliert ins Bare-Repo (keine BP-DB-Validierung)
- `git pull` lädt nur gepushten Stand (keine BP-DB-Materialization)
- BP-Studio-Edits propagieren nicht in Worktrees

**Workaround für User:** nach jedem BP-Studio-Edit manuell `bpgit pull` auf OpenClawPC triggern. Hook-Disclaimer in README.MD Z. 64 dokumentiert.

---

## 6. Verweise

- **Workboard-Karte:** `bp-git-pre-receive-wiring` (ID `866e5346`, priority urgent)
- **Specs:**
  - `specs/SPEC-git-server.md` §7 (Server-Side Hooks) + §9 (Push-Flow) + Disclaimer am Doc-Anfang
  - `specs/SPEC-adapter-architecture.md` (Worktree-Layout, processid-Mapping)
- **Code:**
  - `src/BPGit.Server/GitHttp/PreReceiveHandler.cs` (Library, nicht gewired)
  - `src/BPGit.Server/GitHttp/PostReceiveHandler.cs` (Library, nicht gewired)
  - `src/BPGit.Server/GitHttp/PostCheckoutHandler.cs` (Library, nicht gewired)
  - `src/BPGit.Server/GitHttp/GitHttpHandler.cs` (delegiert an native git-CLI)
- **Tests:**
  - `tests/BPGit.Server.Tests/PreReceiveHandlerTests.cs` (12 Tests grün)
  - `tests/BPGit.Server.Tests/WalkTreeEntriesTests.cs` (4 Tests grün)
- **Code-Review 2026-08-30:** Finding #1 (Hooks tot) + #5 (Delete notImpl) + #7 (TOCTOU-Race)
- **AGENTS.md:** Backlog-Block (iv) Hook-Wiring

---

## 7. Open Questions

| Frage | Entscheidung nötig |
|---|---|
| Locking-Strategie: HOLDLOCK vs lastmodifieddate-CAS? | Martin — Spec-Diskussion §3 |
| Pack-Encoding-Support-Scope: nur ofs-delta + uncompressed für MVP, oder alle? | Martin — Spec-Diskussion §2 |
| Side-Band-64k Support in Pre-Receive-Gate? (für Progress-Messages an Client) | Martin — Spec-Diskussion §2 |
| Delete-Implementation: SqlCommand DELETE BPAProcess + BPAAuditEvent sCode=P005? | Martin — Phase 4b-follow-up (Spec §5/§9) |