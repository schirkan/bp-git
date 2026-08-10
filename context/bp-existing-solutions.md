# Bestehende Lösungen für Blue Prism + Git/VCS

> Quellen: `web_search` (4 Läufe) + Browser-Snapshot (1 erfolgreich) am 2026-08-10.
> Direktes `web_fetch` auf BP-Community-Foren → HTTP 403 (Cloudflare). Volltexte daher nur teilweise vorhanden — Snippet-Stand.

---

## 1) BP Digital Exchange — Card #110866 „Utility — Blue Prism Release"

**Status:** ✅ **verifiziert** via OpenClaw-Browser-Snapshot
(URL: `https://digitalexchange.blueprism.com/cardDetails?id=110866`, 2026-08-10 07:18 GMT+2).

| Feld | Wert |
|---|---|
| Asset-Name | Utility - Blue Prism Release |
| Asset-Typ | Other (Tool/Utility) |
| Zweck | Read and manipulate the Xml Content in Blue Prism bprelease file outside of the Blue Prism studio |
| Updated | 02/02/2021 (**4+ Jahre alt**) |
| Author | DX Community Developer |
| Submitter | Chris Lee (DX Community Developer) |
| License | **MIT** (frei verwendbar) |
| Price | Free |
| Built For | Blue Prism Enterprise |
| Support | Community Supported |
| DIY Hours | 377 |
| Checksum | `61f3ef6d3844d9ea3358efb59cb98238dbf20f8959cfcb1596c41ab6deaad9c7` |
| Department | IT Support |
| Industry | All Industries |
| Dokumentation | „Utility - Blue Prism Release User Guide" (Link vorhanden, lädt per JS) |

**Original-Wortlaut Service Details:**

> This asset provide the features for you to amend/retrieve the desired information within the blue prism bprelease file. Make it easier for the DevOp team or IT support to extract and manipulate the bprelease file content.

**Bewertung für unseren Adapter:**

- ✅ **MIT-Lizenz** → kann als Inspiration/Vorlage dienen, evtl. sogar als Codebasis für das bprelease-Read/Write-Modul eingebunden werden (vorbehaltlich Lizenz-Check beim Studium des Quellcodes).
- ⚠️ **Letzter Update 02/02/2021** → möglicherweise nicht kompatibel mit aktuellen BP-Versionen (7.x ist aktuell). Bei Adaption: Schema-Drift prüfen.
- ⚠️ **Asset-Typ „Other"** / DIY Hours 377 → Komplexität unbekannt; kein klar definiertes API-Format. Eigene Form-Faktor-Untersuchung nötig.
- ⚠️ **Built For „Blue Prism Enterprise"** → vermutlich nur mit Enterprise-Lizenz lauffähig (nicht mit Learning Edition / Free-Tier) — relevant, falls wir den Adapter auf einer Test-Instanz prüfen wollen.
- ✅ **Funktionalität trifft den Kern unseres Adapters** (bprelease-XML außerhalb des Studios lesen/manipulieren).

**Download-Hürde:** Die Digital-Exchange-Seite hat einen „Login"-Button im Header. Ohne BP-Account nicht direkt herunterladbar. Martin: hast du einen DX-Account? Wenn ja, könnten wir die Datei ziehen und gegen unsere Implementierung kreuzen.

---

## 2) LinkedIn (Jun 2025) — Rohit Tupe: „How to use Blue Prism CLI for version control on GitHub"

> By leveraging the command-line interface, I can export and import Blue Prism processes, objects, and releases as files. These files can then be versioned with Git.

- URL: <https://www.linkedin.com/posts/tupe-rohit_blueprism-rpa-versioncontrol-activity-7342264652002689026-rbI5>
- Quelle: `web_search` Snippet — Volltext noch nicht abgerufen.

**Bewertung:** Bestätigt, dass die **offizielle BP-CLI** (im BP-Installationsverzeichnis vorhanden) Export/Import von Processes/Objects/Releases als Dateien kann — und damit **direkt als Engine** für unseren Adapter dienen könnte. Das wäre deutlich weniger invasiv als ein direkter DB-Zugriff.

**Empfehlung:** Diesen Ansatz als Top-Pfad für die Architektur-Entscheidung mitnehmen (CLI als Backend, statt Direct-SQL).

---

## 3) BP-Community-Foren (Snippet-Stand, Volltexte durch Cloudflare blockiert)

| Datum | Titel | URL-Slug | Snippet-Befund |
|---|---|---|---|
| Mai 2025 | Using Git with Blue Prism | `…/120307` | Aktuell, Erfahrungsthread. Inhalt: „Creating a better structure for development / Better version handling / Making it easier to transfer processes/objects". Volltext noch nicht abgerufen. |
| Nov 2023 | GitHub Tool for Blue prism Version Control | `…/49556` | „You can integrate GIT but generally, there is no direct integration with any version control to BP. It requires to write script". → Bestätigt: keine offizielle Integration, Skripte nötig. |
| Feb 2020 | Export Releases from BP automatically | `…/94042` | „Building a utility to create BP release xml file which will hold the extracted XML content from the exported Processes and Objects." → Ähnliches Eigenbau-Muster wie Card #110866. |
| — | Xsd file for bprelease | `…/64261` | „Is the xsd (xml schema) file for the bpreleases available somewhere?" — **Anfrage blieb unbeantwortet**. Bestätigt: kein offizielles XSD öffentlich. |
| — | BP Database Schema | `…/90536` | Gesucht für Tabellenschema; bisher nur Snippet. |

---

## 4) Offizielle BP-Doku (`bpdocs.blueprism.com`, `documentation.blueprism.com`)

- `capture-2-0/en-us/user-guide/export-process.htm` — „Blue Prism process (.xml) – Generates a skeleton process in XML format that can be imported into SS&C | Blue Prism® Enterprise".
- `capture-4-0/en-us/user-guide/export-process.htm` — analog, mit zusätzlichem Hinweis auf `.bprelease` als XML-Container für Process + auto-generated Business Objects.
- `bp-7-1/en-us/frmProcessExport.htm` — „The process export feature allows you to make a local copy of a process within your current Blue Prism database. It will be stored as an XML…"
- `bp-7-5/en-us/relman-import-release.html` — Release-Manager-Import-Doku.

Volltexte dieser Seiten sind nicht durch Cloudflare blockiert (andere Domain). Browser-Open war geplant, scheiterte aber am Tab-Lifecycle-Bug.

---

## Empfehlung für Architektur-Entscheidung

1. **CLI-basierter Ansatz** (BP-OfflineCLI / `automateC.exe`) als primärer Pfad → offiziell supportet, kein direkter DB-Zugriff nötig.
2. **DB-Direct-Fallback** für Fälle, wo die CLI nicht ausreicht (z. B. Live-Diffs ohne Release-Erstellung).
3. **Card #110866 als Schema-Referenz** für das bprelease-XML-Format — idealerweise vor der Architektur-Entscheidung herunterladen.

## Offene Recherchen

- [ ] BP-Community-Volltexte (4 Threads) — durch Cloudflare blockiert; benötigen alternative Browser-Session oder Login.
- [ ] BP-Doku-Volltexte (4 Seiten) — Domain hat kein Cloudflare, aber Browser-Tab-Lifecycle-Bug verhindert Snapshot.
- [ ] Card #110866 Download (braucht DX-Login von Martin).
