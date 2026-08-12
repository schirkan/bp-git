using System.Threading;
using System.Threading.Tasks;
using BPGit.Server.Services;

namespace BPGit.Server.GitHttp;

/// <summary>
/// Post-Receive Hook: nach erfolgreichem git push + pre-receive (BP-DB aktualisiert)
/// wird die Worktree-Materialization aufgerufen, um die Server-seitige Worktree auf
/// den neuen BP-DB-Stand zu bringen.
///
/// Workflow:
/// 1. git-receive-pack nimmt Pack entgegen, validiert refs
/// 2. pre-receive-Hook: pro XML-Aenderung processid-Lookup + /import /forceid /overwrite
/// 3. post-receive-Hook: BP-DB pollen, canonical XML-Files in Worktree schreiben,
///    stale Files löschen (für Renames/Deletes)
/// </summary>
public sealed class PostReceiveHandler
{
    private readonly WorktreeSyncService _sync;

    public PostReceiveHandler(WorktreeSyncService sync) => _sync = sync;

    public Task<MaterializeResult> HandleAsync(string worktreeRoot, CancellationToken ct = default)
        => _sync.MaterializeAsync(worktreeRoot, ct);
}