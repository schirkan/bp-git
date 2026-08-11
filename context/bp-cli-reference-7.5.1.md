# AutomateC.exe — Blue Prism 7.5.1 CLI Reference

**Quelle:**
- Primaer: lokales `AutomateC.exe /help` Output (Version 7.5.1.18099, 200 Zeilen) — `context/automatec-help-7.5.1.txt`
- Online: `https://documentation.blueprism.com/bp-7-5/en-us/helpCommandLine.htm` (bp-7-5-1 gibt es nicht, 404; bp-7-2 verfuegbar, Inhalt weitgehend identisch)
- Installierte Version auf OpenClawPC: 7.5.1.18099 (Registry: `HKLM:\SOFTWARE\Blue Prism Limited\Automate\Version`)

**Datum:** 2026-08-11

**Autor:** bpgit-Projekt (Phase-2-Recherche zur BP-History via CLI)

---

## 1. Overview

Blue Prism liefert zwei Utilities mit Command-Line-Switches:

- **Automate.exe** — grafische BP-Anwendung, Messages werden visuell zurueckgegeben
- **AutomateC.exe** — Command-Line-Utility, Messages ueber stdout/stderr

Returncodes: 0 = Erfolg, non-zero = Fehler.

Pfad: `C:\Program Files\Blue Prism Limited\Blue Prism Automate\AutomateC.exe`

---

## 2. Authentication & Connection

| Switch | Syntax | Beschreibung |
|---|---|---|
| `/user` | `<name> <pwd>` | BP-Login-Credentials (Windows- oder native Auth) |
| `/sso` | — | Single-Sign-On via aktuellen Windows-User |
| `/dbconname` | `<name>` | Name der DB-Connection (sonst Default-Connection) |
| `/resource` | `<target>` | Resource-PC-Ziel |
| `/port` | `<portnumber>` | Port fuer Resource-PC |

**Empirischer Befund (bpgit 2026-08-11):**
`AutomateC.exe /user admin Qwertzui123## /listprocesses` funktioniert **ohne** `/dbconname` — AutomateC nimmt eine Default-Connection, wenn keine explizite angegeben wird. Fruehere Annahme "CLI blockiert ohne /dbconname" war falsch. Credentials reichen.

**Wichtig:** Bei `/createdb`, `/replacedb`, `/upgradedb` mit Windows-Auth wird Password ignoriert, aber ein Dummy-Wert muss uebergeben werden.

---

## 3. Database Operations

| Switch | Syntax | Beschreibung |
|---|---|---|
| `/createdb` | `<password>` | DB neu erstellen (drop + recreate) |
| `/replacedb` | `<password>` | Tabellen leeren + neu aufbauen |
| `/upgradedb` | `<password>` | Bestehende DB upgraden |
| `/getdbscript` | `> [filename].sql` | SQL-Script fuer DB-Erstellung ausgeben (stdout) |
| `/maxdbver` | — | Max DB-Version beim Erstellen/Upgraden |
| `/setbpserver` | `<host> <port>` | Neue Blue-Prism-Server-Connection erstellen |
| `/ag` | `<host> <port>` | Mit `/setdbserver` fuer Availability-Group-Connection |
| `/agport` | `<host> <port>` | Port fuer AG-Listener (default 1433) |
| `/showdbconfig` | — | DB-Config-Form zeigen, dann exit |

---

## 4. License Management

| Switch | Syntax | Beschreibung |
|---|---|---|
| `/license` | `<licensefile>` | License-Key hinzufuegen |
| `/removelicense` | `<licensefile>` | License-Key entfernen |

Brauchen User-Credentials (`/user` oder `/sso`).

---

## 5. Process Management (Kern fuer bpgit)

| Switch | Syntax | Beschreibung |
|---|---|---|
| `/listprocesses` | — | **Alle Processes auflisten** (verifiziert: 31 Rows) |
| `/import` | `<filespec> [/forceid {new\|<guid>}] [/overwrite]` | Einzelnes Process-Object importieren |
| `/export` | `<processname>` | Einzelnes Process als XML exportieren |
| `/publish` | `<processname>` | Process veroeffentlichen (fuer Scheduler/Trigger) |
| `/unpublish` | `<processname>` | Veroeffentlichung rueckgaengig |
| `/publishws` | `<processname> <servicename> [/forcedoclitencoding] [/useGlobalNamespace]` | Als Web-Service veroeffentlichen |
| `/unpublishws` | `<processname>` | Web-Service-Veroeffentlichung rueckgaengig |
| `/refreshdependencies` | `{force}` | BPAProcess*Dependency-Tabellen neu aufbauen |
| `/genivbowrapper` | `<name>` | Wrapper-VBO fuer Internal Business Object erzeugen |

