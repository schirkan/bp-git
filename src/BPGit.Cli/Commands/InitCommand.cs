using BPGit.Data;
using System;
using System.IO;
using System.Threading.Tasks;

namespace BPGit.Cli.Commands;

/// <summary>
/// bpgit init — Bootstrap CLI worktree from BP-DB (no .bpgit/ directory;
/// the unified bpgit.json next to the executable is the single config).
///
/// Optionally installs git hooks (post-checkout, post-merge) for drift warnings.
/// </summary>
public static class InitCommand
{
    public static async Task RunAsync(ServerConfig config, bool installHooks = false)
    {
        var workdir = config.WorktreePath;
        Directory.CreateDirectory(workdir);

        Console.WriteLine($"bpgit init: workdir={workdir}");
        Console.WriteLine($"bpgit init: snapshot={config.SnapshotPath}");
        Console.WriteLine($"bpgit init: bp-server={config.SqlServer}, db={config.SqlDatabase}");
        Console.WriteLine($"bpgit init: connect-string={config.GetEffectiveConnectionString()}");

        // Auto-pull from BP-DB into worktree
        await PullCommand.RunAsync(config);

        if (installHooks)
        {
            await InstallGitHooksAsync(workdir);
        }

        Console.WriteLine("bpgit init complete");
    }

    private static async Task InstallGitHooksAsync(string workdir)
    {
        var gitDir = Path.Combine(workdir, ".git");
        if (!Directory.Exists(gitDir))
        {
            Console.WriteLine($"No .git directory found at {gitDir} - skipping hooks install");
            return;
        }

        var hooksDir = Path.Combine(gitDir, "hooks");
        Directory.CreateDirectory(hooksDir);

        var postCheckout = @"#!/bin/sh
# bpgit post-checkout hook
# Warnt nach Branch-Wechsel, dass Worktree moeglicherweise von BP-DB abweicht.
# KEIN auto-pull, KEIN auto-rewrite - nur Hinweis.
[ -d processes ] && echo '[bpgit] Worktree kann von BP-DB abweichen. ''bpgit status'' pruefen, ggf. ''bpgit pull'' ausfuehren.'
";
        var postMerge = @"#!/bin/sh
# bpgit post-merge hook
# Warnt nach Branch-Merge, dass Worktree moeglicherweise von BP-DB abweicht.
# KEIN auto-pull, KEIN auto-rewrite - nur Hinweis.
[ -d processes ] && echo '[bpgit] Nach Merge: Worktree ggf. von BP-DB abweichen. ''bpgit pull'' empfohlen.'
";

        var postCheckoutPath = Path.Combine(hooksDir, "post-checkout");
        var postMergePath = Path.Combine(hooksDir, "post-merge");

        await File.WriteAllTextAsync(postCheckoutPath, postCheckout);
        await File.WriteAllTextAsync(postMergePath, postMerge);

        // On Unix-like systems, make hooks executable. No-op on Windows (no chmod).
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(postCheckoutPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                File.SetUnixFileMode(postMergePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warn: chmod fehlgeschlagen ({ex.GetType().Name}) - Hooks funktionieren moeglicherweise nicht");
        }

        Console.WriteLine($"Installed hooks: {postCheckoutPath}");
        Console.WriteLine($"Installed hooks: {postMergePath}");
    }
}
