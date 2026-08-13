using BPGit.Cli.Config;
using System;
using System.Diagnostics;
using System.IO;

namespace BPGit.Cli.Services;

/// <summary>
/// Process.Start-Wrapper fuer AutomateC.exe. Setzt Auth-Flags je nach
/// AppConfig.CliAuthMode ("sso" oder "user"), gibt ExitCode + StdOut + StdErr zurueck.
/// </summary>
public static class AutomateCRunner
{
    public record RunResult(int ExitCode, string StdOut, string StdErr);

    public static RunResult Run(AppConfig cfg, params string[] args)
    {
        if (!File.Exists(cfg.AutomateCPath))
            throw new FileNotFoundException($"AutomateC.exe nicht gefunden: {cfg.AutomateCPath}");

        var psi = new ProcessStartInfo
        {
            FileName = cfg.AutomateCPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Auth-Setup
        var auth = cfg.CliAuthMode?.ToLowerInvariant();
        if (auth == "sso")
        {
            psi.ArgumentList.Add("/sso");
        }
        else if (auth == "user")
        {
            if (string.IsNullOrWhiteSpace(cfg.CliUsername))
                throw new InvalidOperationException("auth = \"user\" erfordert cli_username in .bpgit/config.toml [cli] section");
            psi.ArgumentList.Add("/user");
            psi.ArgumentList.Add(cfg.CliUsername);
            var pwd = cfg.GetCliPassword();
            if (string.IsNullOrEmpty(pwd))
                throw new InvalidOperationException(
                    "auth = \"user\" erfordert cli_password in .bpgit/config.toml [cli] section");
            psi.ArgumentList.Add(pwd);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unbekannter auth mode: '{cfg.CliAuthMode}'. Erlaubt: 'sso' oder 'user'.");
        }

        // Action-Args
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return new RunResult(p.ExitCode, stdout, stderr);
    }
}
