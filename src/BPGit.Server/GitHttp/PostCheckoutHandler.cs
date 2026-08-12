using System.Threading;
using System.Threading.Tasks;
using BPGit.Server.Services;

namespace BPGit.Server.GitHttp;

/// <summary>
/// Post-Checkout Hook: nach <c>git clone</c> oder <c>git checkout</c> (Branch-Wechsel)
/// wird die Worktree-Materialization aufgerufen, damit der User den aktuellen
/// BP-DB-Stand im Worktree vorfindet.
///
/// Workflow:
/// 1. User: <c>git clone http://openclawpc:8181/bp-git</c> oder <c>git checkout feature-x</c>
/// 2. git-checkout-/clone-Operation im Bare-Repo
/// 3. post-checkout-Hook: BP-DB pollen, canonical XML-Files in Worktree schreiben
///
/// Kein git commit noetig — der Checkout selbst aktualisiert die Worktree.
/// </summary>
public sealed class PostCheckoutHandler
{
    private readonly WorktreeSyncService _sync;

    public PostCheckoutHandler(WorktreeSyncService sync) => _sync = sync;

    public Task<MaterializeResult> HandleAsync(string worktreeRoot, CancellationToken ct = default)
        => _sync.MaterializeAsync(worktreeRoot, ct);
}