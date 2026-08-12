using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BPGit.Data.Models;
using BPGit.Server.Services;
using Xunit;

namespace BPGit.Server.Tests;

public class BpSyncServiceTests
{
    private static FakeBpDbService NewDb() =>
        new(
            Array.Empty<BpProcessRow>(),
            new FolderStructure(new List<Tree>(), new List<Group>(), new List<ProcessMembership>()));

    private static BpSyncService NewService(FakeBpDbService db) => new(db);

    private static BpaProcessLockInfo SampleLock() =>
        new(
            LockDateTime: DateTime.UtcNow,
            UserId: Guid.NewGuid(),
            MachineName: "test-machine",
            Username: "test-user");

    [Fact]
    public async Task ModifyAsync_ReturnsFailure_WhenProcessNotFound()
    {
        var db = NewDb();
        var svc = NewService(db);

        var result = await svc.ModifyAsync("<process name=\"X\"/>", "Old", "New");

        Assert.False(result.Ok);
        Assert.Null(result.ProcessId);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task ModifyAsync_ReturnsFailure_WhenLocked()
    {
        var db = NewDb();
        var pid = Guid.NewGuid();
        db.NameToProcessId["Old"] = pid;
        db.Locks[pid] = SampleLock();
        var svc = NewService(db);

        var result = await svc.ModifyAsync("<process name=\"New\"/>", "Old", "New");

        Assert.False(result.Ok);
        Assert.Null(result.ProcessId);
        Assert.Contains("locked by", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddAsync_ReturnsFailure_WhenNameAlreadyExists()
    {
        var db = NewDb();
        db.NameToProcessId["Existing"] = Guid.NewGuid();
        var svc = NewService(db);

        var result = await svc.AddAsync("<process name=\"Existing\"/>", "Existing");

        Assert.False(result.Ok);
        Assert.NotNull(result.Message);
        Assert.Contains("already exists", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsNotFound_WhenProcessNotFound()
    {
        var db = NewDb();
        var svc = NewService(db);

        var result = await svc.DeleteAsync("Nonexistent");

        Assert.False(result.Ok);
        Assert.True(result.IsNotFound);
        Assert.False(result.IsLocked);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsLocked_WhenProcessLocked()
    {
        var db = NewDb();
        var pid = Guid.NewGuid();
        db.NameToProcessId["Locked"] = pid;
        db.Locks[pid] = SampleLock();
        var svc = NewService(db);

        var result = await svc.DeleteAsync("Locked");

        Assert.False(result.Ok);
        Assert.True(result.IsLocked);
        Assert.False(result.IsNotFound);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsNotImplemented_WhenProcessExistsAndNotLocked()
    {
        var db = NewDb();
        db.NameToProcessId["ToDelete"] = Guid.NewGuid();
        var svc = NewService(db);

        var result = await svc.DeleteAsync("ToDelete");

        Assert.False(result.Ok);
        Assert.False(result.IsLocked);
        Assert.False(result.IsNotFound);
    }
}
