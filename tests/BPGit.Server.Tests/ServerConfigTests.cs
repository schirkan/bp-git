using System;
using System.IO;
using BPGit.Data;
using Xunit;

namespace BPGit.Server.Tests;

public class ServerConfigTests : IDisposable
{
    private readonly string _tempConfigPath;

    public ServerConfigTests()
    {
        _tempConfigPath = Path.Combine(
            Path.GetTempPath(),
            $"bpgit-server-config-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempConfigPath))
            File.Delete(_tempConfigPath);
    }

    [Fact]
    public void Load_FromExplicitPath_ParsesSqlPassword()
    {
        File.WriteAllText(_tempConfigPath, @"{ ""sqlPassword"": ""geheim"" }");

        var cfg = ServerConfig.Load(_tempConfigPath);

        Assert.Equal("geheim", cfg.SqlPassword);
    }

    [Fact]
    public void Load_FromExplicitPath_ParsesListenUrlsAndSqlAuth()
    {
        File.WriteAllText(_tempConfigPath, @"{
            ""listenUrls"": [""http://10.0.0.1:9999""],
            ""sqlAuth"": ""user"",
            ""sqlUser"": ""myuser"",
            ""sqlServer"": ""MYHOST\\SQLEXPRESS"",
            ""sqlDatabase"": ""bpdb"",
            ""repoRoot"": ""C:\\bpgit\\data"",
            ""repoName"": ""myrepo""
        }");

        var cfg = ServerConfig.Load(_tempConfigPath);

        Assert.Single(cfg.ListenUrls);
        Assert.Equal("http://10.0.0.1:9999", cfg.ListenUrls[0]);
        Assert.Equal("user", cfg.SqlAuth);
        Assert.Equal("myuser", cfg.SqlUser);
        Assert.Equal(@"MYHOST\SQLEXPRESS", cfg.SqlServer);
        Assert.Equal("bpdb", cfg.SqlDatabase);
        Assert.Equal(@"C:\bpgit\data", cfg.RepoRoot);
        Assert.Equal("myrepo", cfg.RepoName);
    }

    [Fact]
    public void Load_NonExistentFile_UsesDefaults()
    {
        var cfg = ServerConfig.Load(_tempConfigPath);

        Assert.Equal("sso", cfg.SqlAuth);
        Assert.NotEmpty(cfg.ListenUrls);
        Assert.Null(cfg.SqlUser);
        Assert.Null(cfg.SqlPassword);
    }

    [Fact]
    public void BareRepoPath_IsDerivedFromRepoRootAndRepoName()
    {
        File.WriteAllText(_tempConfigPath, @"{ ""repoRoot"": ""C:\\x"", ""repoName"": ""foo"" }");

        var cfg = ServerConfig.Load(_tempConfigPath);

        Assert.Equal(@"C:\x\foo.git", cfg.BareRepoPath);
    }

    [Fact]
    public void Load_NullPath_UsesDefaultLocationAndReturnsDefaultsWhenMissing()
    {
        // Pass a path that does not exist by pointing into a non-existent dir under temp.
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            $"bpgit-missing-{Guid.NewGuid():N}.json");

        var cfg = ServerConfig.Load(missingPath);

        // Missing file -> defaults retained.
        Assert.Equal("sso", cfg.SqlAuth);
        Assert.Equal(".\\repos", cfg.RepoRoot);
        Assert.Equal("bp-git", cfg.RepoName);
    }

    [Fact]
    public void WorktreePath_IsAbsoluteAndResolved()
    {
        File.WriteAllText(_tempConfigPath, @"{ ""worktreeDir"": ""./processes"" }");

        var cfg = ServerConfig.Load(_tempConfigPath);

        Assert.True(Path.IsPathRooted(cfg.WorktreePath));
    }

    [Fact]
    public void SnapshotPath_CombinesWorktreeDirAndSnapshotFileName()
    {
        File.WriteAllText(_tempConfigPath, @"{
            ""worktreeDir"": ""./processes"",
            ""snapshotFileName"": ""my-snapshot.json""
        }");

        var cfg = ServerConfig.Load(_tempConfigPath);

        Assert.EndsWith("my-snapshot.json", cfg.SnapshotPath);
        Assert.Equal(Path.Combine(cfg.WorktreePath, "my-snapshot.json"), cfg.SnapshotPath);
    }

    [Fact]
    public void GetEffectiveConnectionString_SsoMode_UsesIntegratedSecurity()
    {
        File.WriteAllText(_tempConfigPath, @"{
            ""sqlServer"": ""MYHOST\\SQLEXPRESS"",
            ""sqlDatabase"": ""bpdb"",
            ""sqlAuth"": ""sso""
        }");

        var cfg = ServerConfig.Load(_tempConfigPath);
        var conn = cfg.GetEffectiveConnectionString();

        Assert.Contains("Server=MYHOST\\SQLEXPRESS", conn);
        Assert.Contains("Database=bpdb", conn);
        Assert.Contains("Integrated Security=SSPI", conn);
        Assert.DoesNotContain("User Id", conn);
        Assert.DoesNotContain("Password", conn);
    }

    [Fact]
    public void GetEffectiveConnectionString_UserMode_AppendsUserIdAndPassword()
    {
        File.WriteAllText(_tempConfigPath, @"{
            ""sqlServer"": ""MYHOST\\SQLEXPRESS"",
            ""sqlDatabase"": ""bpdb"",
            ""sqlAuth"": ""user"",
            ""sqlUser"": ""myuser"",
            ""sqlPassword"": ""mypwd""
        }");

        var cfg = ServerConfig.Load(_tempConfigPath);
        var conn = cfg.GetEffectiveConnectionString();

        Assert.Contains("Server=MYHOST\\SQLEXPRESS", conn);
        Assert.Contains("Database=bpdb", conn);
        Assert.Contains("User Id=myuser", conn);
        Assert.Contains("Password=mypwd", conn);
    }
}
