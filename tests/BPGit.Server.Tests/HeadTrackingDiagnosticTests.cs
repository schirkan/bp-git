using System;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Xunit;
using Xunit.Abstractions;

namespace BPGit.Server.Tests;

public class HeadTrackingDiagnosticTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _repoPath;
    private readonly Repository _repo;

    public HeadTrackingDiagnosticTests(ITestOutputHelper output)
    {
        _out = output;
        _repoPath = Path.Combine(Path.GetTempPath(), "bpgit-head-diag-" + Guid.NewGuid().ToString("N"));
        Repository.Init(_repoPath);
        _repo = new Repository(_repoPath);
    }

    public void Dispose()
    {
        try { _repo?.Dispose(); } catch { }
        GC.Collect();
        if (Directory.Exists(_repoPath))
        {
            for (int i = 0; i < 3; i++)
            {
                try { Directory.Delete(_repoPath, recursive: true); break; }
                catch { System.Threading.Thread.Sleep(100); }
            }
        }
    }

    // Skip-Reason: LibGit2Sharp 0.32.0 Branch/Ref API-Inkonsistenz (Branch.TargetIdentifier existiert nicht,
    // Refs.Add erwartet ObjectId-Overload). Diagnostic ist dokumentiert im Commit-Body und kann spaeter
    // reaktiviert werden, wenn die PreReceive HEAD-Tracking-Root-Cause-Forschung wieder aufgenommen wird.
    [Fact(Skip = "LibGit2Sharp 0.32.0 Branch/Ref API-Inkonsistenz (Branch.TargetIdentifier / Refs.Add ObjectId)")]
    public void Diagnose_WhatCommittedToTree_AndWhereIsHead()
    {
        var sig = new Signature("Tester", "t@e.com", DateTimeOffset.Now);

        var path1 = Path.Combine(_repoPath, "processes", "Old.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path1)!);
        File.WriteAllText(path1, "<process name=\"Old\"/>");

        _out.WriteLine($"WorkingDirectory = {_repo.Info.WorkingDirectory}");
        _out.WriteLine($"Head.Tip.Sha (initial) = {_repo.Head.Tip?.Sha ?? "(null)"}");
        _out.WriteLine($"Refs.Count (initial) = {_repo.Refs.Count()}");

        _repo.Index.Add("processes/Old.xml");
        _repo.Index.Write();

        _out.WriteLine($"Index.Count (after Add+Write) = {_repo.Index.Count}");

        var c1 = _repo.Commit("initial", sig, sig);
        _out.WriteLine($"c1.Sha = {c1.Sha}");
        _out.WriteLine($"c1.Tree.Count (after Commit) = {c1.Tree.Count()}");
        foreach (var e in c1.Tree) _out.WriteLine($"  tree entry: {e.Path} mode={e.Mode}");

        _out.WriteLine($"Head.Tip.Sha (after Commit) = {_repo.Head.Tip?.Sha ?? "(null)"}");
        _out.WriteLine($"Refs.Count (after Commit) = {_repo.Refs.Count()}");
        foreach (var r in _repo.Refs) _out.WriteLine($"  ref: {r.CanonicalName} -> {r.TargetIdentifier}");
    }
}
