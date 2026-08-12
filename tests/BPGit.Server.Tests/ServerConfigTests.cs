using System;
using System.IO;
using BPGit.Server;
using Xunit;

namespace BPGit.Server.Tests;

public class ServerConfigTests : IDisposable
{
    private readonly string _tempConfigPath;
    private readonly string? _originalEnv;

    public ServerConfigTests()
    {
        _tempConfigPath = Path.Combine(
            Path.GetTempPath(),
            $"bpgit-server-config-{Guid.NewGuid():N}.json");
        _originalEnv = Environment.GetEnvironmentVariable("BPGIT_SERVER_CONFIG");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BPGIT_SERVER_CONFIG", _originalEnv);
        if (File.Exists(_tempConfigPath))
            File.Delete(_tempConfigPath);
    }

    [Fact]
    public void Load_FromTempFile_ParsesBpPassword()
    {
        File.WriteAllText(_tempConfigPath, @"{ ""bpPassword"": ""geheim"" }");
        Environment.SetEnvironmentVariable("BPGIT_SERVER_CONFIG", _tempConfigPath);

        var cfg = ServerConfig.Load(Array.Empty<string>());

        Assert.Equal("geheim", cfg.BpPassword);
    }

    [Fact]
    public void Load_FromTempFile_ParsesListenUrlsAndBpAuth()
    {
        File.WriteAllText(_tempConfigPath, @"{
            ""listenUrls"": [""http://10.0.0.1:9999""],
            ""bpAuth"": ""user"",
            ""bpUser"": ""myuser"",
            ""bpServer"": ""MYHOST\\SQLEXPRESS"",
            ""bpDatabase"": ""bpdb"",
            ""repoRoot"": ""C:\\bpgit\\data"",
            ""repoName"": ""myrepo""
        }");
        Environment.SetEnvironmentVariable("BPGIT_SERVER_CONFIG", _tempConfigPath);

        var cfg = ServerConfig.Load(Array.Empty<string>());

        Assert.Single(cfg.ListenUrls);
        Assert.Equal("http://10.0.0.1:9999", cfg.ListenUrls[0]);
        Assert.Equal("user", cfg.BpAuth);
        Assert.Equal("myuser", cfg.BpUser);
        Assert.Equal(@"MYHOST\SQLEXPRESS", cfg.BpServer);
        Assert.Equal("bpdb", cfg.BpDatabase);
        Assert.Equal(@"C:\bpgit\data", cfg.RepoRoot);
        Assert.Equal("myrepo", cfg.RepoName);
    }

    [Fact]
    public void Load_NonExistentFile_UsesDefaults()
    {
        Environment.SetEnvironmentVariable("BPGIT_SERVER_CONFIG", _tempConfigPath);

        var cfg = ServerConfig.Load(Array.Empty<string>());

        Assert.Equal("sso", cfg.BpAuth);
        Assert.NotEmpty(cfg.ListenUrls);
        Assert.Null(cfg.BpUser);
        Assert.Null(cfg.BpPassword);
    }

    [Fact]
    public void BareRepoPath_IsDerivedFromRepoRootAndRepoName()
    {
        File.WriteAllText(_tempConfigPath, @"{ ""repoRoot"": ""C:\\x"", ""repoName"": ""foo"" }");
        Environment.SetEnvironmentVariable("BPGIT_SERVER_CONFIG", _tempConfigPath);

        var cfg = ServerConfig.Load(Array.Empty<string>());

        Assert.Equal(@"C:\x\foo.git", cfg.BareRepoPath);
    }
}