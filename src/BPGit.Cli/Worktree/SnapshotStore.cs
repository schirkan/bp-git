using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BPGit.Cli.Worktree;

public class SnapshotEntry
{
    [JsonPropertyName("hash")] public string Hash { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
}

public class Snapshot
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("extractedAt")] public DateTime ExtractedAt { get; set; }
    [JsonPropertyName("processes")] public Dictionary<string, SnapshotEntry> Processes { get; set; } = new();
}

public static class SnapshotStore
{
    public static string ComputeHash(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static Snapshot? Load(string worktreeRoot)
    {
        var path = Path.Combine(worktreeRoot, ".bpgit", "snapshot.json");
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path));
    }

    public static void Save(string worktreeRoot, Snapshot snapshot)
    {
        var path = Path.Combine(worktreeRoot, ".bpgit", "snapshot.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
