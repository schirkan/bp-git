using System.Diagnostics;
using BPGit.Data;
using BPGit.Server.Services;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;

// Disambiguate: BPGit.Data also exports a `Process` type (BP process entity).
// In PushOrchestrator we only need System.Diagnostics.Process.
using Process = System.Diagnostics.Process;

namespace BPGit.Server.GitHttp;

/// <summary>
/// Orchestrates the smart-HTTP receive-pack flow per the Hybrid-Ansatz
/// documented in <c>specs/SPEC-pre-receive-wiring.md</c> §1.3:
///
///  1. Read the request body into a memory buffer.
///  2. Parse ref-update commands from the pkt-line section
///     (the binary pack-data section that follows the flush packet is
///      forwarded verbatim to native <c>git receive-pack --stateless-rpc</c>).
///  3. Spawn <c>git receive-pack</c>, pipe the buffered body in,
///     pipe stdout (report-status) back to the response.
///  4. On git-exit 0 (pack applied, refs updated), run the Pre-Receive
///     handler as **side-effect post-apply** (per Spec §9 Architektur-Update
///     + SPEC-pre-receive-wiring.md §1.3). This is the documented MVP-1
///     trade-off: pre-validate cannot reject the push today, but BP-DB
///     gets synced so downstream <c>git pull</c> / <c>git clone</c> sees
///     consistent XML state.
///  5. Run the Post-Receive handler for worktree materialization
///     (<c>WorktreeSyncService.MaterializeAsync</c>).
///
/// Ref-updates with <c>new-rev == 0000…0</c> (delete) are skipped because
/// <see cref="BpSyncService.DeleteAsync"/> is NotImplemented (Phase 4b-follow-up).
/// </summary>
public sealed class PushOrchestrator
{
    private readonly PreReceiveHandler _preReceive;
    private readonly PostReceiveHandler _postReceive;
    private readonly ServerConfig _cfg;
    private readonly ILogger<PushOrchestrator>? _logger;

    public PushOrchestrator(
        PreReceiveHandler preReceive,
        PostReceiveHandler postReceive,
        ServerConfig cfg,
        ILogger<PushOrchestrator>? logger = null)
    {
        _preReceive = preReceive;
        _postReceive = postReceive;
        _cfg = cfg;
        _logger = logger;
    }

