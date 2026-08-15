using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BPGit.Server.Services;
using LibGit2Sharp;

namespace BPGit.Server.GitHttp;

/// <summary>
/// Pre-Receive-Hook-Logik: parsed einen Git-Push (<paramref name="oldRev"/> ..
/// <paramref name="newRev"/>) und ruft <see cref="BpSyncService"/> pro
/// file-change auf. Siehe <c>specs/SPEC-git-server.md</c> Kapitel 4
/// (processid-Mapping) + 7 (Server-Side Hooks).
///
/// Eingabe: <c>Repository</c> (LibGit2Sharp) + oldRev + newRev + refName
/// Ausgabe: <see cref="PreReceiveResult"/> mit Liste der Successes + Failures
///
/// Implementierung: manueller Tree-Walker (statt <c>repo.Diff.Compare&lt;T&gt;</c>,
/// weil die LibGit2Sharp 0.32.0-API die generische IDiffResult-Overload hier
/// nicht sauber resolved — siehe Build-Historie dieses Files).
///
/// Rename-Detection: NICHT enthalten (MVP-Limitation). Renames werden als
/// Delete + Add behandelt. User-Workflow: Rename via XML-Content-Edit (siehe #6311),
/// nicht via <c>git mv</c>.
/// </summary>
public sealed class PreReceiveHandler
{
    /// <summary>BP-XML Root: <c>&lt;process name="..."&gt;</c> oder <c>&lt;object name="..."&gt;</c>.</summary>
    private static readonly Regex XmlNameRegex = new(
        @"^\s*<(?:process|object)\s+[^>]*\bname\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly IBpSyncService _sync;

    public PreReceiveHandler(IBpSyncService sync) => _sync = sync;

    public async Task<PreReceiveResult> HandleAsync(
        Repository repo,
        string oldRev,
        string newRev,
        string refName,
        string pathFilter = "processes/")
    {
        var newCommit = repo.Lookup<Commit>(newRev);
        if (newCommit is null)
        {
            return PreReceiveResult.Failure(new[] { $"Cannot resolve newrev '{newRev}' for ref '{refName}'." });
        }

        var oldCommit = IsZeroSha(oldRev) ? null : repo.Lookup<Commit>(oldRev);
        var newFiles = WalkTreeEntries(newCommit.Tree, "");
        var oldFiles = oldCommit?.Tree is null ? null : WalkTreeEntries(oldCommit.Tree, "");

        var successes = new List<string>();
        var failures = new List<string>();

        // Single pass: iterate the union of both file maps. For each path we
        // determine the action by which trees the entry appears in:
        //   - in both, same SHA         -> Unmodified (skip)
        //   - in both, different SHA    -> Modify (call ModifyAsync)
        //   - in new only               -> Add    (call AddAsync)
        //   - in old only               -> Delete (call DeleteAsync)
        // This replaces the formerly separate Pass 1 + Pass 2 loops and keeps
        // the dict-based lookup instead of the old Tree-Indexer hack.
        foreach (var path in UnionKeys(newFiles, oldFiles))
        {
            if (!IsProcessXml(path, pathFilter))
                continue;

            newFiles.TryGetValue(path, out var newEntry);
            TreeEntry? oldEntry = null;
            if (oldFiles is not null) oldFiles.TryGetValue(path, out oldEntry);

            if (SameBlob(newEntry, oldEntry))
                continue;

            await DispatchAsync(newEntry, oldEntry, path, successes, failures);
        }

        return failures.Count == 0
            ? PreReceiveResult.Success(successes)
            : PreReceiveResult.Failure(failures);
    }

    /// <summary>
    /// Recursively walks a tree (including sub-trees) and flattens every file
    /// (blob) entry into a Dictionary keyed by its full repository-relative
    /// path (e.g. "processes/Objects/MyProcess.xml"). LibGit2Sharp's Tree
    /// enumerator only iterates direct children — sub-trees appear as a
    /// single TreeEntry with Path="<dirname>" (no trailing slash) — so we
    /// descend into them manually. Submodule commits and empty paths are
    /// skipped.
    /// </summary>
    internal static Dictionary<string, TreeEntry> WalkTreeEntries(Tree tree, string prefix)
    {
        var result = new Dictionary<string, TreeEntry>(StringComparer.OrdinalIgnoreCase);
        WalkTreeEntriesRecursive(tree, prefix, result);
        return result;
    }

    private static void WalkTreeEntriesRecursive(Tree tree, string prefix, Dictionary<string, TreeEntry> result)
    {
        foreach (var entry in tree)
        {
            if (entry.Path is null) continue;
            // LibGit2Sharp 0.32.0 quirk: blob entries inside sub-trees sometimes
            // retain the full repository-relative path (e.g. "processes/old.xml"
            // instead of just "old.xml"). Take only the last path component as
            // the leaf name so path composition with the recursion prefix
            // doesn't produce doubled paths like "processes/processes/old.xml".
            var leafName = entry.Path;
            var lastSlash = leafName.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                leafName = leafName.Substring(lastSlash + 1);
            }
            var entryPath = string.IsNullOrEmpty(prefix) ? leafName : prefix + "/" + leafName;
            if (entry.TargetType == TreeEntryTargetType.Tree && entry.Target is Tree subTree)
            {
                WalkTreeEntriesRecursive(subTree, entryPath, result);
            }
            else
            {
                result[entryPath] = entry;
            }
        }
    }

