using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BPGit.Data;
using BPGit.Data.Models;
using BPGit.Server.GitHttp;
using BPGit.Server.Services;
using LibGit2Sharp;
using Microsoft.AspNetCore.Http;
using Xunit;
using BpTree = BPGit.Data.Models.Tree;
using GitTree = LibGit2Sharp.Tree;

namespace BPGit.Server.Tests;

public sealed class BpToGitSyncServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"bpgit-sync-tests-{Guid.NewGuid():N}");
    private readonly string _repoPath;
    private readonly string _processDirectory;

    public BpToGitSyncServiceTests()
    {
        _repoPath = Path.Combine(_root, "repo.git");
        _processDirectory = Path.Combine(_root, "processes");
        Directory.CreateDirectory(_root);
        InitializeBareRepository(_repoPath);
    }

    [Fact]
    public async Task SyncBeforeFetchAsync_CommitsDatabaseChangesAndAvoidsEmptyCommits()
    {
        var process = new BpProcessRow(
            Guid.NewGuid(),
            "Example",
            "<process name=\"Example\" version=\"1\"/>");
        var folders = SingleProcessFolder(process);
        var operationLock = new RepositoryOperationLock();
        var firstSync = CreateService(new FakeBpDbService(new[] { process }, folders), operationLock);
        SetHeadDirect(_repoPath);

        Assert.True(await firstSync.SyncBeforeFetchAsync(_repoPath, CancellationToken.None));
        using (var repo = new Repository(_repoPath))
        {
            Assert.IsType<SymbolicReference>(repo.Refs["HEAD"]);
        }
        var firstCommit = ReadHead(_repoPath);
        Assert.Equal("bpgit-server", firstCommit.AuthorName);
        Assert.Equal("bpgit: sync from Blue Prism database", firstCommit.Message);
        Assert.Equal("<process name=\"Example\" version=\"1\"/>", firstCommit.Xml);

        await File.WriteAllTextAsync(Path.Combine(_processDirectory, "notes.txt"), "not part of the repository");
        Assert.False(await firstSync.SyncBeforeFetchAsync(_repoPath, CancellationToken.None));
        Assert.Equal(firstCommit.Id, ReadHead(_repoPath).Id);

        var updatedProcess = process with { XmlContent = "<process name=\"Example\" version=\"2\"/>" };
        var updatedSync = CreateService(
            new FakeBpDbService(new[] { updatedProcess }, SingleProcessFolder(updatedProcess)),
            operationLock);

        Assert.True(await updatedSync.SyncBeforeFetchAsync(_repoPath, CancellationToken.None));
        var updatedCommit = ReadHead(_repoPath);
        Assert.NotEqual(firstCommit.Id, updatedCommit.Id);
        Assert.Equal("<process name=\"Example\" version=\"2\"/>", updatedCommit.Xml);
    }

    [Fact]
    public async Task SyncBeforeFetchAsync_WithNoProcessesDoesNotCreateAnEmptyCommit()
    {
        var db = new FakeBpDbService(
            Array.Empty<BpProcessRow>(),
            new FolderStructure(
                new List<BpTree>(),
                new List<Group>(),
                new List<ProcessMembership>()));
        var service = CreateService(db, new RepositoryOperationLock());
        var initialHead = ReadHead(_repoPath);

        Assert.False(await service.SyncBeforeFetchAsync(_repoPath, CancellationToken.None));
        Assert.Equal(initialHead.Id, ReadHead(_repoPath).Id);
    }

    [Fact]
    public async Task UploadPackDiscovery_SynchronizesDatabaseBeforeAdvertisingRefs()
    {
        var process = new BpProcessRow(
            Guid.NewGuid(),
            "Example",
            "<process name=\"Example\"/>");
        var db = new FakeBpDbService(new[] { process }, SingleProcessFolder(process));
        var config = new ServerConfig
        {
            RepoRoot = _root,
            RepoName = "repo",
            WorktreeDir = _processDirectory,
        };
        var operationLock = new RepositoryOperationLock();
        var sync = new BpToGitSyncService(new WorktreeSyncService(db), config, operationLock);
        var push = new PushOrchestrator(
            new PreReceiveHandler(new FakeBpSyncService()),
            new PostReceiveHandler(new WorktreeSyncService(db)),
            sync,
            config,
            operationLock);
        var context = new DefaultHttpContext();
        context.Request.Path = "/repo/info/refs";
        context.Request.QueryString = new QueryString("?service=git-upload-pack");
        using var response = new MemoryStream();
        context.Response.Body = response;

        Assert.True(await GitHttpHandler.HandleAsync(context, config, push, sync));

        var advertisedRefs = Encoding.ASCII.GetString(response.ToArray());
        Assert.Contains(ReadHead(_repoPath).Id, advertisedRefs);
        Assert.Contains("refs/heads/main", advertisedRefs);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(_root, recursive: true);
        }
    }

    private BpToGitSyncService CreateService(FakeBpDbService db, RepositoryOperationLock operationLock)
    {
        var config = new ServerConfig { WorktreeDir = _processDirectory };
        return new BpToGitSyncService(new WorktreeSyncService(db), config, operationLock);
    }

    private static FolderStructure SingleProcessFolder(BpProcessRow process)
    {
        var tree = new BpTree { Id = 2, Name = "Processes" };
        var group = new Group { Id = Guid.NewGuid(), TreeId = tree.Id, Name = "Default" };
        return new FolderStructure(
            new List<BpTree> { tree },
            new List<Group> { group },
            new List<ProcessMembership>
            {
                new() { GroupId = group.Id, ProcessId = process.ProcessId }
            });
    }

    private static void InitializeBareRepository(string repoPath)
    {
        Repository.Init(repoPath, isBare: true);
        using var repo = new Repository(repoPath);
        using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("*\n!.gitignore\n"));
        var blob = repo.ObjectDatabase.CreateBlob(content);
        var treeDefinition = new TreeDefinition();
        treeDefinition.Add(".gitignore", blob.Id, Mode.NonExecutableFile);
        var tree = repo.ObjectDatabase.CreateTree(treeDefinition);
        var author = new Signature("test", "test@localhost", DateTimeOffset.UtcNow);
        var initialCommit = repo.ObjectDatabase.CreateCommit(
            author,
            author,
            "initial",
            tree,
            Array.Empty<Commit>(),
            false);
        repo.Refs.Add("refs/heads/main", initialCommit.Id);
        repo.Refs.Add("HEAD", "refs/heads/main", allowOverwrite: true);
    }

    private static void SetHeadDirect(string repoPath)
    {
        using var repo = new Repository(repoPath);
        var main = repo.Refs["refs/heads/main"];
        repo.Refs.UpdateTarget(repo.Refs["HEAD"], main.TargetIdentifier);
    }

    private static CommitSnapshot ReadHead(string repoPath)
    {
        using var repo = new Repository(repoPath);
        var commit = repo.Head.Tip ?? throw new InvalidOperationException("Bare repository has no HEAD commit.");
        return new CommitSnapshot(
            commit.Id.Sha,
            commit.Author.Name,
            commit.MessageShort,
            ReadProcessXml(commit));
    }

    private static string? ReadProcessXml(Commit commit)
    {
        var processesEntry = commit.Tree["processes"];
        if (processesEntry is null)
        {
            return null;
        }

        var processes = Assert.IsType<GitTree>(processesEntry.Target);
        var processTree = Assert.IsType<GitTree>(processes["Processes"].Target);
        var group = Assert.IsType<GitTree>(processTree["Default"].Target);
        var xml = Assert.IsType<Blob>(group["Example.xml"].Target);
        return xml.GetContentText();
    }

    private sealed record CommitSnapshot(string Id, string AuthorName, string Message, string? Xml);
}
