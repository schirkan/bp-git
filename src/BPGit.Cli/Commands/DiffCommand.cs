using BPGit.Cli.Worktree;
using System;
using System.Collections.Generic;
using System.IO;

namespace BPGit.Cli.Commands;

/// <summary>
/// Hash-based drift report: compares current worktree process.xml hashes against
/// the snapshot stored in .bpgit/snapshot.json. Use this as a "what would change"
/// preview before `bpgit commit`. For semantic per-stage diffs, see the
/// Phase-2b-follow-up `bpgit diff-xml &lt;processid&gt;`.
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

        var currentIds = new HashSet<string>();
        var modified = 0;
        var added = 0;
        var deleted = 0;

        foreach (var dir in Directory.GetDirectories(procDir))
        {
            var id = Path.GetFileName(dir);
            if (processIdFilter != null && !id.Equals(processIdFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            currentIds.Add(id);
            var file = Path.Combine(dir, "process.xml");
            if (!File.Exists(file)) continue;
            var currentHash = SnapshotStore.ComputeHash(File.ReadAllText(file));
            if (snapshot.Processes.TryGetValue(id, out var snap))
            {
                if (currentHash != snap.Hash)
                {
                    modified++;
                    Console.WriteLine($"  modified: processes/{id}  ({snap.Name})");
                    Console.WriteLine($"    snapshot: {snap.Hash[..16]}");
                    Console.WriteLine($"    current:  {currentHash[..16]}");
                }
            }
            else
            {
                added++;
                Console.WriteLine($"  added (in worktree, not in snapshot): {id}");
            }
        }
        foreach (var id in snapshot.Processes.Keys)
        {
            if (processIdFilter != null && !id.Equals(processIdFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!currentIds.Contains(id))
            {
                deleted++;
                Console.WriteLine($"  deleted in DB (still in snapshot): {id} ({snapshot.Processes[id].Name})");
            }
        }

        Console.WriteLine();
        if (processIdFilter != null)
            Console.WriteLine($"{processIdFilter}: {modified} modified, {added} added, {deleted} deleted");
        else
            Console.WriteLine($"{modified} modified, {added} added, {deleted} deleted");
    }
}
