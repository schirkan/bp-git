using BPGit.Cli.Config;
using BPGit.Cli.Services;
using BPGit.Cli.Worktree;
using BPGit.Data.Connection;
using BPGit.Data.Repositories;
using BPGit.Format;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BPGit.Cli.Commands;

/// <summary>
/// bpgit commit --force
/// Schreibt Worktree-XMLs zurueck in BP-DB via AutomateC.exe /import
/// (statt direkt SqlCommand). Dadurch schreibt BP's Runtime automatisch
/// korrekte Audit-Eintraege in BPAAuditEvents (newXML).
/// Lock-Check bleibt SqlCommand-basiert (Read-Only OK).
/// Process-Delete via CLI nicht unterstuetzt — nur Warnung.
/// </summary>
public static class CommitCommand
{
    // BP's /import-Parser ist strikt: Leading XML-Comments brechen den Parser
    // ("Failed to create... already exists"), obwohl /overwrite gesetzt ist.
    // Wir strippen sie vor dem Temp-Write, behalten die Original-XML im Worktree.
    // Pattern: optionaler Whitespace, dann 1+ Leading-Comments (jeweils gefolgt von Whitespace).
    private static readonly Regex LeadingXmlCommentsRegex =
        new(@"^\s*(?:<!--[\s\S]*?-->\s*)+", RegexOptions.Compiled);

    private static string StripLeadingXmlComments(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return xml;
        return LeadingXmlCommentsRegex.Replace(xml, string.Empty);
    }

    public static async Task<int> RunAsync(string workdir, bool force = false)
    {
        var configPath = Path.Combine(workdir, ".bpgit", "config.toml");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine("bpgit not initialized. Run 'bpgit init' first.");
            return 1;
        }

        var cfg = AppConfig.Load(configPath);
        var snapshot = SnapshotStore.Load(workdir);
        if (snapshot == null)
        {
            Console.Error.WriteLine("No snapshot. Run 'bpgit pull' first.");
            return 1;
        }

        var procDir = Path.Combine(workdir, "processes");
        if (!Directory.Exists(procDir))
        {
            Console.Error.WriteLine($"No processes directory at {procDir}");
            return 1;
        }

        if (!force)
        {
            Console.Error.WriteLine("bpgit commit requires --force (explicit write). Re-run with --force when ready.");
            return 1;
        }

        // Connection factory for read-only operations (Lock-Check)
        var factory = new ConnectionFactory(cfg.GetEffectiveConnectionString());
        var repo = new ProcessRepository(factory);
        var xml = new ProcessXmlSerializer();

        int committed = 0;
        int skipped = 0;
        int errors = 0;
        var tmpFiles = new List<string>();

        try
        {
            foreach (var dir in Directory.GetDirectories(procDir))
            {
                var id = Path.GetFileName(dir);
                if (!Guid.TryParse(id, out var processId))
                {
                    skipped++;
                    continue;
                }

                var procFile = Path.Combine(dir, "process.xml");
                var metaFile = Path.Combine(dir, "meta.json");

                if (!File.Exists(procFile) || !File.Exists(metaFile))
                {
                    skipped++;
                    continue;
                }

                // Idempotenz: Hash-Vergleich Worktree-XML gegen Snapshot-Hash
                var currentHash = SnapshotStore.ComputeHash(File.ReadAllText(procFile));
                if (snapshot.Processes.TryGetValue(id, out var snap) && snap.Hash == currentHash)
                {
                    skipped++;
                    continue;
                }

                // BPAProcessLock-Check (Read-Only SqlCommand OK)
                var lockOwner = await repo.GetLockOwnerAsync(processId);
                if (lockOwner.HasValue && lockOwner.Value != Guid.Empty)
                {
                    Console.Error.WriteLine($"  locked: {id} (by {lockOwner.Value})");
                    skipped++;
                    continue;
                }

                // XML-Validierung
                var procXml = File.ReadAllText(procFile);
                if (!xml.IsValid(procXml))
                {
                    Console.Error.WriteLine($"  invalid XML: {id}");
                    errors++;
                    continue;
                }

                // Leading-XML-Comments strippen (BP's /import ist strikt, siehe oben)
                var importXml = StripLeadingXmlComments(procXml);

                // Temp-File fuer AutomateC.exe /import
                var tmpFile = Path.Combine(Path.GetTempPath(), $"bpgit-import-{id}.xml");
                await File.WriteAllTextAsync(tmpFile, importXml);
                tmpFiles.Add(tmpFile);

                // CLI-Args: /import + /overwrite (reicht fuer existierende Processes/Objects
                // bei sauberer XML — empirisch verifiziert 2026-08-11 mit canonical export).
                var args = new List<string> { "/import", tmpFile, "/overwrite" };

                AutomateCRunner.RunResult result;
                try
                {
                    result = AutomateCRunner.Run(cfg, args.ToArray());
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  CLI failed for {id}: {ex.Message}");
                    errors++;
                    continue;
                }

                if (result.ExitCode != 0)
                {
                    Console.Error.WriteLine($"  AutomateC exit {result.ExitCode} for {id}");
                    if (!string.IsNullOrWhiteSpace(result.StdErr))
                        Console.Error.WriteLine($"    stderr: {result.StdErr.Trim()}");
                    if (!string.IsNullOrWhiteSpace(result.StdOut))
                        Console.Error.WriteLine($"    stdout: {result.StdOut.Trim()}");
                    errors++;
                    continue;
                }

                // Snapshot-Update nach erfolgreichem Commit
                MetaInfo? meta = null;
                try
                {
                    meta = JsonSerializer.Deserialize<MetaInfo>(File.ReadAllText(metaFile),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch { /* meta is optional for snapshot */ }

                snapshot.Processes[id] = new SnapshotEntry
                {
                    Hash = currentHash,
                    Name = meta?.name ?? "",
                    Type = meta?.type ?? ""
                };
                committed++;
                Console.WriteLine($"  committed: {id} (via AutomateC.exe /import)");
            }

            // Detect deletions (snapshot entry but no worktree dir)
            foreach (var snapId in new List<string>(snapshot.Processes.Keys))
            {
                if (!Directory.Exists(Path.Combine(procDir, snapId)))
                {
                    Console.Error.WriteLine($"  deletion detected: {snapId} ({snapshot.Processes[snapId].Name})");
                    Console.Error.WriteLine($"    AutomateC.exe hat kein Process-Delete. Manuell in BP Studio loeschen oder --allow-delete (SQL-Direct, Bypasst Audit-Log) — noch nicht implementiert.");
                }
            }
        }
        finally
        {
            // Cleanup temp files
            foreach (var f in tmpFiles)
            {
                try { File.Delete(f); } catch { /* best effort */ }
            }
        }

        SnapshotStore.Save(workdir, snapshot);
        Console.WriteLine($"\n{committed} committed, {skipped} skipped, {errors} errors");
        Console.WriteLine("Tip: query 'SELECT TOP 5 * FROM BPAAuditEvents ORDER BY eventid DESC' to verify audit entries.");
        return errors == 0 ? 0 : 1;
    }

    private class MetaInfo
    {
        public string? processid { get; set; }
        public string? name { get; set; }
        public string? type { get; set; }
        public string? description { get; set; }
        public string? version { get; set; }
        public int AttributeID { get; set; }
        public DateTime lastmodifieddate { get; set; }
    }
}
