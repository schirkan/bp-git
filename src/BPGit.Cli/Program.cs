using System;
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
                case "--help":
                case "-h":
                case "/?":
                    PrintHelp();
                    return 0;
                default:
                    if (!args[i].StartsWith("-") && command == null)
                    {
                        command = args[i];
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
        Console.WriteLine("bpgit - Git-konformer Adapter fuer Blue Prism (Phase 1 Read-Only)");
        Console.WriteLine();
        Console.WriteLine("Usage: bpgit [options] <command>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -o, --output <dir>     Worktree output directory (default: current dir)");
        Console.WriteLine("      --install-hooks    Install git hooks for drift detection (init only)");
        Console.WriteLine("  -h, --help             Show this help message");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  init                   Initialize bp-git worktree (.bpgit/config.toml)");
        Console.WriteLine("  pull                   Export BP processes from DB to worktree");
        Console.WriteLine("  status                 Show diff between worktree and snapshot");
    }
}
