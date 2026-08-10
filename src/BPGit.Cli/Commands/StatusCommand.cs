using BPGit.Cli.Worktree;
using System;
using System.Collections.Generic;
using System.IO;

namespace BPGit.Cli.Commands;

public static class StatusCommand
{
    public static void Run(string workdir)
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

        var modified = 0;
        var added = 0;
        var deleted = 0;
        var currentIds = new HashSet<string>();

        foreach (var dir in Directory.GetDirectories(procDir))
        {
            var id = Path.GetFileName(dir);
            currentIds.Add(id);
            var file = Path.Combine(dir, "process.xml");
            if (!File.Exists(file)) continue;
            var currentHash = SnapshotStore.ComputeHash(File.ReadAllText(file));
            if (snapshot.Processes.TryGetValue(id, out var snap))
            {
                if (currentHash != snap.Hash)
                {
                    modified++;
                    Console.WriteLine($"  modified: processes/{id} ({snap.Name})");
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
            if (!currentIds.Contains(id))
            {
                deleted++;
                Console.WriteLine($"  deleted in DB: {id} ({snapshot.Processes[id].Name})");
            }
        }
        Console.WriteLine($"\n{modified} modified, {added} added, {deleted} deleted");
    }
}
