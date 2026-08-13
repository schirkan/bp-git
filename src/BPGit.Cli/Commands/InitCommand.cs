using System;
using System.IO;
using System.Threading.Tasks;

namespace BPGit.Cli.Commands;

public static class InitCommand
{
    public static async Task RunAsync(string workdir, bool installHooks = false)
    {
        var bpgitDir = Path.Combine(workdir, ".bpgit");
        Directory.CreateDirectory(bpgitDir);

        var configPath = Path.Combine(bpgitDir, "config.toml");
        if (!File.Exists(configPath))
        {
            var cfg = "# bpgit config - Blue Prism Git adapter\n" +
                "# Zwei Sektionen: [bp] fuer SqlConnection (read-only ops), [cli] fuer AutomateC.exe (write ops)\n\n" +
                "[bp]\n" +
                "connection_string = \"Server=(localdb)\\\\BluePrismLocalDB;Integrated Security=SSPI;Database=BluePrism\"\n" +
                "# Optional: SQL-Auth fallback (only if SSPI unavailable).\n" +
                "# Credentials direkt in config (kein env-var-Lookup).\n" +
                "# sql_username = \"bpgit_readonly\"\n" +
                "# sql_password = \"...\"\n\n" +
                "[cli]\n" +
                "# Path to AutomateC.exe (default is the standard install location)\n" +
                "automatec_path = \"C:\\\\Program Files\\\\Blue Prism Limited\\\\Blue Prism Automate\\\\AutomateC.exe\"\n" +
                "# Auth mode: \"sso\" (default, Windows Integrated Auth) or \"user\"\n" +
                "auth = \"sso\"\n" +
                "# Only used when auth = \"user\". Credentials direkt in config.\n" +
                "# cli_username = \"admin\"\n" +
                "# cli_password = \"...\"\n";
            await File.WriteAllTextAsync(configPath, cfg);
            Console.WriteLine($"Created {configPath}");
        }
        else
        {
            Console.WriteLine($"{configPath} already exists");
        }

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
[ -d .bpgit ] && echo '[bpgit] Worktree kann von BP-DB abweichen. ''bpgit status'' pruefen, ggf. ''bpgit pull'' ausfuehren.'
";
        var postMerge = @"#!/bin/sh
# bpgit post-merge hook
# Warnt nach Branch-Merge, dass Worktree moeglicherweise von BP-DB abweicht.
# KEIN auto-pull, KEIN auto-rewrite - nur Hinweis.
[ -d .bpgit ] && echo '[bpgit] Nach Merge: Worktree ggf. von BP-DB abweichen. ''bpgit pull'' empfohlen.'
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
