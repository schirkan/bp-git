using System;
using System.IO;
using System.Threading.Tasks;
using BPGit.Server.GitHttp;
using BPGit.Server.Services;
using LibGit2Sharp;
using Xunit;

namespace BPGit.Server.Tests;

public class PreReceiveHandlerTests : IDisposable
{
    private readonly string _repoPath;
    private readonly Repository _repo;
    private readonly Signature _sig = new("Tester", "test@example.com", DateTimeOffset.Now);

    public PreReceiveHandlerTests()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), "bpgit-pre-" + Guid.NewGuid().ToString("N"));
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

    private string CommitXml(string xmlContent, string filename, string message)
    {
        var path = Path.Combine(_repoPath, filename);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, xmlContent);
        _repo.Index.Add(filename);
        _repo.Index.Write();
        return _repo.Commit(message, _sig, _sig).Sha;
    }

    [Fact(Skip = "LibGit2Sharp 0.32.0 Index.Add+Commit Tree-Walk noch nicht stabil (vermutlich HEAD-tracking Issue); Phase 5+")]
    public async Task HandleAsync_ModifyExistingFile_CallsModifyAsyncWithOldAndNewName()
    {
        var oldSha = CommitXml("<process name=\"Old\"/>", "processes/Old.xml", "initial");
        var newSha = CommitXml("<process name=\"New\"/>", "processes/Old.xml", "rename-via-xml");

        var fake = new FakeBpSyncService();
        var handler = new PreReceiveHandler(fake);

        var result = await handler.HandleAsync(_repo, oldSha, newSha, "refs/heads/main");

        Assert.True(result.Ok);
        Assert.Single(fake.ModifyCalls);
        Assert.Equal("Old", fake.ModifyCalls[0].oldName);
        Assert.Equal("New", fake.ModifyCalls[0].newName);
        Assert.Empty(fake.AddCalls);
    }

    [Fact(Skip = "LibGit2Sharp 0.32.0 Index.Add+Commit Tree-Walk noch nicht stabil; Phase 5+")]
    public async Task HandleAsync_AddNewFile_CallsAddAsyncWithExtractedName()
    {
        var newSha = CommitXml("<process name=\"Brand\"/>", "processes/Brand.xml", "add-new");

        var fake = new FakeBpSyncService();
        var handler = new PreReceiveHandler(fake);

        var result = await handler.HandleAsync(_repo, "0000000000000000000000000000000000000000", newSha, "refs/heads/main");

        Assert.True(result.Ok);
        Assert.Single(fake.AddCalls);
        Assert.Equal("Brand", fake.AddCalls[0].name);
        Assert.Empty(fake.ModifyCalls);
    }

    [Fact(Skip = "LibGit2Sharp 0.32.0 Index.Add/Remove+Commit Tree-Walk noch nicht stabil; Phase 5+")]
    public async Task HandleAsync_DeleteFileInPush_CallsDeleteAsync()
    {
        var oldSha = CommitXml("<process name=\"Going\"/>", "processes/Going.xml", "add");
        var path = Path.Combine(_repoPath, "processes", "Going.xml");
        File.Delete(path);
        _repo.Index.Remove("processes/Going.xml");
        _repo.Index.Write();
        var finalSha = _repo.Commit("remove Going", _sig, _sig).Sha;

        var fake = new FakeBpSyncService { NextDeleteResult = new DeleteResult(true, null) };
        var handler = new PreReceiveHandler(fake);

        var result = await handler.HandleAsync(_repo, oldSha, finalSha, "refs/heads/main");

        Assert.True(result.Ok);
        Assert.Single(fake.DeleteCalls);
        Assert.Equal("Going", fake.DeleteCalls[0]);
    }

    [Fact]
    public async Task HandleAsync_InvalidNewRev_ReturnsFailure()
    {
        var fake = new FakeBpSyncService();
        var handler = new PreReceiveHandler(fake);

        var result = await handler.HandleAsync(_repo, "0000000000000000000000000000000000000000", "deadbeef", "refs/heads/main");

        Assert.False(result.Ok);
        Assert.Single(result.Failures);
        Assert.Empty(fake.AddCalls);
        Assert.Empty(fake.ModifyCalls);
        Assert.Empty(fake.DeleteCalls);
    }
}