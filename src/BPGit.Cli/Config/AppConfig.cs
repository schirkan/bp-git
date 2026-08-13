using System;
using System.Collections.Generic;
using System.IO;

namespace BPGit.Cli.Config;

public class AppConfig
{
    // [bp] section - SQL connection (used for read-only operations: pull, status, lock-check)
    public string ConnectionString { get; set; } = "";
    public string? SqlUsername { get; set; }
    public string? SqlPassword { get; set; }
    public List<string> IgnoreTables { get; set; } = new();

    // [cli] section - AutomateC.exe integration (used for write operations: commit)
    public string AutomateCPath { get; set; } = @"C:\Program Files\Blue Prism Limited\Blue Prism Automate\AutomateC.exe";
    public string CliAuthMode { get; set; } = "sso"; // "sso" | "user"
    public string? CliUsername { get; set; }
    public string? CliPassword { get; set; }

    /// <summary>
    /// Returns the connection string. If sql_username is set, appends User ID / Password
    /// to the connection string for SQL authentication (otherwise the original
    /// connection_string — typically Windows Integrated Auth — is returned unchanged).
    /// </summary>
    public string GetEffectiveConnectionString()
    {
        if (string.IsNullOrWhiteSpace(SqlUsername))
            return ConnectionString;

        var sep = ConnectionString.EndsWith(";", StringComparison.Ordinal) ? "" : ";";
        var pwd = SqlPassword ?? "";
        return $"{ConnectionString}{sep}User ID={SqlUsername};Password={pwd};";
    }

    /// <summary>
    /// Returns the CLI password from config (only used when auth = "user").
    /// Null if auth != "user" or cli_password not configured.
    /// </summary>
    public string? GetCliPassword()
    {
        if (!string.Equals(CliAuthMode, "user", StringComparison.OrdinalIgnoreCase))
            return null;
        return CliPassword;
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
                    case "sql_username": cfg.SqlUsername = value; break;
                    case "sql_password": cfg.SqlPassword = value; break;
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
                    case "cli_username": cfg.CliUsername = value; break;
                    case "cli_password": cfg.CliPassword = value; break;
                }
            }
        }
        return cfg;
    }
}
