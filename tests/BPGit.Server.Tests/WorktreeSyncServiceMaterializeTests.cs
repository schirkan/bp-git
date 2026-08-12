using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BPGit.Data.Models;
using BPGit.Server.Services;
using Xunit;

namespace BPGit.Server.Tests;

public class WorktreeSyncServiceMaterializeTests : IDisposable
{
    private readonly string _worktreeRoot;

    public WorktreeSyncServiceMaterializeTests()
    {
        _worktreeRoot = Path.Combine(
            Path.GetTempPath(),
            "bpgit-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_worktreeRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_worktreeRoot))
            Directory.Delete(_worktreeRoot, recursive: true);
    }

    private static WorktreeSyncService BuildService(
        IReadOnlyList<BpProcessRow> processes,
        FolderStructure folders)
        => new WorktreeSyncService(new FakeBpDbService(processes, folders));

    private static FolderStructure EmptyFolders() => new(
        new List<Tree>(), new List<Group>(), new List<ProcessMembership>());

    private static FolderStructure SingleGroupFolder(BpProcessRow proc, int treeId = 2, string treeName = "Processes", string groupName = "Default")
    {
        var tree = new Tree { Id = treeId, Name = treeName };
        var group = new Group { Id = Guid.NewGuid(), TreeId = treeId, Name = groupName };
        var membership = new ProcessMembership { GroupId = group.Id, ProcessId = proc.ProcessId };
        return new FolderStructure(
            new List<Tree> { tree },
            new List<Group> { group },
            new List<ProcessMembership> { membership });
    }

    [Fact]
    public async Task MaterializeAsync_WritesCanonicalXml_ForSingleProcess()
    {
        var proc = new BpProcessRow(
            ProcessId: Guid.NewGuid(),
            Name: "My Process",
            XmlContent: "<process name=\"My Process\"/>");
        var svc = BuildService(new[] { proc }, SingleGroupFolder(proc));

        var result = await svc.MaterializeAsync(_worktreeRoot);

        Assert.Equal(1, result.Written);
        Assert.Equal(0, result.Deleted);
        var expected = Path.Combine(_worktreeRoot, "Processes", "Default", "My Process.xml");
        Assert.True(File.Exists(expected));
        Assert.Equal("<process name=\"My Process\"/>", await File.ReadAllTextAsync(expected));
    }

    [Fact]
    public async Task MaterializeAsync_DuplicatesFile_AcrossMultipleGroups()
    {
        var proc = new BpProcessRow(
            ProcessId: Guid.NewGuid(),
            Name: "Shared",
            XmlContent: "<process name=\"Shared\"/>");
        var tree = new Tree { Id = 3, Name = "Objects" };
        var g1 = new Group { Id = Guid.NewGuid(), TreeId = 3, Name = "Alpha" };
        var g2 = new Group { Id = Guid.NewGuid(), TreeId = 3, Name = "Beta" };
        var folders = new FolderStructure(
            new List<Tree> { tree },
            new List<Group> { g1, g2 },
            new List<ProcessMembership>
            {
                new ProcessMembership { GroupId = g1.Id, ProcessId = proc.ProcessId },
                new ProcessMembership { GroupId = g2.Id, ProcessId = proc.ProcessId },
            });

        var svc = BuildService(new[] { proc }, folders);
        var result = await svc.MaterializeAsync(_worktreeRoot);

        Assert.Equal(2, result.Written);
        Assert.True(File.Exists(Path.Combine(_worktreeRoot, "Objects", "Alpha", "Shared.xml")));
        Assert.True(File.Exists(Path.Combine(_worktreeRoot, "Objects", "Beta", "Shared.xml")));
    }

    [Fact]
    public async Task MaterializeAsync_DeletesStaleFiles()
    {
        var staleDir = Path.Combine(_worktreeRoot, "Processes", "Default");
        Directory.CreateDirectory(staleDir);
        var staleFile = Path.Combine(staleDir, "OldProcess.xml");
        await File.WriteAllTextAsync(staleFile, "<process name=\"OldProcess\"/>");

        var proc = new BpProcessRow(
            ProcessId: Guid.NewGuid(),
            Name: "NewProcess",
            XmlContent: "<process name=\"NewProcess\"/>");
        var svc = BuildService(new[] { proc }, SingleGroupFolder(proc));

        var result = await svc.MaterializeAsync(_worktreeRoot);

        Assert.Equal(1, result.Written);
        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(staleFile));
        Assert.True(File.Exists(Path.Combine(staleDir, "NewProcess.xml")));
    }

    [Fact]
    public async Task MaterializeAsync_SkipsProcessWithNoGroupMembership()
    {
        var orphan = new BpProcessRow(
            ProcessId: Guid.NewGuid(),
            Name: "Orphan",
            XmlContent: "<process name=\"Orphan\"/>");
        var svc = BuildService(new[] { orphan }, EmptyFolders());

        var result = await svc.MaterializeAsync(_worktreeRoot);

        Assert.Equal(0, result.Written);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(Directory.EnumerateFiles(_worktreeRoot, "*.xml", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task MaterializeAsync_SecondRunWithIdenticalContent_WritesNothing()
    {
        var proc = new BpProcessRow(
            ProcessId: Guid.NewGuid(),
            Name: "Stable",
            XmlContent: "<process name=\"Stable\"/>");
        var svc = BuildService(new[] { proc }, SingleGroupFolder(proc));

        var r1 = await svc.MaterializeAsync(_worktreeRoot);
        var r2 = await svc.MaterializeAsync(_worktreeRoot);

        Assert.Equal(1, r1.Written);
        Assert.Equal(0, r2.Written);
        Assert.Equal(0, r2.Deleted);
    }

    [Fact]
    public async Task MaterializeAsync_SanitizesFilenameForInvalidChars()
    {
        var proc = new BpProcessRow(
            ProcessId: Guid.NewGuid(),
            Name: "Email - POP3/SMTP/IMAP",
            XmlContent: "<process name=\"Email - POP3/SMTP/IMAP\"/>");
        var svc = BuildService(new[] { proc }, SingleGroupFolder(proc));

        var result = await svc.MaterializeAsync(_worktreeRoot);

        Assert.Equal(1, result.Written);
        var expected = Path.Combine(_worktreeRoot, "Processes", "Default", "Email - POP3_SMTP_IMAP.xml");
        Assert.True(File.Exists(expected));
        Assert.False(File.Exists(Path.Combine(_worktreeRoot, "Processes", "Default", "Email - POP3/SMTP/IMAP.xml")));
    }
}
