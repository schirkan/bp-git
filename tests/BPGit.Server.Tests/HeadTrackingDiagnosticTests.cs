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
        _repo?.Dispose();
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

    [Fact]
    public void Diagnose_WhatCommittedToTree_AndWhereIsHead()
    {
        var sig = new Signature("Tester", "t@e.com", DateTimeOffset.Now);

        // Phase 1: write + index add + commit
        var path1 = Path.Combine(_repoPath, "processes", "Old.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path1)!);
        File.WriteAllText(path1, "<process name=\"Old\"/>");

        _out.WriteLine($"WorkingDirectory = {_repo.Info.WorkingDirectory}");
        _out.WriteLine($"Head.Tip = {_repo.Head.Tip?.Sha ?? \"(null)\"}");
        _out.WriteLine($"Head.TargetIdentifier = {_repo.Head.TargetIdentifier}");
        _out.WriteLine($"Head.IsCurrentRepositoryHead = {_repo.Head.IsCurrentRepositoryHead}");
        _out.WriteLine($"Refs (count) = {_repo.Refs.Count()}");

        _repo.Index.Add("processes/Old.xml");
        _repo.Index.Write();

        _out.WriteLine($"Index.Count = {_repo.Index.Count}");
        _out.WriteLine($"Index [0] = {_repo.Index[0].Path} {(_repo.Index[0].Mode == Mode.NonExecutableFile ? \"file\" : _repo.Index[0].Mode.ToString())}");

        var c1 = _repo.Commit("initial", sig, sig);
        _out.WriteLine($"c1.Sha = {c1.Sha}");
        _out.WriteLine($"c1.Tree.Count = {c1.Tree.Count()}");
        foreach (var e in c1.Tree) _out.WriteLine($"  tree entry: {e.Path} mode={e.Mode}");

        var refs = _repo.Refs.Where(r => r.CanonicalName.Contains("main") || r.CanonicalName.Contains("master") || r.CanonicalName == "HEAD");
        foreach (var r in refs) _out.WriteLine($"  ref: {r.CanonicalName} -> {r.TargetIdentifier}");

        // Refs manuell anlegen falls fehlt
        if (!refs.Any(r => r.CanonicalName == "refs/heads/main"))
        {
            _repo.Refs.Add("refs/heads/main", c1.Sha);
            _out.WriteLine($"manually added refs/heads/main -> {c1.Sha}");
        }
    }

    [Fact]
    public void Diagnose_DoubleCommitAndDiff_TreeContents()
    {
        var sig = new Signature("Tester", "t@e.com", DateTimeOffset.Now);

        File.WriteAllText(Path.Combine(_repoPath, "x.xml"), "<process name=\"X\"/>");
        _repo.Index.Add("x.xml");
        _repo.Index.Write();
        var c1 = _repo.Commit("first", sig, sig);
        // Refs manuell anlegen
        if (!_repo.Branches.Any(b => b.FriendlyName == "main"))
        {
            _repo.Refs.Add("refs/heads/main", c1.Sha);
        }

        // Modify
        File.WriteAllText(Path.Combine(_repoPath, "x.xml"), "<process name=\"Y\"/>");
        _repo.Index.Add("x.xml");
        _repo.Index.Write();
        var c2 = _repo.Commit("second", sig, sig);
        _repo.Refs.UpdateTarget("refs/heads/main", c2.Sha);

        // Inspect tree via c1 vs c2 diff
        _out.WriteLine($"c1.Tree.Count = {c1.Tree.Count()}");
        foreach (var e in c1.Tree) _out.WriteLine($"  c1 tree: {e.Path}");

        _out.WriteLine($"c2.Tree.Count = {c2.Tree.Count()}");
        foreach (var e in c2.Tree) _out.WriteLine($"  c2 tree: {e.Path}");

        var headSha = _repo.Refs["refs/heads/main"].Resolve().Sha;
        _out.WriteLine($"refs/heads/main resolves to {headSha}");
        var headCommit = _repo.Lookup<Commit>(headSha);
        _out.WriteLine($"HEAD commit Tree.Count = {headCommit.Tree.Count()}");
    }
}