using System;
using System.IO;
using BPGit.Cli.Worktree;
using Xunit;

namespace BPGit.Cli.Tests;

public class SnapshotStoreTests : IDisposable
{
    private readonly string _workdir;
    private const string SnapshotFileName = "bpgit-snapshot.json";

    public SnapshotStoreTests()
    {
        _workdir = Path.Combine(
            Path.GetTempPath(),
            "bpgit-snapshotstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workdir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workdir))
            Directory.Delete(_workdir, recursive: true);
    }

    [Fact]
    public void ComputeHash_SameInput_ReturnsSameHash()
    {
        Assert.Equal(
            SnapshotStore.ComputeHash("hello"),
            SnapshotStore.ComputeHash("hello"));
    }

    [Fact]
    public void ComputeHash_ReturnsLowercaseSha256HexWithPrefix()
    {
        var hash = SnapshotStore.ComputeHash("hello");
        Assert.StartsWith("sha256:", hash);
        var hex = hash["sha256:".Length..];
        Assert.Equal(64, hex.Length);
        Assert.Matches("^[0-9a-f]{64}$", hex);
    }

    [Fact]
    public void ComputeHash_DifferentInputs_ProduceDifferentHashes()
    {
        Assert.NotEqual(
            SnapshotStore.ComputeHash("hello"),
            SnapshotStore.ComputeHash("world"));
    }

    [Fact]
    public void Snapshot_DefaultVersion_Is2()
    {
        var s = new Snapshot();
        Assert.Equal(2, s.Version);
    }

    [Fact]
    public void Save_CreatesSnapshotFileInWorkdir()
    {
        var snap = new Snapshot
        {
            Processes = { ["foo"] = new SnapshotEntry { Name = "foo", Hash = "sha256:abc", Type = "O", Path = "Objects/Default/foo.xml" } }
        };
        SnapshotStore.Save(_workdir, SnapshotFileName, snap);

        var path = Path.Combine(_workdir, SnapshotFileName);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Save_WritesIndentedJson()
    {
        var snap = new Snapshot
        {
            Processes = { ["foo"] = new SnapshotEntry { Name = "foo" } }
        };
        SnapshotStore.Save(_workdir, SnapshotFileName, snap);
        var content = File.ReadAllText(Path.Combine(_workdir, SnapshotFileName));
        Assert.Contains("\n", content);
        Assert.Contains("  ", content);
    }

    [Fact]
    public void Save_PreservesVersion2PathFieldOnEntry()
    {
        var snap = new Snapshot
        {
            Processes =
            {
                ["MP - Subprocess A"] = new SnapshotEntry
                {
                    Hash = "sha256:abc",
                    Name = "MP - Subprocess A",
                    Type = "P",
                    Path = "Processes/Default/MP - Subprocess A.xml"
                }
            }
        };
        SnapshotStore.Save(_workdir, SnapshotFileName, snap);
        var loaded = SnapshotStore.Load(_workdir, SnapshotFileName);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Version);
        Assert.Single(loaded.Processes);
        Assert.Equal("Processes/Default/MP - Subprocess A.xml",
            loaded.Processes["MP - Subprocess A"].Path);
    }

    [Fact]
    public void Load_ReturnsNullWhenFileDoesNotExist()
    {
        Assert.Null(SnapshotStore.Load(_workdir, SnapshotFileName));
    }

    [Fact]
    public void Load_RoundTripsFullSnapshot()
    {
        var original = new Snapshot
        {
            ExtractedAt = DateTime.UtcNow,
            Processes =
            {
                ["a"] = new SnapshotEntry { Name = "A", Hash = "sha256:111", Type = "O", Path = "x/a.xml" },
                ["b"] = new SnapshotEntry { Name = "B", Hash = "sha256:222", Type = "P", Path = "x/b.xml" },
            }
        };
        SnapshotStore.Save(_workdir, SnapshotFileName, original);

        var loaded = SnapshotStore.Load(_workdir, SnapshotFileName);

        Assert.NotNull(loaded);
        Assert.Equal(original.Version, loaded!.Version);
        Assert.Equal(2, loaded.Processes.Count);
        Assert.Equal("x/a.xml", loaded.Processes["a"].Path);
        Assert.Equal("x/b.xml", loaded.Processes["b"].Path);
    }

    [Fact]
    public void Save_RespectsCustomSnapshotFileName()
    {
        var snapshotFileName = "my-snapshot.json";
        var snap = new Snapshot { Processes = { ["foo"] = new SnapshotEntry { Name = "foo" } } };
        SnapshotStore.Save(_workdir, snapshotFileName, snap);

        Assert.True(File.Exists(Path.Combine(_workdir, snapshotFileName)));
        Assert.False(File.Exists(Path.Combine(_workdir, SnapshotFileName)));
    }
}