**Empirischer Befund (bpgit Phase 2a):**
- `/import` ist NICHT fuer bpgit-Workflow verwendet — Martin-Direktive "KEIN automateC.exe-Round-Trip". Stattdessen direkter SqlCommand in CommitCommand.cs.
- `/listprocesses` ist Read-Only-Aequivalent zum BP-Studio-Process-Browser.

---

## 6. Release Management (Kern fuer History-Mechanismus)

| Switch | Syntax | Beschreibung |
|---|---|---|
| `/importrelease` | `<filespec>` | `.bprelease`-File (oder `.bpskill` seit 7.x) importieren — **erzeugt neue BPARelease-Row + BPAReleaseEntry-Rows** |
| `/exportpackage` | `<packagename> [/release <releasename>]` | Package oder Release als XML exportieren |

**Empirischer Befund (bpgit 2026-08-11):**
`AutomateC.exe /user admin Qwertzui123## /importrelease All-copy.bprelease` mit einer Kopie von `All.bprelease` (2.8 MB) erzeugt:
- 1 neue `BPARelease`-Row (compressedxml IMAGE)
- 35 neue `BPAReleaseEntry`-Rows mit gleichem `entityid` aber neuem `releaseid`

**Folgerung:** `/importrelease` ist der Mechanismus fuer "echte Process-History" via Release-Snapshots. Jede entityid (processid) erscheint danach in mehreren Releases = Versionen.

**7.2 Doc-Hinweis:** `/importrelease` akzeptiert laut `documentation.blueprism.com/bp-7-2/en-us/helpCommandLine.htm` Blue-Prism-Skill-Files (`.bpskill`). Lokales `/help` fuer 7.5.1 erwaehnt das nicht explizit, aber gleicher Mechanismus.

---

## 7. Queue Management

| Switch | Syntax | Beschreibung |
|---|---|---|
| `/createqueue` | `<keyfield> <running> <maxattempts>` | Work-Queue erstellen |
| `/exportqueue` | `<filespec> [/queuefilter <filtername>] [/clearexported]` | Queue exportieren (optional loeschen) |
| `/deletequeue` | — | Queue loeschen |
| `/queueclearworked` | `/queuename <name> [/age <age>]` | Worked/Exception-Items loeschen (optional mit Alters-Filter) |

---

## 8. Schedule Management

| Switch | Syntax | Beschreibung |
|---|---|---|
| `/startschedule` | `[/schedule <name>\|...]` | Schedule(s) starten |
| `/deleteschedule` | `[/schedule <name>\|...]` | Schedule(s) loeschen |
| `/viewschedtimetable` | `{<name> \| <no-of-days> <date>} [/schedule <name>\|...] [/format {csv\|txt}]` | Timetable anzeigen |
| `/viewschedreport` | `{<name> \| <no-of-days> <date>} [/schedule <name>\|...] [/format {csv\|txt}]` | Schedule-Report anzeigen |

---

## 9. Environment Variables & Archive

| Switch | Syntax | Beschreibung |
|---|---|---|
| `/setev` | `<name> <datatype> <value> <description>` | Environment-Variable setzen |
| `/deleteev` | `<name>` | Environment-Variable loeschen |
| `/archive` | `[/from yyyyMMdd] [/to yyyyMMdd] [/age <value>] [/process <name>] [/delete]` | Logs archivieren |
| `/restorearchive` | — | Archivierte Logs wiederherstellen |
| `/setarchivepath` | `<path>` | Pfad fuer Archive setzen |

---

## 10. Runtime Control

