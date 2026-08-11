using LibGit2Sharp;

namespace BPGit.Server.Commands;

/// <summary>
/// <c>bpgit-server init</c> subcommand — creates a bare git repo at
/// <c>{RepoRoot}/{RepoName}.git</c>. Run once on the server before serving.
/// </summary>
public static class InitCommand
{
    public static int Run(ServerConfig cfg)
    {
        var repoPath = cfg.BareRepoPath;

        Console.WriteLine($"[bpgit-server init] RepoRoot = {cfg.RepoRoot}");
        Console.WriteLine($"[bpgit-server init] RepoName = {cfg.RepoName}");
        Console.WriteLine($"[bpgit-server init] Target   = {repoPath}");

        if (Directory.Exists(repoPath) && Directory.EnumerateFileSystemEntries(repoPath).Any())
        {
            if (Repository.IsValid(repoPath))
            {
                Console.WriteLine($"[bpgit-server init] Bare repo already exists at {repoPath}. Nothing to do.");
                return 0;
            }
            Console.Error.WriteLine($"[bpgit-server init] Path {repoPath} exists but is not a valid git repo. Aborting.");
            return 1;
        }

        Directory.CreateDirectory(cfg.RepoRoot);
        Repository.Init(repoPath, isBare: true);

        Console.WriteLine($"[bpgit-server init] Initialized empty bare repository at {repoPath}");
        Console.WriteLine($"[bpgit-server init] Next: start the server with `bpgit-server serve` or run as Windows service.");
        return 0;
    }
}
