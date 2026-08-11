using BPGit.Cli.Config;
using BPGit.Cli.Worktree;
using BPGit.Data.Connection;
using BPGit.Data.Repositories;
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace BPGit.Cli.Commands;

public static class LogCommand
{
    public static async Task RunAsync(
        string workdir,
        int limit,
        Guid? processId,
        DateTime? since)
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

        var rows = await repo.GetHistoryAsync(limit, processId, since);

        if (rows.Count == 0)
        {
            Console.WriteLine("No backup history. Backups are written by BP on every Save.");
            return;
        }

        Console.WriteLine($"Showing {rows.Count} backup(s)");
        Console.WriteLine();
        Console.WriteLine($"  {"BACKUPDATE",-19}  {"PROCESS",-40}  {"AUTHOR",-20}  ID");
        Console.WriteLine($"  {new string('-', 19)}  {new string('-', 40)}  {new string('-', 20)}  {new string('-', 36)}");
        foreach (var r in rows)
        {
            var ts = r.BackupDate?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "(null)";
            var name = Truncate(r.Name ?? "(no name)", 40);
            var author = r.Username ?? (r.UserId?.ToString()[..8] ?? "(unknown)");
            author = Truncate(author, 20);
            var idShort = r.ProcessId.ToString();
            Console.WriteLine($"  {ts,-19}  {name,-40}  {author,-20}  {idShort}");
        }
        Console.WriteLine();
        Console.WriteLine("Tip: 'bpgit diff --xml <processid>' to inspect a backup XML payload.");
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "\u2026";
}
