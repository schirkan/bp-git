using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BPGit.Server.GitHttp;
using LibGit2Sharp;
using Xunit;

namespace BPGit.Server.Tests;

/// <summary>
/// Empirical coverage for <see cref="PreReceiveHandler.WalkTreeEntries"/>: verifies
/// that the recursive walk correctly handles arbitrary nesting depth (the production
/// layout is up to 3 levels: processes/&lt;group&gt;/&lt;process&gt;.xml, but the
/// walker must support deeper paths in case BP folder hierarchies go that deep).
/// </summary>
public class WalkTreeEntriesTests : IDisposable
{
    private readonly string _repoPath;
    private readonly Repository _repo;

    public WalkTreeEntriesTests()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), "bpgit-walktree-" + Guid.NewGuid().ToString("N"));
        Repository.Init(_repoPath);
        _repo = new Repository(_repoPath);
    }

    public void Dispose()
    {
        try { _repo?.Dispose(); } catch { }
        GC.Collect();
        GC.WaitForPendingFinalizers();
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

    private Tree BuildTreeWithBlobs(params (string path, string content)[] files)
    {
        var td = new TreeDefinition();
        foreach (var (path, content) in files)
        {
            var blob = _repo.ObjectDatabase.CreateBlob(
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)));
            td.Add(path, blob, Mode.NonExecutableFile);
        }
        return _repo.ObjectDatabase.CreateTree(td);
    }

    [Fact]
    public void WalkTreeEntries_FlatFiles_ReturnsAllPaths()
    {
        var tree = BuildTreeWithBlobs(
            ("a.xml", "<process name=\"a\"/>"),
            ("b.xml", "<process name=\"b\"/>"));

        var result = PreReceiveHandler.WalkTreeEntries(tree, "");

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("a.xml"));
        Assert.True(result.ContainsKey("b.xml"));
    }

    [Fact]
    public void WalkTreeEntries_OneLevelNested_ReturnsFullPaths()
    {
        var tree = BuildTreeWithBlobs(
            ("processes/old.xml", "<process name=\"old\"/>"),
            ("processes/new.xml", "<process name=\"new\"/>"));

        var result = PreReceiveHandler.WalkTreeEntries(tree, "");

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("processes/old.xml"));
        Assert.True(result.ContainsKey("processes/new.xml"));
    }

    [Fact]
    public void WalkTreeEntries_TwoLevelsNested_ReturnsFullPaths()
    {
        var tree = BuildTreeWithBlobs(
            ("processes/GroupA/ProcessA.xml", "<process name=\"A\"/>"),
            ("processes/GroupA/ProcessB.xml", "<process name=\"B\"/>"),
            ("processes/GroupB/ProcessC.xml", "<object name=\"C\"/>"));

        var result = PreReceiveHandler.WalkTreeEntries(tree, "");

        Assert.Equal(3, result.Count);
        Assert.True(result.ContainsKey("processes/GroupA/ProcessA.xml"));
        Assert.True(result.ContainsKey("processes/GroupA/ProcessB.xml"));
        Assert.True(result.ContainsKey("processes/GroupB/ProcessC.xml"));

        // Sub-trees themselves are NOT in the result.
        Assert.False(result.ContainsKey("processes"));
        Assert.False(result.ContainsKey("processes/GroupA"));
        Assert.False(result.ContainsKey("processes/GroupB"));
    }

    [Fact]
    public void WalkTreeEntries_DeeplyNested_ReturnsAllFilesWithFullPaths()
    {
        // 5 levels deep + sibling at 2 levels + flat root file.
        var tree = BuildTreeWithBlobs(
            ("a/b/c/d/e/DeepFile.xml", "<process name=\"Deep\"/>"),
            ("processes/Objects/SubGroup/Other/More/Nested.xml", "<process name=\"Nested\"/>"),
            ("processes/Objects/Top.xml", "<object name=\"Top\"/>"),
            ("top.xml", "<process name=\"TopRoot\"/>"));

        var result = PreReceiveHandler.WalkTreeEntries(tree, "");

        Assert.Equal(4, result.Count);
        Assert.True(result.ContainsKey("a/b/c/d/e/DeepFile.xml"));
        Assert.True(result.ContainsKey("processes/Objects/SubGroup/Other/More/Nested.xml"));
        Assert.True(result.ContainsKey("processes/Objects/Top.xml"));
        Assert.True(result.ContainsKey("top.xml"));

        // Sub-trees themselves are NOT in the result.
        Assert.False(result.ContainsKey("a"));
        Assert.False(result.ContainsKey("a/b"));
        Assert.False(result.ContainsKey("a/b/c/d/e"));
        Assert.False(result.ContainsKey("processes/Objects/SubGroup/Other/More"));
    }

    [Fact]
    public void WalkTreeEntries_EmptyTree_ReturnsEmptyDict()
    {
        var tree = _repo.ObjectDatabase.CreateTree(new TreeDefinition());

        var result = PreReceiveHandler.WalkTreeEntries(tree, "");

        Assert.Empty(result);
    }

    [Fact]
    public void WalkTreeEntries_PrefixPropagatesThroughRecursion()
    {
        var tree = BuildTreeWithBlobs(
            ("sub/x.xml", "<process name=\"x\"/>"));

        var result = PreReceiveHandler.WalkTreeEntries(tree, "processes");

        Assert.Single(result);
        Assert.True(result.ContainsKey("processes/sub/x.xml"));
    }
}
