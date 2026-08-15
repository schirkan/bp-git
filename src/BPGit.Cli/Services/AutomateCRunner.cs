using BPGit.Data;
using System;
using System.Diagnostics;
using System.IO;

namespace BPGit.Cli.Services;

/// <summary>
/// Process.Start-Wrapper fuer AutomateC.exe. Setzt Auth-Flags je nach
/// <see cref="ServerConfig.CliAuthMode"/> ("sso" oder "user"), gibt ExitCode +
/// StdOut + StdErr zurueck.
/// </summary>
public static class AutomateCRunner
{
    public record RunResult(int ExitCode, string StdOut, string StdErr);

    public static RunResult Run(ServerConfig config, params string[] args)
    {
        if (!File.Exists(config.AutomateCPath))
            throw new FileNotFoundException($"AutomateC.exe nicht gefunden: {config.AutomateCPath}");

        var psi = new ProcessStartInfo
        {
            FileName = config.AutomateCPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Auth-Setup
        var auth = config.CliAuthMode?.ToLowerInvariant();
        if (auth == "sso")
        {
            psi.ArgumentList.Add("/sso");
        }
        else if (auth == "user")
        {
            if (string.IsNullOrWhiteSpace(config.CliUsername))
                throw new InvalidOperationException("cliAuthMode = \"user\" erfordert cliUsername in bpgit.json");
            psi.ArgumentList.Add("/user");
            psi.ArgumentList.Add(config.CliUsername);
            if (string.IsNullOrEmpty(config.CliPassword))
                throw new InvalidOperationException(
                    "cliAuthMode = \"user\" erfordert cliPassword in bpgit.json");
            psi.ArgumentList.Add(config.CliPassword);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unbekannter cliAuthMode: '{config.CliAuthMode}'. Erlaubt: 'sso' oder 'user'.");
        }

        // Action-Args
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return new RunResult(p.ExitCode, stdout, stderr);
    }
}