| Switch | Syntax | Beschreibung |
|---|---|---|
| `/run` | `<processname> [/startp <xml>]` | Process starten |
| `/status` | `<sessionid>` | Session-Status |
| `/getlog` | `<sessionid>` | Log einer Session holen |
| `/requeststop` | `<sessionid\|sessionnumber>` | Session stoppen |
| `/getauthtoken` | `[/process <processname>]` | Auth-Token holen |
| `/getbod` | `<objectname>` | Business-Object-Definition holen |
| `/getauditlog` | — | Audit-Log aus DB holen |
| `/resourcestatus` | `<resourcename> <limit> <type>` | Resource-Sessions listen (m/h/d/mm) |
| `/wslog` | `on\|off` | Web-Service-Logging toggeln |

---

## 11. Resource Pools

| Switch | Syntax | Beschreibung |
|---|---|---|
| `/poolcreate` | `/pool <name>` | Pool erstellen |
| `/pooldelete` | `/pool <name>` | Pool loeschen |
| `/pooladd` | `/pool <name> [/resource <name>]` | Resource zum Pool hinzufuegen |
| `/poolremove` | `[/resource <name>]` | Resource aus Pool entfernen |

---

## 12. Server & Authentication-Server-Config

| Switch | Syntax | Beschreibung |
|---|---|---|
| `/serverconfig` | `<name> <connection> <port>` | BP-App-Server-Config erstellen/updaten |
| `/connectionmode` | `<value>` | 0..5 (WCF-/Remoting-Modi) |
| `/encryptionscheme` | `<name> [<method>]` | Encryption-Scheme (1=Triple-DES, 2=AES-Rijndael, 3=AES-CryptoService) |
| `/ordered` | `<value>` | Ordered Sessions (default true) |
| `/setallowanonresources` | `<value>` | Anonymous-Resources erlauben |
| `/serviceaccount` | `<clientid> <clientsecret>` | Service-Account fuer Auth-Server |
| `/resetdefaultadminpassword` | `<new password>` | Default-Admin-Password zuruecksetzen |

**Authentication-Server:**
| Switch | Syntax | Beschreibung |
|---|---|---|
| `/setactivedirectoryauth` | `<flag>` | AD-Auth toggeln |
| `/setactivedirectorygroupbasedroles` | `<flag>` | AD-Group→Role-Mapping toggeln |
| `/mapactivedirectorygrouptorole` | `<role> <group SID> <group DN>` | Mapping erstellen |
| `/mapauthenticationserverusers` | `<inputcsv> <outputcsv>` | User-Mapping zwischen BP und Auth-Server |
| `/getblueprismtemplateforusermapping` | `<outputcsv>` | BP-Native-User-Template erzeugen |
| `/getauthenticationservertemplateforusermapping` | `<outputcsv>` | Auth-Server-User-Template erzeugen |
| `/setkerberosrealm` | `<kerberosrealm>` | Kerberos-Realm fuer SPN-Config |
| `/forcentlm` | `<flag>` | Force-NTLM-Flag |

---

## 13. Credential Management

| Switch | Syntax | Beschreibung |
|---|---|---|
| `/createcredential` | `<credname> <username> <password> [/description <string>] [/expirydate <date>] [/invalid <flag>] [/credentialtype <string>]` | Credential erstellen |
| `/updatecredential` | `<credname> [/username] [/password] [/description] [/expirydate] [/invalid] [/credentialtype]` | Credential updaten |
| `/setcredentialproperty` | `<credname> <propertyname> <propertyvalue>` | Credential-Property erstellen/updaten |

Credential-Typen: General, BasicAuthentication, OAuth2ClientCredentials, OAuth2JwtBearerToken, BearerToken, DataGatewayCredentials.

---

## 14. Reports & Diagnostics

| Switch | Syntax | Beschreibung |
|---|---|---|
| `/report` | `<filespec>` | Report ausgeben |
| `/rolereport` | `<filespec>` | Rollen-Berechtigungs-Report |
| `/elementusage` | `<filespec>` | Element-Usage-Report |
| `/fontimport` | `<filespec>` | Font importieren |

---

## 15. Encryption & Re-Encrypt

| Switch | Syntax | Beschreibung |
|---|---|---|
| `/setencrypt` | `<encrypter-name>` | Encryption-Provider setzen |
| `/resetencrypt` | — | Encryption-Provider zuruecksetzen |
| `/reencryptdata` | `[/batchsize <size>] [/maxbatches <number>]` | Daten re-encrypten |
| `/configencrypt` | `[default \| thumbprint] [/forceconfigencrypt]` | Config-Encryption-Methode setzen |

