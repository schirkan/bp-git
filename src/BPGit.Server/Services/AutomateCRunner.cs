using System;
using System.Diagnostics;
using System.IO;

namespace BPGit.Server.Services;

/// <summary>
/// Process.Start-Wrapper fuer AutomateC.exe, Server-Edition (verwendet <see cref="ServerConfig"/>
/// statt <c>AppConfig</c>). Setzt Auth-Flags (SSO oder SQL-Auth) je nach
/// <see cref="ServerConfig.BpAuth"/>, gibt ExitCode + StdOut + StdErr zurueck.
///
/// Pfad zu AutomateC.exe wird per env-var <c>BPGIT_AUTOMATE_PATH</c> ueberschrieben,
/// sonst Default <c>C:\Program Files\Blue Prism Limited\Blue Prism Automate\AutomateC.exe</c>.
/// </summary>
public static class AutomateCRunner
{
    public record RunResult(int ExitCode, string StdOut, string StdErr);

    private const string DefaultAutomateCPath =
        @"C:\Program Files\Blue Prism Limited\Blue Prism Automate\AutomateC.exe";

    public static string ResolveAutomateCPath(ServerConfig _)
    {
        var path = Environment.GetEnvironmentVariable("BPGIT_AUTOMATE_PATH");
        return string.IsNullOrWhiteSpace(path) ? DefaultAutomateCPath : path;
    }

    public static RunResult Run(ServerConfig cfg, params string[] args)
    {
        var automatePath = ResolveAutomateCPath(cfg);
        if (!File.Exists(automatePath))
            throw new FileNotFoundException($"AutomateC.exe nicht gefunden: {automatePath}");

        var psi = new ProcessStartInfo
        {
            FileName = automatePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Auth-Setup
        var auth = cfg.BpAuth?.ToLowerInvariant();
        if (auth == "sso")
        {
            psi.ArgumentList.Add("/sso");
        }
        else if (auth == "user")
        {
            if (string.IsNullOrWhiteSpace(cfg.BpUser))
                throw new InvalidOperationException("auth = \"user\" erfordert bpUser in bpgit-server.json");
            psi.ArgumentList.Add("/user");
            psi.ArgumentList.Add(cfg.BpUser);
            var pwd = Environment.GetEnvironmentVariable(cfg.BpPasswordEnv);
            if (string.IsNullOrEmpty(pwd))
                throw new InvalidOperationException(
                    $"auth = \"user\" erfordert password in env var {cfg.BpPasswordEnv}");
            psi.ArgumentList.Add(pwd);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unbekannter bpAuth: '{cfg.BpAuth}'. Erlaubt: 'sso' oder 'user'.");
        }

        // DB-Connection
        psi.ArgumentList.Add("/dbconname");
        psi.ArgumentList.Add(cfg.BpServer);

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