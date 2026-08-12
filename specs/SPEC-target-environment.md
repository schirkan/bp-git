# SPEC — Zielumgebung für den BP-Git-Adapter

Stand: 2026-08-12 (Martin #6401 verifiziert nach xunit-Tests-Welle: BP-Demo-LocalDB-SSO funktional, .NET 10 LibGit2Sharp 0.32.0 + Issue-#802 workaround stabil, 12 Test-Commits, 65 gruen + 4 skipped)  
Status: draft v2 (verifiziert 2026-08-12 - BP-LocalDB-Test funktional mit SSO, .NET 10 LibGit2Sharp 0.32.0 Issue-#802 workaround build-stabil)

## Host-Plattform

- **OS:** Microsoft Windows 11 (24H2 oder neuer)
- **Architektur:** x64 (OpenClaw-Integration ist x64-erprobt); ARM64 nicht erprobt
- **PowerShell:** 5.1 (Windows-Default) als Bridge für Tools ohne .NET-Bindung — Inline-Quoting problematisch (Lessons 2026-07-14: `&&` ungültig, `$…` muss in single-quotes); für nicht-triviale Scripts `temp\*.ps1` mit `powershell -NoProfile -ExecutionPolicy Bypass -File ...`

## Build-Toolchain

- **Git for Windows:** aktueller Stable (2.47+)
  - Empfehlungen pro Repo: `core.autocrlf=false` (LF bleibt LF), `core.longpaths=true` (für lange BP-Pfade), ggf. `credential.helper=manager` für HTTPS-Tokens
- **Visual Studio Code:** aktueller Stable mit Extensions:
  - C# Dev Kit (Microsoft)
  - PowerShell (Microsoft)
  - GitLens / Git Graph
  - Even Better TOML (für `config.toml`-Highlighting)
- **Visual Studio 2022 oder neuer** (optional, für Roslyn-Tools, Performance-Profiler, ML-Enabled-Diagnose)

## .NET / C#

- **.NET SDK:** .NET 10.x (Stable)
- **Sprache:** C# 13 mit `<LangVersion>latest</LangVersion>`
- **Nullable Reference Types:** an (C# Default seit 11)
- **ImplicitUsings:** an
- **Top-Level Statements / File-Scoped Namespaces:** an

## NuGet-Pakete (initial, MVP)

### Runtime
- `Microsoft.Data.SqlClient` (5.x oder aktuell) — SQL-Server-Native-Provider
- `Dapper` (Micro-ORM für BPA*-Tabellenmapping)
- `System.CommandLine` (Microsoft, CLI-Subcommands analog `git`)
- `Serilog` + `Serilog.Sinks.Console` (strukturiertes Logging)
- `System.IO.Abstractions` (für testbaren Dateisystem-Zugriff)

### Tests
- `xUnit` + `xUnit.runner.visualstudio`
- `FluentAssertions`
- `Verify` (Approval-Tests für XML-Snapshots: `bp-process.HelloWorld.bpprocess.xml.verified.txt`)
- `Verify.DiffPlex` (für saubere Unified-Diffs)

## Sicherheits-Baseline

- **Credentials:** niemals im Code oder Git; via `dotnet user-secrets` oder Windows Credential Manager (DPAPI)
- **Connection-Strings:** in `config.toml` als Template ohne Secrets, eingespielt via User-Secrets oder DPAPI
- **Lizenz-Dateien:** ausserhalb des bp-git-Repos (auf Desktop oder in `temp/`); nie committed (`.gitignore: *.lic`)
- **TLS:** alle externen Aufrufe (HTTPS only)
- **Package-Vulnerabilities:** `dotnet list package --vulnerable --include-transitive` in CI

## CI-Skelett (Empfehlung)

- **GitHub Actions** mit `actions/setup-dotnet@v4` (channel `10.x`)
- **Versions-Bumping:** `dotnet-version` GitHub Action (SemVer)
- **Build-Matrix:** `net10.0` primär (LTS-Fallback `net8.0` für heterogene Teams)
- **Cache:** `actions/cache@v4` mit Key `nuxtget-${{ hashFiles('**/packages.lock.json') }}`

## Versions-Verwaltung

- `git tag` mit SemVer (`v0.1.0`, `v0.2.0`, `v1.0.0`)
- **Conventional Commits** für Commit-Messages
- CHANGELOG.md automatisch generiert (optional, später)
- **Pre-1.0:** Breaking Changes bei jedem Minor-Bump (`v0.x.0`)
