using BPGit.Cli.Config;
using BPGit.Cli.Worktree;
using BPGit.Data;
using BPGit.Data.Connection;
using BPGit.Data.Repositories;
using BPGit.Format;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace BPGit.Cli.Commands;

public static class CommitCommand
{
    public static async Task<int> RunAsync(string workdir, bool force = false)
    {
        var configPath = Path.Combine(workdir, ".bpgit", "config.toml");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine("bpgit not initialized. Run 'bpgit init' first.");
            return 1;
        }

        var cfg = AppConfig.Load(configPath);
        var factory = new ConnectionFactory(cfg.GetEffectiveConnectionString());
        var repo = new BPGit.Data.Repositories.ProcessRepository(factory);
        var xml = new ProcessXmlSerializer();

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

        int committed = 0;
        int skipped = 0;
        int errors = 0;

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

            // BPAProcessLock-Check
            var lockOwner = await repo.GetLockOwnerAsync(processId);
            if (lockOwner.HasValue && lockOwner.Value != Guid.Empty)
            {
                Console.Error.WriteLine($"  locked: {id} (by {lockOwner.Value})");
                skipped++;
                continue;
            }

            // Parse meta.json
            MetaInfo? meta;
            try
            {
                meta = JsonSerializer.Deserialize<MetaInfo>(File.ReadAllText(metaFile),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  meta.json parse error for {id}: {ex.Message}");
                errors++;
                continue;
            }
            if (meta == null)
            {
                errors++;
                continue;
            }

            // Validate XML
            var procXml = File.ReadAllText(procFile);
            if (!xml.IsValid(procXml))
            {
                Console.Error.WriteLine($"  invalid XML: {id}");
                errors++;
                continue;
            }

            // Load existing DB row to preserve FK-referenced columns (lastmodifiedby, AttributeID, createdby, runmode, etc.)
            var dbProcess = await repo.FindByIdAsync(processId);
            if (dbProcess == null)
            {
                Console.Error.WriteLine($"  process not found in DB: {id}");
                errors++;
                continue;
            }

            // Build Process: keep FK-referenced columns from DB, update XML + lastmodifieddate
            var process = new Process
            {
                processid = processId,
                ProcessType = string.IsNullOrEmpty(meta.type) ? "P" : meta.type,
                name = meta.name ?? "",
                description = meta.description,
                version = meta.version,
                AttributeID = dbProcess.AttributeID,
                processxml = procXml,
                runmode = dbProcess.runmode,
                sharedObject = dbProcess.sharedObject,
                forceLiteralForm = dbProcess.forceLiteralForm,
                useLegacyNamespace = dbProcess.useLegacyNamespace,
                hasStartupParameters = dbProcess.hasStartupParameters,
                wspublishname = dbProcess.wspublishname,
                createdate = dbProcess.createdate,
                createdby = dbProcess.createdby,
                lastmodifieddate = DateTime.UtcNow,
                lastmodifiedby = dbProcess.lastmodifiedby
            };

            try
            {
                await repo.UpdateAsync(process);
                snapshot.Processes[id] = new SnapshotEntry
                {
                    Hash = currentHash,
                    Name = meta.name ?? "",
                    Type = meta.type ?? ""
                };
                committed++;
                Console.WriteLine($"  committed: {id} ({meta.name})");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  error committing {id}: {ex.Message}");
                errors++;
            }
        }

        SnapshotStore.Save(workdir, snapshot);
        Console.WriteLine($"\n{committed} committed, {skipped} skipped, {errors} errors");
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
