using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BPGit.Data;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;

namespace BPGit.Server.Services;

public sealed class BpToGitSyncService
{
    private const string CommitMessage = "bpgit: sync from Blue Prism database";
    private readonly WorktreeSyncService _worktreeSync;
    private readonly ServerConfig _config;
    private readonly RepositoryOperationLock _operationLock;
    private readonly ILogger<BpToGitSyncService>? _logger;

    public BpToGitSyncService(
        WorktreeSyncService worktreeSync,
        ServerConfig config,
        RepositoryOperationLock operationLock,
        ILogger<BpToGitSyncService>? logger = null)
    {
        _worktreeSync = worktreeSync;
        _config = config;
        _operationLock = operationLock;
        _logger = logger;
    }

    public async Task<bool> SyncBeforeFetchAsync(string repoPath, CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            return await SyncUnderRepositoryLockAsync(repoPath, cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    // The caller must hold RepositoryOperationLock; PushOrchestrator uses this
    // to preflight before receive-pack without recursively acquiring the lock.
    internal async Task<bool> SyncUnderRepositoryLockAsync(string repoPath, CancellationToken cancellationToken)
    {
        var materialized = await _worktreeSync.MaterializeAsync(_config.WorktreePath, cancellationToken);
        if (materialized.Errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"BP-to-Git materialization failed: {string.Join("; ", materialized.Errors)}");
        }

        var processDirectory = Path.GetFullPath(_config.WorktreePath);
        var worktreeRoot = Directory.GetParent(processDirectory)?.FullName
                           ?? throw new InvalidOperationException(
                               $"Worktree directory '{processDirectory}' must have a parent directory.");
        var relativeProcessPath = Path.GetRelativePath(worktreeRoot, processDirectory)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        if (!string.Equals(relativeProcessPath, "processes", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The configured worktree directory must be named 'processes' and be a direct child of its Git worktree root; found '{relativeProcessPath}'.");
        }

        var indexPath = Path.Combine(Path.GetTempPath(), $"bpgit-index-{Guid.NewGuid():N}");
        try
        {
            var headRef = GetHeadRef(repoPath);
            var head = await RunGitAsync(repoPath, worktreeRoot, indexPath, cancellationToken,
                "rev-parse", "--verify", headRef);
            await RunGitAsync(repoPath, worktreeRoot, indexPath, cancellationToken,
                "read-tree", head);
            var processXmlPaths = await GetProcessXmlPathsAsync(
                repoPath, worktreeRoot, processDirectory, indexPath, cancellationToken);
            if (processXmlPaths.Count > 0)
            {
                var addArguments = new[] { "add", "--all", "--" }
                    .Concat(processXmlPaths.Select(path => $":(literal){path}"))
                    .ToArray();
                await RunGitAsync(
                    repoPath,
                    worktreeRoot,
                    indexPath,
                    cancellationToken,
                    addArguments,
                    configureCommitIdentity: false);
            }

            var tree = await RunGitAsync(repoPath, worktreeRoot, indexPath, cancellationToken, "write-tree");
            var headTree = await RunGitAsync(repoPath, worktreeRoot, indexPath, cancellationToken,
                "rev-parse", $"{head}^{{tree}}");
            if (string.Equals(tree, headTree, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var commit = await RunGitAsync(repoPath, worktreeRoot, indexPath, cancellationToken,
                new[] { "commit-tree", tree, "-p", head, "-m", CommitMessage },
                configureCommitIdentity: true);
            await RunGitAsync(repoPath, worktreeRoot, indexPath, cancellationToken,
                "update-ref", headRef, commit, head);
            _logger?.LogInformation(
                "Committed Blue Prism database changes as {Commit} to {Ref}",
                commit,
                headRef);
            return true;
        }
        finally
        {
            if (File.Exists(indexPath))
            {
                File.Delete(indexPath);
            }
        }
    }

    private static async Task<IReadOnlyCollection<string>> GetProcessXmlPathsAsync(
        string repoPath,
        string worktreeRoot,
        string processDirectory,
        string indexPath,
        CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tracked = await RunGitAsync(
            repoPath,
            worktreeRoot,
            indexPath,
            cancellationToken,
            "ls-files",
            "-z",
            "--",
            ":(glob)processes/**/*.xml");
        foreach (var path in tracked.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            paths.Add(path);
        }

        foreach (var file in Directory.EnumerateFiles(processDirectory, "*.xml", SearchOption.AllDirectories))
        {
            paths.Add(Path.GetRelativePath(worktreeRoot, file)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/'));
        }

        return paths;
    }

    private string GetHeadRef(string repoPath)
    {
        using var repo = new Repository(repoPath);
        if (repo.Refs["HEAD"] is SymbolicReference head)
        {
            return head.Target.CanonicalName;
        }

        if (repo.Refs["refs/heads/main"] is null)
        {
            throw new InvalidOperationException(
                $"Bare repository '{repoPath}' has a detached HEAD and no refs/heads/main branch.");
        }

        repo.Refs.Add("HEAD", "refs/heads/main", allowOverwrite: true);
        _logger?.LogWarning(
            "Migrated detached bare-repository HEAD in {Repository} to refs/heads/main",
            repoPath);
        return "refs/heads/main";
    }

    private static async Task<string> RunGitAsync(
        string repoPath,
        string worktreeRoot,
        string indexPath,
        CancellationToken cancellationToken,
        params string[] arguments)
        => await RunGitAsync(repoPath, worktreeRoot, indexPath, cancellationToken, arguments, false);

    private static async Task<string> RunGitAsync(
        string repoPath,
        string worktreeRoot,
        string indexPath,
        CancellationToken cancellationToken,
        string[] arguments,
        bool configureCommitIdentity)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add($"--git-dir={repoPath}");
        startInfo.ArgumentList.Add($"--work-tree={worktreeRoot}");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["GIT_INDEX_FILE"] = indexPath;
        if (configureCommitIdentity)
        {
            startInfo.Environment["GIT_AUTHOR_NAME"] = "bpgit-server";
            startInfo.Environment["GIT_AUTHOR_EMAIL"] = "bpgit-server@localhost";
            startInfo.Environment["GIT_COMMITTER_NAME"] = "bpgit-server";
            startInfo.Environment["GIT_COMMITTER_EMAIL"] = "bpgit-server@localhost";
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Failed to start git for BP-to-Git synchronization.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }

        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(" ", arguments)} failed with exit code {process.ExitCode}: {error}");
        }

        return output;
    }
}