---

## 16. 7.5.1-spezifische Befunde (vs 6.10 Docs)

1. **Default-Connection ohne `/dbconname`**: in 7.5.1 funktioniert `AutomateC.exe /user admin <pwd> /listprocesses` ohne explizite Connection-Angabe. Bei 6.10 war das Verhalten ggf. anders (nicht verifiziert).
2. **`.bpskill`-Support fuer `/importrelease`**: in 7.2 Docs erwaehnt, lokales 7.5.1 /help bestaetigt `/importrelease` ohne Dateityp-Einschraenkung — vermutlich gleicher Mechanismus.
3. **`/getauditlog`**: dedizierter Switch fuer Audit-Log (verwendet `BPAAuditEvents` DB-Tabelle).
4. **Connection-Mode-Set `0..5`** ist unveraendert, aber `WCF-Insecure` (Modus 5) ist neuere Option.

---

## 17. Relevanz fuer bpgit-Projekt

| Use-Case | Empfohlener Switch | Alternative |
|---|---|---|
| Processes enumerieren | `/listprocesses` | direkter SQL via `BPAProcess` |
| Process-History (Releases) | `/importrelease` + `BPAReleaseEntry` | direkter SQL auf `BPAReleaseEntry` |
| Per-Edit-History | `/getauditlog` | direkter SQL auf `BPAAuditEvents` (oldXML/newXML) |
| Process-Import (Test) | `/import <filespec>` — NICHT fuer bpgit-Workflow | direkter SqlCommand in `CommitCommand.cs` (Phase 2a) |
| Process-Export | `/export <processname>` | direkter SQL via `BPAProcess.processxml` |

**Wichtig fuer bpgit-Design:**
- `bpgit pull` nutzt `BPAProcess` direkt (nicht CLI)
- `bpgit commit` nutzt SqlCommand direkt (nicht CLI) — Martin-Direktive "KEIN automateC.exe-Round-Trip"
- `bpgit log` liest `BPAAuditEvents` (Per-Edit-Log) oder `BPAReleaseEntry` (Release-Manifest) — **nicht** `BPAProcessBackup` (nur Autosave)

---

## 18. Empirische Tests (2026-08-11, alle gruen)

| Test | Kommando | Ergebnis |
|---|---|---|
| Login + ListProcesses | `AutomateC.exe /user admin Qwertzui123## /listprocesses` | 31 Processes gelistet |
| Release-Import | `AutomateC.exe /user admin Qwertzui123## /importrelease All-copy.bprelease` | "Release 'All [2]' imported from file ..." (85% progress bei 35 Processes) |
| BPARelease-Vor-Test | `SELECT COUNT(*) FROM BPARelease` | 1 |
| BPARelease-Nach-Test | `SELECT COUNT(*) FROM BPARelease` | **2** |
| BPAReleaseEntry-Vor-Test | `SELECT COUNT(*) FROM BPAReleaseEntry` | 35 |
| BPAReleaseEntry-Nach-Test | `SELECT COUNT(*) FROM BPAReleaseEntry` | **70** |
| entityid-in-mehreren-Releases | `SELECT entityid, COUNT(DISTINCT releaseid) FROM BPAReleaseEntry GROUP BY entityid` | Alle entityids haben COUNT=2 |

---

## 19. Quellen

- Lokal: `C:\Program Files\Blue Prism Limited\Blue Prism Automate\AutomateC.exe` (Version 7.5.1.18099)
- `/help`-Output: `context/automatec-help-7.5.1.txt` (200 Zeilen, vollstaendiger Switch-Katalog)
- Online: `https://documentation.blueprism.com/bp-7-5/en-us/helpCommandLine.htm` (bp-7-5-1 = 404)
- 7.2-Doc (zusaetzlich): `https://documentation.blueprism.com/bp-7-2/en-us/helpCommandLine.htm`
- BP-Community: `https://community.blueprism.com/`

---

## 20. Aenderungshistorie

- 2026-08-11: Erstellt (Phase 2b-Folge, Martin-Direktive "Lese CLI-Doku 7.5.1, schreibe in context")
- Quellen: lokales `/help` 7.5.1.18099 + bp-7-5/bp-7-2 Online-Docs
- Empirische Tests: alle aus 2026-08-11 (Login, Release-Import)
