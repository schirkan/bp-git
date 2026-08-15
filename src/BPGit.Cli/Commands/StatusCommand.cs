using BPGit.Cli.Worktree;
using BPGit.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BPGit.Cli.Commands;

/// <summary>
/// bpgit status — Show worktree-vs-snapshot drift (modified/added/deleted).
///
/// Folder-aware layout (per #6289): walks WorktreeDir/**/*.xml, resolves each file's
/// processid via snapshot.json (path -> processid), computes hash, compares to snapshot.
/// </summary>
public static class StatusCommand
{
    public static void Run(ServerConfig config)
    {
        var workdir = config.WorktreePath;
        var snapshot = SnapshotStore.Load(workdir, config.SnapshotFileName);
        if (snapshot == null)
        {
            Console.WriteLine($"No snapshot at {config.SnapshotPath}. Run 'bpgit pull' first.");
            return;
        }
        if (!Directory.Exists(workdir))
        {
            Console.WriteLine($"No worktree directory at {workdir}");
            return;
        }

        // Build path -> (processid, entry) reverse-map
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

        foreach (var file in Directory.EnumerateFiles(workdir, "*.xml", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(workdir, file).Replace('\\', '/');
            currentPaths.Add(relPath);
            var currentHash = SnapshotStore.ComputeHash(File.ReadAllText(file));

            if (entryByPath.TryGetValue(relPath, out var snap))
            {
                if (currentHash != snap.entry.Hash)
                {
                    modified++;
                    Console.WriteLine($"  modified: {relPath} ({snap.entry.Name})");
                }
            }
            else
            {
                added++;
                Console.WriteLine($"  added (in worktree, not in snapshot): {relPath}");
            }
        }

        foreach (var kv in entryByPath)
        {
            if (!currentPaths.Contains(kv.Key))
            {
                deleted++;
                Console.WriteLine($"  deleted in DB: {kv.Key} ({kv.Value.entry.Name})");
            }
        }

        Console.WriteLine($"\n{modified} modified, {added} added, {deleted} deleted");
    }
}
