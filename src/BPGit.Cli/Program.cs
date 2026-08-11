using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using BPGit.Cli.Commands;

namespace BPGit.Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 1;
        }

        // Parse args: first non-option is the command, options are global
        string? command = null;
        string? output = null;
        bool installHooks = false;
        bool force = false;
        int limit = 50;
        Guid? processId = null;
        DateTime? since = null;
        string? sCode = null;
        string? positionalArg = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--output":
                case "-o":
                    if (i + 1 < args.Length)
                    {
                        output = args[++i];
                    }
                    break;
                case "--install-hooks":
                    installHooks = true;
                    break;
                case "--force":
                    force = true;
                    break;
                case "--limit":
                case "-n":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var n) && n > 0)
                        limit = n;
                    break;
                case "--processid":
                    if (i + 1 < args.Length && Guid.TryParse(args[++i], out var g))
                        processId = g;
                    break;
                case "--since":
                    if (i + 1 < args.Length &&
                        DateTime.TryParse(args[++i], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var s))
                        since = s;
                    break;
                case "--event":
                    if (i + 1 < args.Length)
                        sCode = args[++i];
                    break;
                case "--help":
                case "-h":
                case "/?":
                    PrintHelp();
                    return 0;
                default:
                    if (!args[i].StartsWith("-"))
                    {
                        if (command == null) command = args[i];
                        else if (positionalArg == null) positionalArg = args[i];
                    }
                    break;
            }
        }

        output ??= Directory.GetCurrentDirectory();

        try
        {
            switch (command)
            {
                case "init":
                    await InitCommand.RunAsync(output, installHooks);
                    return 0;
                case "pull":
                    await PullCommand.RunAsync(output);
                    return 0;
                case "status":
                    StatusCommand.Run(output);
                    return 0;
                case "diff":
                    DiffCommand.Run(output, positionalArg);
                    return 0;
                case "log":
                    await LogCommand.RunAsync(output, limit, processId, since, sCode);
                    return 0;
                case "commit":
                    return await CommitCommand.RunAsync(output, force);
                case null:
                    Console.Error.WriteLine("No command specified. Use 'bpgit --help' for usage.");
                    return 1;
                default:
                    Console.Error.WriteLine($"Unknown command: {command}. Use 'bpgit --help' for usage.");
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("bpgit - Git-konformer Adapter fuer Blue Prism (Phase 1+2 Read/Write)");
        Console.WriteLine();
        Console.WriteLine("Usage: bpgit [options] <command>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -o, --output <dir>     Worktree output directory (default: current dir)");
        Console.WriteLine("      --install-hooks    Install git hooks for drift detection (init only)");
        Console.WriteLine("      --force            Required for `commit` (explicit write)");
        Console.WriteLine("  -n, --limit N          Limit rows for `log` (default 50)");
        Console.WriteLine("      --processid <guid> Filter by processid for `log`");
        Console.WriteLine("      --since YYYY-MM-DD Only entries with eventdatetime >= since for `log`");
        Console.WriteLine("      --event <sCode> Filter by event-type code (e.g. P006, L001) for `log`");
        Console.WriteLine("  -h, --help             Show this help message");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  init                   Initialize bp-git worktree (.bpgit/config.toml)");
        Console.WriteLine("  pull                   Export BP processes from DB to worktree");
        Console.WriteLine("  status                 Show diff between worktree and snapshot");
        Console.WriteLine("  diff [<processid>]     Hash-based drift report (worktree vs snapshot)");
        Console.WriteLine("  log                    Show BP per-edit audit history from BPAAuditEvents");
        Console.WriteLine("  commit                 Write worktree changes back to BP DB (requires --force)");
    }
}
