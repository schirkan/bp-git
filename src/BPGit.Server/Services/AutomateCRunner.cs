using System;
using System.Diagnostics;
using System.IO;
using BPGit.Data;

namespace BPGit.Server.Services;

/// <summary>
/// Process.Start-Wrapper fuer AutomateC.exe, Server-Edition (verwendet <see cref="ServerConfig"/>
/// statt <c>AppConfig</c>). Setzt Auth-Flags (SSO oder SQL-Auth) je nach
/// <see cref="ServerConfig.SqlAuth"/>, gibt ExitCode + StdOut + StdErr zurueck.
///
/// Pfad zu AutomateC.exe wird per env-var <c>BPGIT_AUTOMATE_PATH</c> ueberschrieben,
/// sonst Default <c>C:\Program Files\Blue Prism Limited\Blue Prism Automate\AutomateC.exe</c>.
/// </summary>
public static class AutomateCRunner
{
    public record RunResult(int ExitCode, string StdOut, string StdErr);

    public static RunResult Run(ServerConfig cfg, params string[] args)
    {
        var automatePath = cfg.AutomateCPath;
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
        var auth = cfg.SqlAuth?.ToLowerInvariant();
        if (auth == "sso")
        {
            psi.ArgumentList.Add("/sso");
        }
        else if (auth == "user")
        {
            if (string.IsNullOrWhiteSpace(cfg.SqlUser))
                throw new InvalidOperationException("auth = \"user\" erfordert sqlUser in bpgit-server.json");
            psi.ArgumentList.Add("/user");
            psi.ArgumentList.Add(cfg.SqlUser);
            var pwd = cfg.SqlPassword;
            if (string.IsNullOrEmpty(pwd))
                throw new InvalidOperationException(
                    $"auth = \"user\" erfordert sqlPassword in bpgit-server.json (per Martin #6359, nicht mehr in env var)");
            psi.ArgumentList.Add(pwd);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unbekannter sqlAuth: '{cfg.SqlAuth}'. Erlaubt: 'sso' oder 'user'.");
        }

        // DB-Connection
        psi.ArgumentList.Add("/dbconname");
        psi.ArgumentList.Add(cfg.SqlServer);

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