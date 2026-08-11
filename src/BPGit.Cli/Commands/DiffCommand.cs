using BPGit.Cli.Worktree;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BPGit.Cli.Commands;

/// <summary>
/// bpgit diff [&lt;processIdFilter&gt;] — Hash-based drift report.
/// Folder-aware: walks processes/**/*.xml, resolves each file's processid via
/// snapshot.json (path → processid), shows modified/added/deleted with hash diffs.
/// </summary>
public static class DiffCommand
{
    public static void Run(string workdir, string? processIdFilter)
    {
        var snapshot = SnapshotStore.Load(workdir);
        if (snapshot == null)
        {
            Console.WriteLine("No snapshot. Run 'bpgit pull' first.");
            return;
        }
        var procDir = Path.Combine(workdir, "processes");
        if (!Directory.Exists(procDir))
        {
            Console.WriteLine($"No processes directory at {procDir}");
            return;
        }

        // Build path → (processid, entry) reverse-map
        var entryByPath = new Dictionary<string, (string processId, SnapshotEntry entry)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in snapshot.Processes)
        {
            if (!string.IsNullOrEmpty(kv.Value.Path))
            {
                var normalized = kv.Value.Path.Replace('\\', '/');
                entryByPath[normalized] = (kv.Key, kv.Value);
            }
        }

        var modified = 0;
        var added = 0;
        var deleted = 0;
        var currentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(procDir, "*.xml", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(workdir, file).Replace('\\', '/');
            currentPaths.Add(relPath);

            if (!entryByPath.TryGetValue(relPath, out var snap))
            {
                added++;
                Console.WriteLine($"  added (in worktree, not in snapshot): {relPath}");
                continue;
            }

            if (processIdFilter != null && !snap.processId.Equals(processIdFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var currentHash = SnapshotStore.ComputeHash(File.ReadAllText(file));
            if (currentHash != snap.entry.Hash)
            {
                modified++;
                Console.WriteLine($"  modified: {relPath}  ({snap.entry.Name})");
                Console.WriteLine($"    snapshot: {snap.entry.Hash[..16]}");
                Console.WriteLine($"    current:  {currentHash[..16]}");
            }
        }

        foreach (var kv in entryByPath)
        {
            if (processIdFilter != null && !kv.Value.processId.Equals(processIdFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!currentPaths.Contains(kv.Key))
            {
                deleted++;
                Console.WriteLine($"  deleted in DB (still in snapshot): {kv.Key} ({kv.Value.entry.Name})");
            }
        }

        Console.WriteLine();
        if (processIdFilter != null)
            Console.WriteLine($"{processIdFilter}: {modified} modified, {added} added, {deleted} deleted");
        else
            Console.WriteLine($"{modified} modified, {added} added, {deleted} deleted");
    }
}
