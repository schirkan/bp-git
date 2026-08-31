using BPGit.Data;
using System;
using System.IO;
using System.Threading.Tasks;

namespace BPGit.Cli.Commands;

/// <summary>
/// <c>bpgit init</c> - Bootstrap CLI worktree from BP-DB (no .bpgit/ directory;
/// the unified bpgit.json next to the executable is the single config).
///
/// Workstation-side git-hook installation was removed in 2026-08-30 per Spec §13.
/// The drift-warning hooks (post-checkout, post-merge) belonged to the pre-Server
/// architecture (Phase 2a, Martin #6295) where the worktree was its own source of
/// truth. In the Git-Server architecture (Phase 4+), the server is the source of
/// truth for BP-DB state - the worktree is a materialized view, refreshed via
/// <c>bpgit pull</c>. Hooks would be redundant. If a future need arises (e.g.
/// multi-user warning on stale worktrees), implement as a server-side
/// post-checkout handler, not a workstation shell hook.
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
