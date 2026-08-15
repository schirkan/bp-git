using BPGit.Cli.Worktree;
using BPGit.Data;
using BPGit.Data.Connection;
using BPGit.Data.Repositories;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BPGit.Cli.Commands;

/// <summary>
/// bpgit pull — Materialize worktree from BP-DB into folder-aware layout (per #6289).
///
/// Layout:
///   WorktreeDir/
///     Processes/                  (BPATree id=2 only; other Trees excluded per #6287)
///       Default/                  (BPAGroup name)
///         MP - Subprocess A.xml    (filename = process.name + ".xml")
///       System Update/
///         Microsoft Store.xml
///     Objects/                    (BPATree id=3)
///       Default/
///         Data - SQL Server.xml
///
/// snapshot.json (extended per #6293) holds processid -> worktree-relative path mapping,
/// so commit/status/diff can resolve path -> processid without meta.json sidecars.
///
/// M:N Group-Process membership: same process appears under multiple folder paths
/// (file duplication, accepted per #6287).
/// </summary>
public static class PullCommand
{
    private static readonly Regex ProcessNameRegex =
        new(@"^\s*<(process|object)\s+[^>]*\bname\s*=\s*""([^""]+)""", RegexOptions.Compiled);

    private static readonly char[] InvalidNameChars =
        Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).Distinct().ToArray();

    public static async Task RunAsync(ServerConfig config)
    {
        var workdir = config.WorktreePath;
        Directory.CreateDirectory(workdir);

        var factory = new ConnectionFactory(config.GetEffectiveConnectionString());
        var repo = new ProcessRepository(factory);

        var processes = await repo.ListAllAsync();
        var folderStructure = await repo.GetFolderStructureAsync();
        Console.WriteLine($"Found {processes.Count} processes, {folderStructure.Groups.Count} folders across {folderStructure.Trees.Count} trees");

        // Build lookup maps
        var groupById = folderStructure.Groups.ToDictionary(g => g.Id);
        var treeById = folderStructure.Trees.ToDictionary(t => t.Id);
        var membershipsByProcessId = folderStructure.Memberships
            .GroupBy(m => m.ProcessId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.GroupId).ToList());

        // Wipe & recreate the configured worktree directory to avoid stale files
        if (Directory.Exists(workdir)) Directory.Delete(workdir, recursive: true);
        Directory.CreateDirectory(workdir);

        var snapshot = new Snapshot { ExtractedAt = DateTime.UtcNow };
        int skipped = 0;
        foreach (var p in processes)
        {
            var name = ExtractProcessName(p.processxml);
            if (string.IsNullOrEmpty(name))
            {
                Console.WriteLine($"  skipped {p.processid}: cannot extract name from processxml");
                skipped++;
                continue;
            }
            var sanitized = SanitizeFilename(name);
            if (string.IsNullOrEmpty(sanitized))
            {
                Console.WriteLine($"  skipped {p.processid}: sanitized filename is empty (name='{name}')");
                skipped++;
                continue;
            }

            // Build all folder paths for this process (M:N -> file duplication).
            var relPaths = new List<string>();
            if (membershipsByProcessId.TryGetValue(p.processid, out var groupIds))
            {
                foreach (var gid in groupIds)
                {
                    if (groupById.TryGetValue(gid, out var grp) && treeById.TryGetValue(grp.TreeId, out var tree))
                    {
                        relPaths.Add($"{SanitizeDirName(tree.Name)}/{SanitizeDirName(grp.Name)}/{sanitized}.xml");
                    }
                }
            }

            // Orphaned processes (no group membership) - put under _orphaned/ for review
            if (relPaths.Count == 0)
            {
                relPaths.Add($"_orphaned/{sanitized}.xml");
                Console.WriteLine($"  orphaned: {p.processid} ({p.name})");
            }

            // Write XML to each group path
            foreach (var relPath in relPaths)
            {
                var fullPath = Path.Combine(workdir, relPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                if (!string.IsNullOrEmpty(p.processxml))
                {
                    await File.WriteAllTextAsync(fullPath, p.processxml);
                }
            }

            snapshot.Processes[p.processid.ToString()] = new SnapshotEntry
            {
                Hash = SnapshotStore.ComputeHash(p.processxml ?? ""),
                Name = p.name,
                Type = p.ProcessType,
                Path = relPaths[0]
            };
        }

        SnapshotStore.Save(workdir, config.SnapshotFileName, snapshot);

        Console.WriteLine($"Snapshot saved ({snapshot.Processes.Count} entries, {skipped} skipped) to {Path.Combine(workdir, config.SnapshotFileName)}");
    }

    private static readonly Regex LeadingCommentsRegex =
        new(@"^\s*(?:<!--[\s\S]*?-->\s*)+", RegexOptions.Compiled);

    private static string? ExtractProcessName(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return null;
        // Leading XML-Comments strippen (BP-Studio-Save kann welche hinzufuegen;
        // sonst matcht ProcessNameRegex nicht, weil das erste '<' zum Comment gehoert)
        var cleaned = LeadingCommentsRegex.Replace(xml, string.Empty);
        var m = ProcessNameRegex.Match(cleaned);
        return m.Success ? m.Groups[2].Value : null;
    }

    private static string SanitizeFilename(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(InvalidNameChars, c) >= 0 ? '_' : c);
        var s = sb.ToString().TrimEnd('.', ' ');
        return s.Length == 0 ? "" : s;
    }

    private static string SanitizeDirName(string name) => SanitizeFilename(name);
}