    /// <summary>
    /// Hard timeout for the entire receive-pack flow (git-CLI + hooks).
    /// 5 min is generous: git receive-pack normally completes in seconds
    /// for non-pathological repos, and a sync MaterializeAsync against
    /// (localdb) is fast.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    public async Task<PushOrchestrationResult> ExecuteAsync(
        string repoPath,
        Stream requestBody,
        Stream responseBody,
        CancellationToken externalCt)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        cts.CancelAfter(DefaultTimeout);

        // 1. Buffer request body. MVP-1 pushes are small (BP-DB-process XML,
        //    single-user, LAN-only). Memory cost ~10 KB / push typical.
        byte[] bodyBytes;
        try
        {
            using var bodyMs = new MemoryStream();
            await requestBody.CopyToAsync(bodyMs, cts.Token);
            bodyBytes = bodyMs.ToArray();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException($"Client disconnected before body fully buffered (timeout {DefaultTimeout.TotalMinutes:0} min)");
        }

        // 2. Parse ref-updates synchronously from the buffered bytes
        //    (no I/O, just byte[] reads — see RefUpdateParser).
        var refUpdates = RefUpdateParser.Parse(bodyBytes);
        _logger?.LogDebug("[PushOrchestrator] Buffered {Bytes} bytes, parsed {Refs} ref-updates",
            bodyBytes.Length, refUpdates.Count);

        // 3. Spawn git-receive-pack and pipe body in.
        Process? proc = null;
        try
        {
            proc = SpawnReceivePack(repoPath);

            // Body → git stdin (Pack + ref-update pkt-lines).
            try
            {
                await proc.StandardInput.BaseStream.WriteAsync(bodyBytes, cts.Token);
            }
            finally
            {
                try { proc.StandardInput.Close(); } catch { /* already closed */ }
            }

            // Stderr drain (non-blocking).
            var stderrTask = proc.StandardError.ReadToEndAsync();

            // Git stdout → response (report-status pkt-line + pack-data).
            try
            {
                await proc.StandardOutput.BaseStream.CopyToAsync(responseBody, cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Client disconnected mid-pack. Kill git so it stops trying
                // to write into a closed pipe. Report timeout upstream.
                KillProcess(proc);
                throw new TimeoutException(
                    $"git receive-pack timed out after {DefaultTimeout.TotalMinutes:0} min");
            }

            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                KillProcess(proc);
                throw new TimeoutException(
                    $"git receive-pack timed out after {DefaultTimeout.TotalMinutes:0} min");
            }

            var stderr = await stderrTask;
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Console.Error.WriteLine($"[bpgit-server /git-receive-pack] {stderr.TrimEnd()}");
            }

            if (proc.ExitCode != 0)
            {
                _logger?.LogWarning(
                    "[PushOrchestrator] git receive-pack exited {Code}, skipping hooks",
                    proc.ExitCode);
                return new PushOrchestrationResult(proc.ExitCode, refUpdates, null, null);
            }

            // 4. Pre-Receive side-effect post-apply (Spec §9 Architektur-Update).
            var pre = await RunPreReceiveAsync(repoPath, refUpdates, cts.Token);

            // 5. Post-Receive worktree materialization.
            var post = await _postReceive.HandleAsync(_cfg.WorktreePath, cts.Token);

            return new PushOrchestrationResult(proc.ExitCode, refUpdates, pre, post);
        }
        finally
        {
            try { proc?.Dispose(); } catch { /* ignore */ }
        }
    }

    private async Task<PreReceiveSummary> RunPreReceiveAsync(
        string repoPath,
        IReadOnlyList<RefUpdate> updates,
        CancellationToken ct)
    {
        var successes = 0;
        var skipped = 0;
        var failures = new List<string>();

        using var repo = new Repository(repoPath);
        foreach (var update in updates)
        {
            ct.ThrowIfCancellationRequested();

            // Skip deletes: BpSyncService.DeleteAsync is NotImplemented
            // (Phase 4b-follow-up, see SPEC-pre-receive-wiring §1.3).
            // Skipping prevents pushing deletes from blocking BP-DB sync
            // for the modify/add ref-updates in the same push.
            if (IsZeroSha(update.NewRev))
            {
                skipped++;
                _logger?.LogDebug(
                    "[PushOrchestrator] Skipping delete ref-update {Ref}",
                    update.RefName);
                continue;
            }

            var result = await _preReceive.HandleAsync(
                repo,
                update.OldRev,
                update.NewRev,
                update.RefName,
                pathFilter: "processes/");

            if (result.Ok)
            {
                successes++;
            }
            else
            {
                failures.AddRange(result.Failures);
                _logger?.LogWarning(
                    "[PushOrchestrator] PreReceive failed for {Ref}: {Summary}",
                    update.RefName, result.Summary);
            }
        }

        if (failures.Count > 0)
        {
            Console.Error.WriteLine(
                $"[bpgit-server /git-receive-pack] PreReceive sync failed for {failures.Count} ref(s); " +
                "BP-DB may be inconsistent with repo (side-effect post-apply — see SPEC-pre-receive-wiring §1.3)");
        }

        return new PreReceiveSummary(successes, skipped, failures);
    }

    private static bool IsZeroSha(string sha) =>
        string.IsNullOrEmpty(sha) || sha.All(c => c == '0');

    private static void KillProcess(Process proc)
    {
        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // Process may already be exiting; ignore.
        }
    }

    private static Process SpawnReceivePack(string repoPath)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(repoPath);
        psi.ArgumentList.Add("receive-pack");
        psi.ArgumentList.Add("--stateless-rpc");
        return Process.Start(psi)!;
    }
}

/// <summary>
/// One ref-update extracted from the smart-HTTP receive-pack pkt-line section.
/// <c>OldRev</c> and <c>NewRev</c> are 40-char SHA-1 strings; <c>NewRev</c>
/// "0000000000000000000000000000000000000000" indicates a delete.
/// </summary>
public sealed record RefUpdate(string OldRev, string NewRev, string RefName);

/// <summary>Aggregate of a Pre-Receive batch run.</summary>
public sealed record PreReceiveSummary(
    int Successes,
    int Skipped,
    IReadOnlyList<string> Failures);

/// <summary>
/// Aggregate of the receive-pack orchestration: git exit code + ref-update
/// list + hook outcomes. <c>PreReceive</c>/<c>PostReceive</c> are null when
/// git-receive-pack exited non-zero (no hooks run) or when no ref-updates
/// were parsed (e.g. keepalive push).
/// </summary>
public sealed record PushOrchestrationResult(
    int GitExitCode,
    IReadOnlyList<RefUpdate> RefUpdates,
    PreReceiveSummary? PreReceive,
    MaterializeResult? PostReceive);