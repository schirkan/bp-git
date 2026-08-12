using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BPGit.Data.Models;

namespace BPGit.Server.Services;

/// <summary>
/// Materializes BP-DB state to a target worktree directory.
///
/// Filename invariant (per Martin #6311): filename = sanitize(BPAProcess.name) + ".xml"
/// Folder hierarchy: &lt;targetRoot&gt;/&lt;TreeName&gt;/&lt;GroupName&gt;/&lt;processname&gt;.xml
///
/// Tree filter: only BPATree id IN (2=Processes, 3=Objects) — others excluded
/// (Tiles, Queues, Resources, users — BP-Studio-specific per #6287).
///
/// M:N duplication: a single Process in multiple Groups → file in each folder.
/// </summary>
public sealed class WorktreeSyncService
{
    private readonly BpDbService _db;

    // Windows-Dateinamen verbotene Zeichen (per Martin #6311 + alle Path.GetInvalidFileNameChars inkl. / und \)
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    // Leading XML-Comments brechen BP's /import-Parser (per Martin #6277)
    private static readonly Regex LeadingComments =
        new(@"^\s*(?:<!--[\s\S]*?-->\s*)+", RegexOptions.Compiled);

    public WorktreeSyncService(BpDbService db)
    {
        _db = db;
    }

    /// <summary>
    /// Reads BP-DB and writes canonical XML files to <paramref name="targetRoot"/>.
    /// Stale XML files (in processes/ subdirs) are deleted so renames propagate.
    /// </summary>
    public async Task<MaterializeResult> MaterializeAsync(string targetRoot, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetRoot))
            throw new ArgumentException("targetRoot required", nameof(targetRoot));

        Directory.CreateDirectory(targetRoot);

        var processes = await _db.GetAllProcessesAsync();
        var folderStruct = await _db.GetFolderStructureAsync();

        var groupsById = folderStruct.Groups.ToDictionary(g => g.Id);
        var treesById = folderStruct.Trees.ToDictionary(t => t.Id);

        // Build processId -> list of folder paths (via memberships + group + tree)
        var processFolders = new Dictionary<Guid, List<string>>();
        foreach (var m in folderStruct.Memberships)
        {
            if (!groupsById.TryGetValue(m.GroupId, out var grp)) continue;
            if (!treesById.TryGetValue(grp.TreeId, out var tree)) continue;

            var folderPath = Path.Combine(targetRoot, tree.Name, grp.Name);
            if (!processFolders.TryGetValue(m.ProcessId, out var list))
            {
                list = new List<string>();
                processFolders[m.ProcessId] = list;
            }
            list.Add(folderPath);
        }

        // Snapshot existing XML files in targetRoot
        var existingFiles = Directory.Exists(targetRoot)
            ? Directory.EnumerateFiles(targetRoot, "*.xml", SearchOption.AllDirectories)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keptFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int written = 0, deleted = 0, skipped = 0;
        var errors = new List<string>();

        // Write canonical files (skip processes without memberships)
        foreach (var proc in processes)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(proc.XmlContent)) { skipped++; continue; }
            if (!processFolders.TryGetValue(proc.ProcessId, out var folders)) { skipped++; continue; }

            var safeName = SanitizeFilename(proc.Name) + ".xml";
            var cleanXml = StripLeadingXmlComments(proc.XmlContent);

            foreach (var folder in folders)
            {
                Directory.CreateDirectory(folder);
                var filePath = Path.Combine(folder, safeName);
                keptFiles.Add(filePath);

                try
                {
                    if (File.Exists(filePath))
                    {
                        var existing = await File.ReadAllTextAsync(filePath, ct);
                        if (existing == cleanXml) continue; // no change
                    }
                    await File.WriteAllTextAsync(filePath, cleanXml, ct);
                    written++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Write {filePath}: {ex.Message}");
                }
            }
        }

        // Delete stale XML files (renames/deletes in BP-DB propagate to worktree)
        foreach (var oldFile in existingFiles)
        {
            if (!keptFiles.Contains(oldFile))
            {
                try { File.Delete(oldFile); deleted++; }
                catch (Exception ex) { errors.Add($"Delete {oldFile}: {ex.Message}"); }
            }
        }

        return new MaterializeResult(written, deleted, skipped, errors);
    }

    /// <summary>
    /// Windows-safe filename sanitization. Ersetzt alle Zeichen aus <see cref="Path.GetInvalidFileNameChars"/>
    /// (inkl. &lt;, &gt;, :, ", /, \, |, ?, *) durch _ und trimmt trailing dots/spaces.
    /// </summary>
    public static string SanitizeFilename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "_";
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(InvalidChars, chars[i]) >= 0)
                chars[i] = '_';
        }
        return new string(chars).TrimEnd('.', ' ');
    }

    /// <summary>
    /// Strip leading XML comments (BP's /import-Parser bricht sonst ab, per #6277).
    /// </summary>
    public static string StripLeadingXmlComments(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return xml;
        return LeadingComments.Replace(xml, string.Empty);
    }
}

public sealed record MaterializeResult(
    int Written,
    int Deleted,
    int Skipped,
    IReadOnlyList<string> Errors);