using System;
using System.Threading.Tasks;
using BPGit.Cli.Commands;
using BPGit.Data;

namespace BPGit.Cli;

/// <summary>
/// CLI entry point: Admin-Tool für Server-Setup. Nur <c>init</c> als Subcommand —
/// alle anderen Funktionen laufen über die Git-Smart-HTTP-API.
///
/// Stand 2026-09-11: CLI auf nur `init` reduziert. Server macht BP-DB-Sync
/// atomar mit jedem Push via <c>PreReceiveHandler</c>, daher kein Client-Tool nötig.
///
/// Die einheitliche <see cref="ServerConfig"/> (geladen aus <c>bpgit.json</c>) ist die
/// Single Source of Truth — kein <c>.bpgit/</c>-Verzeichnis.
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h" || args[0] == "/?")
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        var command = args[0];
        var config = ServerConfig.Load();

        try
        {
            return command switch
            {
                "init" => await RunInit(config),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunInit(ServerConfig config)
    {
        await InitCommand.RunAsync(config);
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}. Use 'bpgit --help' for usage.");
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("bpgit - Admin-Tool für bpgit-server (initialisiert Bare-Repo + Worktree)");
        Console.WriteLine();
        Console.WriteLine("Usage: bpgit <command>");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  init                   Initialisiert Server-Worktree (einmalig pro Repo, Admin-Task)");
        Console.WriteLine();
        Console.WriteLine("Server starten: separater Befehl 'bpgit-server.exe' (nicht via CLI)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --help             Show this help message");
    }
}
