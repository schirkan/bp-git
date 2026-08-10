using System;
using System.Collections.Generic;
using System.IO;

namespace BPGit.Cli.Config;

public class AppConfig
{
    public string ConnectionString { get; set; } = "";
    public string? SqlUser { get; set; }
    public string? SqlPasswordEnvVar { get; set; }
    public List<string> IgnoreTables { get; set; } = new();

    public string GetEffectiveConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(SqlUser) && !string.IsNullOrWhiteSpace(SqlPasswordEnvVar))
        {
            var pwd = Environment.GetEnvironmentVariable(SqlPasswordEnvVar) ?? "";
            return ConnectionString.Replace("{BPGIT_DB_PASSWORD}", pwd);
        }
        return ConnectionString;
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
                value = value.Substring(1, value.Length - 2);
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
        }
        return cfg;
    }
}
