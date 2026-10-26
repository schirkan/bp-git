using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BPGit.Data;

/// <summary>
/// Server configuration loaded from
/// <c>bpgit.json</c> next to the executable (or via <paramref name="configPath"/>
/// override). All fields have defaults so a fresh install with an empty config
/// still works.
///
/// Sections (logical, not literal JSON sections):
///  - Server-side: <c>listenUrls</c>, <c>repoRoot</c>, <c>repoName</c>
///  - BP-DB connection: <c>sqlServer</c>, <c>sqlDatabase</c>, <c>sqlAuth</c>,
///    <c>sqlUser</c>, <c>sqlPassword</c>
///  - Server-side: <c>worktreeDir</c>, <c>automatecPath</c>
///
/// Default config-path: <c>AppContext.BaseDirectory/bpgit.json</c> (the directory
/// containing the <c>bpgit</c> binary). Override via:
///  - Server: positional config-path argument
///  - Test: <c>ServerConfig.Load("/path/to/config.json")</c>
///
/// Lives in BPGit.Data because server services and repository initialization
/// both need this configuration.
/// </summary>
public sealed class ServerConfig
{
    /// <summary>
    /// HTTP listen URL(s) for Kestrel. Default: <c>http://0.0.0.0:8181</c>.
    /// <para>
    /// <c>0.0.0.0</c> is a <em>bind</em> address - it accepts connections on
    /// every interface. Clients must connect via the host's actual name or IP
    /// (<c>localhost</c> / <c>127.0.0.1</c> / the machine's hostname), NOT via
    /// <c>0.0.0.0</c> (Windows rejects "Address not available"). For LAN access
    /// from other machines, replace <c>0.0.0.0</c> with the machine's actual
    /// IPv4 address (e.g. <c>http://192.168.1.10:8181</c>) or use
    /// <c>http://+:8181</c> (HTTP.sys namespace reservation on Windows).
    /// </para>
    /// </summary>
    [JsonPropertyName("listenUrls")]
    public List<string> ListenUrls { get; init; } = new() { "http://0.0.0.0:8181" };

    /// <summary>Root directory for bare git repos. Relative paths (e.g. <c>.\repos</c>) resolve against
    /// <c>AppContext.BaseDirectory</c> (the directory containing the <c>bpgit</c> executable per
    /// Martin #6480). Absolute paths (e.g. <c>C:\bpgit\repos</c>) are used as-is.</summary>
    [JsonPropertyName("repoRoot")]
    public string RepoRoot { get; init; } = ".\\repos";

    /// <summary>Default repo name used by the <c>init</c> subcommand.</summary>
    [JsonPropertyName("repoName")]
    public string RepoName { get; init; } = "bp-git";

    /// <summary>SQL Server instance for the BP-DB connection (e.g. <c>(localdb)\BluePrismLocalDB</c>).</summary>
    [JsonPropertyName("sqlServer")]
    public string SqlServer { get; init; } = @"(localdb)\BluePrismLocalDB";

    /// <summary>BP database name. Default: BluePrism</summary>
    [JsonPropertyName("sqlDatabase")]
    public string SqlDatabase { get; init; } = "BluePrism";

    /// <summary>Auth mode for BP-DB: <c>sso</c> (Windows Integrated) or <c>user</c> (SQL auth).</summary>
    [JsonPropertyName("sqlAuth")]
    public string SqlAuth { get; init; } = "sso";

    /// <summary>SQL username (only used when <see cref="SqlAuth"/> = <c>user</c>).</summary>
    [JsonPropertyName("sqlUser")]
    public string? SqlUser { get; init; }

    /// <summary>SQL password (plaintext in config file, only used when <see cref="SqlAuth"/> = <c>user</c>). Sensitive - see .gitignore for bpgit.json.</summary>
    [JsonPropertyName("sqlPassword")]
    public string? SqlPassword { get; init; }

    /// <summary>Server worktree directory for materialized BP process XML files. Must be the repository's processes directory. Relative paths resolve against the executable directory.</summary>
    [JsonPropertyName("worktreeDir")]
    public string WorktreeDir { get; init; } = "./processes";

    /// <summary>Path to AutomateC.exe. Default: standard install location.</summary>
    [JsonPropertyName("automatecPath")]
    public string AutomateCPath { get; init; } = @"C:\Program Files\Blue Prism Limited\Blue Prism Automate\AutomateC.exe";

    /// <summary>Absolute path to the bare repo (derived from <see cref="RepoRoot"/> + <see cref="RepoName"/>).
    /// Relative <see cref="RepoRoot"/> is resolved against <c>AppContext.BaseDirectory</c> so the
    /// server always finds its repos next to the executable, regardless of the CWD it was started from.</summary>
    [JsonIgnore]
    public string BareRepoPath
    {
        get
        {
            var root = Path.IsPathRooted(RepoRoot)
                ? RepoRoot
                : Path.Combine(AppContext.BaseDirectory, RepoRoot);
            return Path.Combine(root, $"{RepoName}.git");
        }
    }

    /// <summary>Effective worktree directory (absolute path resolved from <see cref="WorktreeDir"/>).</summary>
    [JsonIgnore]
    public string WorktreePath => Path.GetFullPath(
        Path.IsPathRooted(WorktreeDir)
            ? WorktreeDir
            : Path.Combine(AppContext.BaseDirectory, WorktreeDir));

    /// <summary>
    /// Builds the ADO.NET connection string from the SQL configuration.
    /// SQL-auth credentials are included only in the returned connection string;
    /// callers must not log it.
    /// </summary>
    public string GetEffectiveConnectionString()
    {
        var builder = new DbConnectionStringBuilder
        {
            ["Server"] = SqlServer,
            ["Database"] = SqlDatabase,
            ["TrustServerCertificate"] = true,
        };

        if (SqlAuth.Equals("sso", StringComparison.OrdinalIgnoreCase))
        {
            builder["Integrated Security"] = "SSPI";
            return builder.ConnectionString;
        }

        if (!SqlAuth.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported SQL authentication mode '{SqlAuth}'. Expected 'sso' or 'user'.");
        }
        if (string.IsNullOrWhiteSpace(SqlUser) || string.IsNullOrWhiteSpace(SqlPassword))
        {
            throw new InvalidOperationException("sqlUser and sqlPassword are required when sqlAuth is 'user'.");
        }

        builder["User ID"] = SqlUser;
        builder["Password"] = SqlPassword;
        return builder.ConnectionString;
    }

    /// <summary>
    /// Loads config from JSON file. Default location: <c>AppContext.BaseDirectory/bpgit.json</c>.
    /// Returns defaults if file is missing or invalid.
    /// </summary>
    public static ServerConfig Load(string? configPath = null)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            configPath = Path.Combine(AppContext.BaseDirectory, "bpgit.json");
        }

        if (!File.Exists(configPath))
        {
            Console.WriteLine($"[bpgit] No config file at {configPath} - using defaults.");
            return new ServerConfig();
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var cfg = JsonSerializer.Deserialize<ServerConfig>(json, JsonOpts);
            if (cfg is null)
            {
                Console.Error.WriteLine($"[bpgit] Config at {configPath} is empty - using defaults.");
                return new ServerConfig();
            }
            Console.WriteLine($"[bpgit] Config loaded from {configPath}.");
            return cfg;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[bpgit] Failed to parse {configPath}: {ex.Message}");
            Console.Error.WriteLine($"[bpgit] Falling back to defaults.");
            return new ServerConfig();
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
