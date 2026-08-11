using BPGit.Cli.Config;
using BPGit.Cli.Services;
using BPGit.Cli.Worktree;
using BPGit.Data.Connection;
using BPGit.Data.Repositories;
using BPGit.Format;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BPGit.Cli.Commands;

/// <summary>
/// bpgit commit --force — Write worktree XMLs back to BP-DB via AutomateC.exe /import.
///
/// Folder-aware layout (per #6289): walks processes/**/*.xml, resolves each file's
/// processid via snapshot.json (path → processid), strips leading XML comments
/// (per #6277 / BP's strict /import parser), and invokes AutomateC.exe
/// /import + /forceid &lt;guid&gt; + /overwrite.
///
/// Lock-check stays on SqlCommand (read-only is fine). Process-Delete via CLI is
/// not supported (BP-CLI limitation, per #6276) — only warning.
/// </summary>
public static class CommitCommand
{
    // BP's /import-Parser ist strikt: Leading XML-Comments brechen den Parser
    // ("Failed to create... already exists"), obwohl /overwrite gesetzt ist.
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

        // Build path → processid reverse-map from snapshot (forward-slash normalized)
        var pathToId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var pathToEntry = new Dictionary<string, SnapshotEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in snapshot.Processes)
        {
            if (!string.IsNullOrEmpty(kv.Value.Path))
            {
                var normalized = kv.Value.Path.Replace('\\', '/');
                pathToId[normalized] = Guid.Parse(kv.Key);
                pathToEntry[normalized] = kv.Value;
            }
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
            foreach (var file in Directory.EnumerateFiles(procDir, "*.xml", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(workdir, file).Replace('\\', '/');

                if (!pathToId.TryGetValue(relPath, out var processId))
                {
                    Console.WriteLine($"  skipped (not in snapshot): {relPath}");
                    skipped++;
                    continue;
                }

                // Idempotency: Hash-Vergleich Worktree-XML gegen Snapshot-Hash
                var currentHash = SnapshotStore.ComputeHash(File.ReadAllText(file));
                if (pathToEntry.TryGetValue(relPath, out var snap) && snap.Hash == currentHash)
                {
                    skipped++;
                    continue;
                }

                // BPAProcessLock-Check (Read-Only SqlCommand OK)
                var lockOwner = await repo.GetLockOwnerAsync(processId);
                if (lockOwner.HasValue && lockOwner.Value != Guid.Empty)
                {
                    Console.Error.WriteLine($"  locked: {relPath} ({processId})");
                    skipped++;
                    continue;
                }

                // XML-Validierung
                var procXml = File.ReadAllText(file);
                if (!xml.IsValid(procXml))
                {
                    Console.Error.WriteLine($"  invalid XML: {relPath}");
                    errors++;
                    continue;
                }

                // Leading-XML-Comments strippen
                var importXml = StripLeadingXmlComments(procXml);

                // Temp-File fuer AutomateC.exe /import
                var tmpFile = Path.Combine(Path.GetTempPath(), $"bpgit-import-{processId}.xml");
                await File.WriteAllTextAsync(tmpFile, importXml);
                tmpFiles.Add(tmpFile);

                // CLI-Args: /import + /forceid <guid> + /overwrite
                var args = new List<string> { "/import", tmpFile, "/forceid", processId.ToString(), "/overwrite" };

                AutomateCRunner.RunResult result;
                try
                {
                    result = AutomateCRunner.Run(cfg, args.ToArray());
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  CLI failed for {relPath}: {ex.Message}");
                    errors++;
                    continue;
                }

                if (result.ExitCode != 0)
                {
                    Console.Error.WriteLine($"  AutomateC exit {result.ExitCode} for {relPath}");
                    if (!string.IsNullOrWhiteSpace(result.StdErr))
                        Console.Error.WriteLine($"    stderr: {result.StdErr.Trim()}");
                    if (!string.IsNullOrWhiteSpace(result.StdOut))
                        Console.Error.WriteLine($"    stdout: {result.StdOut.Trim()}");
                    errors++;
                    continue;
                }

                // Snapshot-Update nach erfolgreichem Commit
                snapshot.Processes[processId.ToString()] = new SnapshotEntry
                {
                    Hash = currentHash,
                    Name = snap?.Name ?? "",
                    Type = snap?.Type ?? "",
                    Path = relPath
                };
                committed++;
                Console.WriteLine($"  committed: {relPath} ({processId})");
            }
        }
        finally
        {
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
}