    private static IEnumerable<string> UnionKeys(
        Dictionary<string, TreeEntry> newFiles,
        Dictionary<string, TreeEntry>? oldFiles)
    {
        if (oldFiles is null)
            return newFiles.Keys;
        var set = new HashSet<string>(newFiles.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var key in oldFiles.Keys)
            set.Add(key);
        return set;
    }

    private static bool IsProcessXml(string path, string pathFilter) =>
        path.StartsWith(pathFilter, StringComparison.OrdinalIgnoreCase)
        && path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static bool SameBlob(TreeEntry? newEntry, TreeEntry? oldEntry) =>
        newEntry is not null
        && oldEntry is not null
        && newEntry.Target is Blob newBlob
        && oldEntry.Target is Blob oldBlob
        && newBlob.Sha == oldBlob.Sha;

    private async Task DispatchAsync(
        TreeEntry? newEntry,
        TreeEntry? oldEntry,
        string path,
        List<string> successes,
        List<string> failures)
    {
        // newEntry fehlt -> Delete (nur in oldTree)
        if (newEntry is null)
        {
            var oldName = Path.GetFileNameWithoutExtension(path);
            var r = await _sync.DeleteAsync(oldName);
            if (r.Ok) successes.Add($"Delete {oldName}");
            else failures.Add(r.Message ?? "Delete failed (no message)");
            return;
        }

        // newEntry vorhanden -> Add oder Modify (lesen XML-Content + Namen extrahieren)
        var xmlContent = TryGetXmlContent(newEntry, path, failures);
        if (xmlContent is null) return;

        var newName = ExtractProcessName(xmlContent);
        if (string.IsNullOrEmpty(newName))
        {
            failures.Add($"Add/Modify: cannot extract process name from '{path}'.");
            return;
        }

        if (oldEntry is null)
        {
            // Added (in neuer Tree, nicht in alter)
            var r = await _sync.AddAsync(xmlContent, newName);
            if (r.Ok) successes.Add($"Add {newName}");
            else failures.Add(r.Message ?? "Add failed (no message)");
        }
        else
        {
            // Modified (existiert in beiden Trees, aber SHA differs)
            var oldName = Path.GetFileNameWithoutExtension(path);
            var r = await _sync.ModifyAsync(xmlContent, oldName, newName);
            if (r.Ok) successes.Add($"Modify {oldName} -> {newName}");
            else failures.Add(r.Message ?? "Modify failed (no message)");
        }
    }

    private static string? TryGetXmlContent(TreeEntry entry, string path, List<string> failures)
    {
        if (entry.Target is not Blob blob)
        {
            failures.Add($"Add/Modify: '{path}' ist kein Blob (Mode={entry.Mode}).");
            return null;
        }
        return blob.GetContentText(Utf8NoBom);
    }

    internal static bool IsZeroSha(string? sha) =>
        string.IsNullOrEmpty(sha) || sha.All(c => c == '0');

    internal static string? ExtractProcessName(string xml)
    {
        var match = XmlNameRegex.Match(xml);
        return match.Success ? match.Groups[1].Value : null;
    }
}

public sealed record PreReceiveResult(bool Ok, IReadOnlyList<string> Successes, IReadOnlyList<string> Failures)
{
    public string Summary => Ok
        ? $"{Successes.Count} processes synced"
        : $"{Failures.Count} failures (of {Successes.Count + Failures.Count} operations): {string.Join(" | ", Failures)}";

    public static PreReceiveResult Success(IReadOnlyList<string> successes)
        => new(true, successes, Array.Empty<string>());
    public static PreReceiveResult Failure(IReadOnlyList<string> failures)
        => new(false, Array.Empty<string>(), failures);
}
