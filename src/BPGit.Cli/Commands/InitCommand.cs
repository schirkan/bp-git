using BPGit.Data;
using System;
using System.IO;
using System.Threading.Tasks;

namespace BPGit.Cli.Commands;

/// <summary>
/// <c>bpgit init</c> - Bootstrap CLI worktree from BP-DB (no .bpgit/ directory;
/// the unified bpgit.json next to the executable is the single config).
///
/// **Stand 2026-09-10:** keine Hooks mehr — weder Server-seitig (alle Logik im
/// `bpgit-server` HTTP-Handler, keine Shell-Scripts in `<bare-repo>/hooks/`)
/// noch Client-seitig (keine `core.hooksPath`, kein `bpgit install-hooks`).
/// Filename = `sanitize(BPAProcess.name) + ".xml"` per #6311 — kein post-checkout
/// nötig. Worktree-Refresh gegen lokale BP-DB via `bpgit pull` (CLI-Subcommand).
/// </summary>
public static class InitCommand
{
    /// <summary>
    /// Bootstrap the CLI worktree from BP-DB into <c>config.WorktreePath</c>.
    /// </summary>
    public static async Task RunAsync(ServerConfig config)
    {
        var workdir = config.WorktreePath;
        Directory.CreateDirectory(workdir);

        Console.WriteLine($"bpgit init: workdir={workdir}");
        Console.WriteLine($"bpgit init: snapshot={config.SnapshotPath}");
        Console.WriteLine($"bpgit init: bp-server={config.SqlServer}, db={config.SqlDatabase}");
        Console.WriteLine($"bpgit init: connect-string={config.GetEffectiveConnectionString()}");

        // Auto-pull from BP-DB into worktree
        await PullCommand.RunAsync(config);

        Console.WriteLine("bpgit init complete");
    }
}
