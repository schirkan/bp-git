using System.Text.Json;
using System.Text.Json.Serialization;

namespace BPGit.Server;

/// <summary>
/// Configuration loaded from <c>C:\bpgit\bpgit-server.json</c> (or path from
/// <c>BPGIT_SERVER_CONFIG</c> env var). All fields have defaults so a fresh
/// install with an empty config still works.
/// </summary>
public sealed class ServerConfig
{
    /// <summary>HTTP listen URL(s) for Kestrel. Default: http://0.0.0.0:8181</summary>
    [JsonPropertyName("listenUrls")]
    public List<string> ListenUrls { get; init; } = new() { "http://0.0.0.0:8181" };

    /// <summary>Root directory for bare git repos. Default: C:\bpgit\repos</summary>
    [JsonPropertyName("repoRoot")]
    public string RepoRoot { get; init; } = @"C:\bpgit\repos";

    /// <summary>Default repo name used by the <c>init</c> subcommand.</summary>
    [JsonPropertyName("repoName")]
    public string RepoName { get; init; } = "bp-git";

    /// <summary>BP SQL Server instance (e.g. <c>(localdb)\BluePrismLocalDB</c>).</summary>
    [JsonPropertyName("bpServer")]
    public string BpServer { get; init; } = @"(localdb)\BluePrismLocalDB";

    /// <summary>BP database name. Default: BluePrism</summary>
    [JsonPropertyName("bpDatabase")]
    public string BpDatabase { get; init; } = "BluePrism";

    /// <summary>Auth mode for BP: <c>sso</c> (Windows Integrated) or <c>user</c> (SQL auth).</summary>
    [JsonPropertyName("bpAuth")]
    public string BpAuth { get; init; } = "sso";

    /// <summary>BP SQL username (only used when <see cref="BpAuth"/> = <c>user</c>).</summary>
    [JsonPropertyName("bpUser")]
    public string? BpUser { get; init; }

    /// <summary>BP SQL password (env-var reference, never in config file directly).</summary>
    [JsonPropertyName("bpPasswordEnv")]
    public string BpPasswordEnv { get; init; } = "BPGIT_DB_PASSWORD";

    /// <summary>Absolute path to the bare repo (derived from <see cref="RepoRoot"/> + <see cref="RepoName"/>).</summary>
    [JsonIgnore]
    public string BareRepoPath => Path.Combine(RepoRoot, $"{RepoName}.git");

    /// <summary>
    /// Loads config from JSON file. Search order:
    /// 1. <c>BPGIT_SERVER_CONFIG</c> env var
    /// 2. <c>C:\bpgit\bpgit-server.json</c>
    /// 3. Defaults (no file required)
    /// </summary>
    public static ServerConfig Load(string[] args)
    {
        var configPath = Environment.GetEnvironmentVariable("BPGIT_SERVER_CONFIG");
        if (string.IsNullOrWhiteSpace(configPath))
        {
            configPath = @"C:\bpgit\bpgit-server.json";
        }

        if (!File.Exists(configPath))
        {
            Console.WriteLine($"[bpgit-server] No config file at {configPath} — using defaults.");
            return new ServerConfig();
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var cfg = JsonSerializer.Deserialize<ServerConfig>(json, JsonOpts);
            if (cfg is null)
            {
                Console.Error.WriteLine($"[bpgit-server] Config at {configPath} is empty — using defaults.");
                return new ServerConfig();
            }
            Console.WriteLine($"[bpgit-server] Config loaded from {configPath}.");
            return cfg;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[bpgit-server] Failed to parse {configPath}: {ex.Message}");
            Console.Error.WriteLine($"[bpgit-server] Falling back to defaults.");
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
