using System;
using System.IO;
using System.Text;
using LibGit2Sharp;

namespace BPGit.Server.Commands;

/// <summary>
/// <c>bpgit-server init</c> subcommand — creates a bare git repo at
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
    /// whitelist directories, XML files, <c>.gitignore</c> itself, and <c>.bpgit/config.toml</c>.
    /// </summary>
    public const string GitIgnoreContent = """
        # bpgit: only allow XML files (BP process definitions) plus bpgit config
        # See context/SPEC-git-server.md Kapitel 3 "Worktree-Layout"

        # Ignore everything by default
        *

        # Allow directories so git can recurse
        !*/

        # Allow XML files (BP process definitions)
        !*.xml

        # Allow .gitignore itself
        !.gitignore

        # Allow bpgit config directory + files
        !.bpgit/
        !.bpgit/config.toml
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
        Console.WriteLine($"[bpgit-server init] Next: start the server with `bpgit-server` (no args) or run as Windows service.");
        return 0;
    }

    /// <summary>
    /// Creates a single-file initial commit containing only <c>.gitignore</c>.
    /// Uses LibGit2Sharp's low-level <see cref="ObjectDatabase"/> APIs to bypass
    /// the working tree (a bare repo has none).
    ///
    /// LibGit2Sharp 0.32.0 API notes:
    /// <list type="bullet">
    ///   <item><c>CreateBlob(string)</c> — takes string content directly (UTF-8 internal).</item>
    ///   <item><c>TreeDefinition.Add(path, oid, mode)</c> — builder pattern for tree entries.</item>
    ///   <item><c>CreateCommit(author, committer, message, tree, params ObjectId[] parents)</c> — variadic parents, empty array for initial.</item>
    ///   <item><c>Refs.Add(name, ObjectId)</c> — for unborn refs (no <c>UpdateTarget(string, ObjectId)</c> overload in 0.32.0).</item>
    /// </list>
    /// </summary>
    private static void CreateInitialCommitWithGitIgnore(string repoPath)
    {
        using var repo = new Repository(repoPath);

        // 1. Create the .gitignore blob.
        //    WICHTIG: In LibGit2Sharp 0.32.0 gibt es folgende CreateBlob-Overloads:
        //      - CreateBlob(Stream stream)          -- liest Content aus dem Stream
        //      - CreateBlob(string path)            -- interpretiert als DATEIPFAD (nicht Content!)
        //      - CreateBlob(byte[] data)            -- nicht (mehr) vorhanden in 0.32.0
        //    Wir nutzen den Stream-Overload mit MemoryStream-Wrapper, weil:
        //      a) kein Temp-File noetig (kein Cleanup-Aufwand)
        //      b) funktioniert in bare repos (kein Working-Directory-Pfad noetig)
        //      c) der string-Overload wuerde sonst unsere .gitignore-Inhalte als Pfad interpretieren
        using var gitignoreStream = new MemoryStream(Encoding.UTF8.GetBytes(GitIgnoreContent));
        var blob = repo.ObjectDatabase.CreateBlob(gitignoreStream);

        // 2. Build tree with .gitignore entry
        var treeDef = new TreeDefinition();
        treeDef.Add(".gitignore", blob.Id, Mode.NonExecutableFile);
        var tree = repo.ObjectDatabase.CreateTree(treeDef);

        // 3. Create the initial commit (no parents via empty IEnumerable<Commit>)
        //    LibGit2Sharp 0.32.0 CreateCommit: (Signature, Signature, string, Tree, IEnumerable<Commit>, Boolean amend)
        //    -- IEnumerable<Commit> ist required (kein params), Boolean ist required (positional, heisst nicht 'amend').
        var author = new Signature("bpgit-server", "noreply@openclawpc", DateTimeOffset.UtcNow);
        var commit = repo.ObjectDatabase.CreateCommit(
            author, author,
            "bpgit: initial commit (XML-only .gitignore)",
            tree,
            Array.Empty<Commit>(),
            false);

        // 4. Create refs/heads/main (unborn branch in fresh bare repo)
        repo.Refs.Add("refs/heads/main", commit.Id);

        // 5. Update HEAD to point to refs/heads/main explicitly.
        //    LibGit2's bare-repo default is refs/heads/master, but our convention is
        //    refs/heads/main. Without this, HEAD is symbolic to refs/heads/master
        //    (non-existent) and `git show HEAD:.gitignore` fails.
        var headRef = repo.Refs["HEAD"];
        repo.Refs.UpdateTarget(headRef, "refs/heads/main");
    }
}