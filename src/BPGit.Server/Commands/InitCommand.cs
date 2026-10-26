using System;
using System.IO;
using System.Text;
using BPGit.Data;
using LibGit2Sharp;

namespace BPGit.Server.Commands;

/// <summary>
/// <c>bpgit init</c> subcommand — creates a bare git repo at
/// <c>{RepoRoot}/{RepoName}.git</c> AND seeds it with an initial commit containing
/// a bpgit-curated <c>.gitignore</c> (XML-only whitelist).
///
/// Run once on the server before serving. The initial commit ensures that every
/// <c>git clone</c> delivers a working tree with the correct <c>.gitignore</c> in place
/// (per Martin-Direktive: ".gitignore soll beim clone immer ausgeliefert werden, nur xml").
/// </summary>
public static class InitCommand
{
    /// <summary>
    /// bpgit-curated <c>.gitignore</c>: allow-list approach — ignore everything, then
    /// whitelist directories, XML files, and <c>.gitignore</c>.
    /// </summary>
    public const string GitIgnoreContent = """
        # bpgit: only allow XML files (BP process definitions) plus .gitignore
        # See specs/SPEC-git-server.md Kapitel 3 "Worktree-Layout"

        # Ignore everything by default
        *

        # Allow directories so git can recurse
        !*/

        # Allow XML files (BP process definitions)
        !*.xml

        # Allow .gitignore itself
        !.gitignore

        """;

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

        // Seed initial commit with .gitignore so every clone receives it
        CreateInitialCommitWithGitIgnore(repoPath);

        Console.WriteLine($"[bpgit-server init] Initialized bare repository at {repoPath}");
        Console.WriteLine($"[bpgit-server init] Initial commit seeded with .gitignore (XML-only whitelist).");
        Console.WriteLine($"[bpgit-server init] Next: start the server with `bpgit` (no args) or run as Windows service.");
        return 0;
    }

    /// <summary>
    /// Creates a single-file initial commit containing only <c>.gitignore</c>.
    /// Uses LibGit2Sharp's low-level <see cref="ObjectDatabase"/> APIs to bypass
    /// the working tree (a bare repo has none).
    ///
    /// Creates the blob from a stream because the string overload treats its argument as a file path.
    /// </summary>
    private static void CreateInitialCommitWithGitIgnore(string repoPath)
    {
        using var repo = new Repository(repoPath);

        // Create the .gitignore blob without a temporary file.
        using var gitignoreStream = new MemoryStream(Encoding.UTF8.GetBytes(GitIgnoreContent));
        var blob = repo.ObjectDatabase.CreateBlob(gitignoreStream);

        // 2. Build tree with .gitignore entry
        var treeDef = new TreeDefinition();
        treeDef.Add(".gitignore", blob.Id, Mode.NonExecutableFile);
        var tree = repo.ObjectDatabase.CreateTree(treeDef);

        // Create the initial commit with no parents.
        var author = new Signature("bpgit-server", "noreply@openclawpc", DateTimeOffset.UtcNow);
        var commit = repo.ObjectDatabase.CreateCommit(
            author, author,
            "bpgit: initial commit (XML-only .gitignore)",
            tree,
            Array.Empty<Commit>(),
            false);

        repo.Refs.Add("refs/heads/main", commit.Id);

        // Point HEAD symbolically to main so clients and the server can resolve
        // the default branch after initialization.
        repo.Refs.Add("HEAD", "refs/heads/main", allowOverwrite: true);
    }
}