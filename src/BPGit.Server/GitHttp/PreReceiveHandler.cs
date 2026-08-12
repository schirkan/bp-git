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
/// file-change auf. Siehe <c>context/SPEC-git-server.md</c> Kapitel 4
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

        var oldCommit = IsZeroSha(oldRev)
            ? null
            : repo.Lookup<Commit>(oldRev);

        var oldTree = oldCommit?.Tree;
        var newTree = newCommit.Tree;

        var successes = new List<string>();
        var failures = new List<string>();

        // Pass 1: Walk NEW tree — Modified (mit oldEntry) oder Added (ohne oldEntry)
        foreach (var newEntry in newTree)
        {
            if (newEntry.Path is null || !newEntry.Path.StartsWith(pathFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!newEntry.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                continue;

            var oldEntry = oldTree?[newEntry.Path];

            // SHA-Vergleich: wenn oldEntry existiert und gleicher Blob-SHA → Unmodified
            if (oldEntry is not null
                && oldEntry.Target is Blob oldBlob
                && newEntry.Target is Blob newBlob
                && oldBlob.Sha == newBlob.Sha)
            {
                continue;
            }

            var blob = newEntry.Target as Blob;
            if (blob is null)
            {
                failures.Add($"Add/Modify: '{newEntry.Path}' ist kein Blob (Mode={newEntry.Mode}).");
                continue;
            }
            var xmlContent = blob.GetContentText(Utf8NoBom);
            var newName = ExtractProcessName(xmlContent);
            if (string.IsNullOrEmpty(newName))
            {
                failures.Add($"Add/Modify: cannot extract process name from '{newEntry.Path}'.");
                continue;
            }
            var oldName = Path.GetFileNameWithoutExtension(newEntry.Path);

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
                var r = await _sync.ModifyAsync(xmlContent, oldName, newName);
                if (r.Ok) successes.Add($"Modify {oldName} -> {newName}");
                else failures.Add(r.Message ?? "Modify failed (no message)");
            }
        }

        // Pass 2: Walk OLD tree — Deleted (in alter Tree, nicht in neuer)
        if (oldTree is not null)
        {
            foreach (var oldEntry in oldTree)
            {
                if (oldEntry.Path is null || !oldEntry.Path.StartsWith(pathFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!oldEntry.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Wenn oldEntry.Path in newTree existiert → wurde oben behandelt (Modified/Added/Unmodified)
                var newEntry = newTree[oldEntry.Path];
                if (newEntry is not null)
                    continue;

                var oldName = Path.GetFileNameWithoutExtension(oldEntry.Path);
                var r = await _sync.DeleteAsync(oldName);
                if (r.Ok) successes.Add($"Delete {oldName}");
                else failures.Add(r.Message ?? "Delete failed (no message)");
            }
        }

        return failures.Count == 0
            ? PreReceiveResult.Success(successes)
            : PreReceiveResult.Failure(failures);
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