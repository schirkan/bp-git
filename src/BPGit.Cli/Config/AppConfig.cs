using System;
using System.Collections.Generic;
using System.IO;

namespace BPGit.Cli.Config;

public class AppConfig
{
    // [bp] section - SQL connection (used for read-only operations: pull, status, lock-check)
    public string ConnectionString { get; set; } = "";
    public string? SqlUser { get; set; }
    public string? SqlPasswordEnvVar { get; set; }
    public List<string> IgnoreTables { get; set; } = new();

    // [cli] section - AutomateC.exe integration (used for write operations: commit)
    public string AutomateCPath { get; set; } = @"C:\Program Files\Blue Prism Limited\Blue Prism Automate\AutomateC.exe";
    public string CliAuthMode { get; set; } = "sso"; // "sso" | "user"
    public string? CliUsername { get; set; }
    public string? CliPasswordEnvVar { get; set; } = "BPGIT_CLI_PASSWORD";

    public string GetEffectiveConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(SqlUser) && !string.IsNullOrWhiteSpace(SqlPasswordEnvVar))
        {
            var pwd = Environment.GetEnvironmentVariable(SqlPasswordEnvVar) ?? "";
            return ConnectionString.Replace("{BPGIT_DB_PASSWORD}", pwd);
        }
        return ConnectionString;
    }

    /// <summary>
    /// Returns the CLI password from the configured env var (only used when auth = "user").
    /// Null if auth != "user" or env var not set.
    /// </summary>
    public string? GetCliPassword()
    {
        if (!string.Equals(CliAuthMode, "user", StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.IsNullOrWhiteSpace(CliPasswordEnvVar))
            return null;
        return Environment.GetEnvironmentVariable(CliPasswordEnvVar);
    }

    public static AppConfig Load(string path)
    {
        var cfg = new AppConfig();
        if (!File.Exists(path)) return cfg;

        string? section = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line.Substring(1, line.Length - 2);
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line.Substring(0, eq).Trim();
            var value = line.Substring(eq + 1).Trim();
            if (value.StartsWith("\"") && value.EndsWith("\""))
            {
                value = value.Substring(1, value.Length - 2);
                // TOML basic-string escapes: at minimum \\ -> \ (for connection-string backslashes)
                value = value.Replace("\\\\", "\\");
            }
            if (section == "bp")
            {
                switch (key)
                {
                    case "connection_string": cfg.ConnectionString = value; break;
                    case "sql_user": cfg.SqlUser = value; break;
                    case "sql_password_env": cfg.SqlPasswordEnvVar = value; break;
                    case "ignore_tables":
                        cfg.IgnoreTables.AddRange(value.Split(',',
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                        break;
                }
            }
            else if (section == "cli")
            {
                switch (key)
                {
                    case "automatec_path": cfg.AutomateCPath = value; break;
                    case "auth": cfg.CliAuthMode = value; break;
                    case "username": cfg.CliUsername = value; break;
                    case "password_env": cfg.CliPasswordEnvVar = value; break;
                }
            }
        }
        return cfg;
    }
}
