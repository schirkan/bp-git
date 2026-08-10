using BPGit.Cli.Config;
using BPGit.Cli.Worktree;
using BPGit.Data.Connection;
using BPGit.Data.Repositories;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace BPGit.Cli.Commands;

public static class PullCommand
{
    public static async Task RunAsync(string workdir)
    {
        var configPath = Path.Combine(workdir, ".bpgit", "config.toml");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine("bpgit not initialized. Run 'bpgit init' first.");
            return;
        }
        var cfg = AppConfig.Load(configPath);
        var factory = new ConnectionFactory(cfg.GetEffectiveConnectionString());
        var repo = new ProcessRepository(factory);

        var processes = await repo.ListAllAsync();
        Console.WriteLine($"Found {processes.Count} rows in BPAProcess");

        var procDir = Path.Combine(workdir, "processes");
        Directory.CreateDirectory(procDir);

        var snapshot = new Snapshot { ExtractedAt = DateTime.UtcNow };
        foreach (var p in processes)
        {
            var dir = Path.Combine(procDir, p.processid.ToString());
            Directory.CreateDirectory(dir);

            if (!string.IsNullOrEmpty(p.processxml))
            {
                var file = Path.Combine(dir, "process.xml");
                await File.WriteAllTextAsync(file, p.processxml);
            }

            var metaFile = Path.Combine(dir, "meta.json");
            var meta = JsonSerializer.Serialize(new
            {
                processid = p.processid,
                name = p.name,
                type = p.ProcessType,
                version = p.version,
                lastmodifieddate = p.lastmodifieddate
            }, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metaFile, meta);

            snapshot.Processes[p.processid.ToString()] = new SnapshotEntry
            {
                Hash = SnapshotStore.ComputeHash(p.processxml ?? ""),
                Name = p.name,
                Type = p.ProcessType
            };
        }
        SnapshotStore.Save(workdir, snapshot);
        Console.WriteLine($"Snapshot saved ({snapshot.Processes.Count} entries)");
    }
}
