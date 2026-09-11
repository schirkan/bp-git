using BPGit.Data;
using System;
using System.IO;
using System.Threading.Tasks;

namespace BPGit.Cli.Commands;

/// <summary>
/// <c>bpgit init</c> - Bootstrap Server-Worktree (einmalig pro Repo, Admin-Task).
///
/// Stand 2026-09-11: CLI auf nur <c>init</c> reduziert. Keine Hooks, kein Auto-Pull
/// — Worktree wird durch nachfolgende Pushes via Server-<c>PostReceiveHandler</c>
/// materialisiert. Server hält BP-DB als Single Source of Truth (atomar mit Push synchron).
/// </summary>
public static class InitCommand
{
    /// <summary>
    /// Bootstrap the server worktree directory at <c>config.WorktreePath</c>.
    /// Materialization from BP-DB happens automatically on the first push
    /// via <c>PostReceiveHandler</c> → <c>WorktreeSyncService.MaterializeAsync</c>.
    /// </summary>
    public static async Task RunAsync(ServerConfig config)
    {
        var workdir = config.WorktreePath;
        Directory.CreateDirectory(workdir);

        Console.WriteLine($"bpgit init: workdir={workdir}");
        Console.WriteLine("bpgit init: worktree bereit - wird durch ersten Push via PostReceiveHandler materialisiert");
        Console.WriteLine("bpgit init complete");

        await Task.CompletedTask;
    }
}
