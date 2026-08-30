using System;
using System.Globalization;
using System.Threading.Tasks;
using BPGit.Cli.Commands;
using BPGit.Data;

namespace BPGit.Cli;

/// <summary>
/// CLI entry point: parses commands and dispatches to <see cref="BPGit.Cli.Commands"/>.
/// The unified <see cref="ServerConfig"/> (loaded from <c>bpgit.json</c> next to the
/// executable) is the single source of truth — no <c>.bpgit/</c> directory is created.
/// </summary>
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
                case "--install-hooks":
                    // Deprecated since Spec §13 (Martin #6295). Pre-Server architecture
                    // had workstation-shell hooks for drift warnings; the Git-Server
                    // architecture uses WorktreeSyncService.MaterializeAsync on the
                    // server side instead. InitCommand ignores the flag with a warning
                    // so old scripts don't break.
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

        // Load unified config (default: <exe-dir>/bpgit.json)
        var config = ServerConfig.Load();

        try
        {
            switch (command)
            {
                case "init":
                    await InitCommand.RunAsync(config, installHooks);
                    return 0;
                case "pull":
                    await PullCommand.RunAsync(config);
                    return 0;
                case "status":
                    StatusCommand.Run(config);
                    return 0;
                case "diff":
                    DiffCommand.Run(config, positionalArg);
                    return 0;
                case "log":
                    await LogCommand.RunAsync(config, limit, processId, since, sCode);
                    return 0;
                case "commit":
                    return await CommitCommand.RunAsync(config, force);
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
        Console.WriteLine("bpgit - Git-konformer Adapter fuer Blue Prism (unified CLI + Server)");
        Console.WriteLine();
        Console.WriteLine("Usage: bpgit [options] <command>");
        Console.WriteLine("       bpgit --serve [config-path] | /s [config-path] | -s [config-path]");
        Console.WriteLine();
        Console.WriteLine("Server-Mode (--serve):");
        Console.WriteLine("  --serve              Start Kestrel self-hosted git-server (default config: <exe-dir>/bpgit.json)");
        Console.WriteLine("  --serve <config>     Start server with custom config");
        Console.WriteLine("  --serve init <repo>  Initialize bare git-repo for the BP project");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("      --install-hooks    DEPRECATED (Spec §13): no-op with warning, ignored");
        Console.WriteLine("      --force            Required for 'commit' (explicit write)");
        Console.WriteLine("  -n, --limit N          Limit rows for 'log' (default 50)");
        Console.WriteLine("      --processid <guid> Filter by processid for 'log'");
        Console.WriteLine("      --since YYYY-MM-DD Only entries with eventdatetime >= since for 'log'");
        Console.WriteLine("      --event <sCode>    Filter by event-type code (e.g. P006, L001) for 'log'");
        Console.WriteLine("  -h, --help             Show this help message");
        Console.WriteLine();
        Console.WriteLine("CLI commands:");
        Console.WriteLine("  init                   Initialize CLI worktree from BP-DB (uses bpgit.json, no .bpgit/ dir)");
        Console.WriteLine("  pull                   Re-pull BP processes into worktree");
        Console.WriteLine("  status                 Show diff between worktree and snapshot");
        Console.WriteLine("  diff [<processid>]     Hash-based drift report (worktree vs snapshot)");
        Console.WriteLine("  log                    Show BP per-edit audit history from BPAAuditEvents");
        Console.WriteLine("  commit                 Write worktree changes back to BP-DB (requires --force)");
    }
}
