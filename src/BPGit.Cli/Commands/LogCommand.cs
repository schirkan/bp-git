using BPGit.Cli.Config;
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
        DateTime? since,
        string? sCode)
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

        var rows = await repo.GetAuditHistoryAsync(limit, processId, since, sCode);

        if (rows.Count == 0)
        {
            Console.WriteLine("No audit history matches the filter.");
            return;
        }

        var filterDesc = BuildFilterDescription(processId, since, sCode);
        Console.WriteLine($"Showing {rows.Count} audit event(s){filterDesc}");
        Console.WriteLine();
        Console.WriteLine($"  {"WHEN",-19}  {"EVENT",-6}  {"DESCRIPTION",-60}  {"PROCESS",-36}  AUTHOR");
        Console.WriteLine($"  {new string('-', 19)}  {new string('-', 6)}  {new string('-', 60)}  {new string('-', 36)}  {new string('-', 20)}");
        foreach (var r in rows)
        {
            var ts = r.EventDateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var code = r.SCode ?? "(no code)";
            var narrative = Truncate(r.SNarrative ?? "", 60);
            var proc = r.TgtProcId.HasValue
                ? $"{r.TgtProcName ?? "(deleted)"}  {Truncate(r.TgtProcId.Value.ToString(), 8)}"
                : "(n/a)";
            proc = Truncate(proc, 36);
            var author = r.Username ?? Truncate(r.SrcUserId.ToString()[..8], 8);
            author = Truncate(author, 20);
            Console.WriteLine($"  {ts,-19}  {code,-6}  {narrative,-60}  {proc,-36}  {author}");
        }
        Console.WriteLine();
        Console.WriteLine("Tip: 'bpgit log --event P006' shows only process imports. 'bpgit log --processid <guid>' filters to one process.");
    }

    private static string BuildFilterDescription(Guid? processId, DateTime? since, string? sCode)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (processId.HasValue) parts.Add($"processid={processId.Value}");
        if (since.HasValue) parts.Add($"since={since.Value:yyyy-MM-dd}");
        if (!string.IsNullOrEmpty(sCode)) parts.Add($"event={sCode}");
        return parts.Count == 0 ? "" : " (" + string.Join(", ", parts) + ")";
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..(max - 1)] + "\u2026");
}
