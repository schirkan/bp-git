using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using BPGit.Data;
using LibGit2Sharp;

namespace BPGit.Server.GitHttp;

/// <summary>
/// Implements the git smart-HTTP protocol (v2) endpoints:
/// <list type="bullet">
///   <item><c>GET  /{repo}/info/refs?service=git-upload-pack</c>  - ref advertisement for fetch/clone</item>
///   <item><c>POST /{repo}/git-upload-pack</c>                   - pack download (Phase 4b: full implementation + post-checkout hook)</item>
///   <item><c>GET  /{repo}/info/refs?service=git-receive-pack</c> - ref advertisement for push</item>
///   <item><c>POST /{repo}/git-receive-pack</c>                  - pack upload (Phase 4b + 5+: full implementation + pre/post-receive hooks)</item>
/// </list>
///
/// Hook-Wiring (Phase 5+ Hybrid-Ansatz per <c>specs/SPEC-pre-receive-wiring.md</c> §1.3):
///  - <c>POST /git-receive-pack</c>: <see cref="PushOrchestrator"/> runs the receive-pack flow, then invokes
///    <see cref="PreReceiveHandler"/> as a **side-effect post-apply** (Spec §9 Architektur-Update, MVP-1 trade-off
///    documented in SPEC-pre-receive-wiring.md §1.3) followed by <see cref="PostReceiveHandler"/>
///    for worktree materialization. The pack-apply itself remains delegated to native
///    <c>git receive-pack --stateless-rpc</c>.
///  - <c>POST /git-upload-pack</c>: after a successful fetch/clone, <see cref="PostCheckoutHandler"/>
///    re-materializes the configured worktree from BP-DB (canonical XML format).
/// </summary>
public static class GitHttpHandler
{
    /// <summary>Content-Type for smart-HTTP responses per git protocol v2 spec.</summary>
    public const string UploadPackContentType = "application/x-git-upload-pack-advertisement";
    public const string ReceivePackContentType = "application/x-git-receive-pack-advertisement";

    /// <summary>Capabilities advertised on the first ref.</summary>
    private const string UploadPackCapabilities = "multi_ack_detailed no-done side-band-64k thin-pack ofs-delta deepen-since deepen-not agent=git/2.43-bpgit";
    private const string ReceivePackCapabilities = "report-status delete-refs side-band-64k quiet atomic ofs-delta agent=git/2.43-bpgit";

    /// <summary>
    /// Hard timeout for git-CLI delegated upload-pack (clone/fetch). 5 minutes is
    /// generous: git upload-pack normally completes in seconds for non-pathological
    /// repos. Receive-pack uses <see cref="PushOrchestrator.DefaultTimeout"/> instead.
    /// </summary>
    private static readonly TimeSpan GitRpcTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Routes <c>GET /{repo}/info/refs</c> and the two POST endpoints.
    /// Returns <c>true</c> if the request was handled (even with 504/timeout); <c>false</c> if no
    /// git route matched (caller should return 404).
    /// </summary>
    public static async Task<bool> HandleAsync(
        HttpContext ctx,
        ServerConfig cfg,
        PushOrchestrator push,
        PostCheckoutHandler postCheckout)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;

        // Route: /{repo}/info/refs
        var infoRefsMatch = MatchRoute(path, suffix: "/info/refs");
        if (infoRefsMatch is { } repo1)
        {
            return await HandleInfoRefsAsync(ctx, cfg, repo1);
        }

        // Route: /{repo}/git-upload-pack  (POST)
        var uploadMatch = MatchRoute(path, suffix: "/git-upload-pack");
        if (uploadMatch is { } repo2 && HttpMethods.IsPost(ctx.Request.Method))
        {
            return await HandleUploadPackAsync(ctx, repo2, cfg, postCheckout);
        }

        // Route: /{repo}/git-receive-pack  (POST)
        var receiveMatch = MatchRoute(path, suffix: "/git-receive-pack");
        if (receiveMatch is { } repo3 && HttpMethods.IsPost(ctx.Request.Method))
        {
            return await HandleReceivePackAsync(ctx, repo3, cfg, push);
        }

        return false;
    }

    /// <summary>
    /// Returns the bare repo path for a given repo name, or null if it would escape <see cref="ServerConfig.RepoRoot"/>.
    /// Defends against path traversal in <c>git clone http://server/..\..\etc\passwd</c>.
    /// </summary>
    public static string? ResolveRepoPath(ServerConfig cfg, string repoName)
    {
        if (string.IsNullOrWhiteSpace(repoName)) return null;
        // Allow "bp-git" or "bp-git.git"
        var bareName = repoName.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? repoName
            : repoName + ".git";

        var fullPath = Path.GetFullPath(Path.Combine(cfg.RepoRoot, bareName));
        var rootFull = Path.GetFullPath(cfg.RepoRoot);

        if (!fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return null;

        return Directory.Exists(fullPath) && Repository.IsValid(fullPath)
            ? fullPath
            : null;
    }

    private static string? MatchRoute(string path, string suffix)
    {
        // Expect "/{repo}/info/refs" or "/{repo}/git-upload-pack" (with or without trailing slash)
        var trimmed = path.TrimEnd('/');
        if (!trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        var repoSegment = trimmed[..^suffix.Length].TrimStart('/');
        if (string.IsNullOrEmpty(repoSegment) || repoSegment.Contains('/')) return null;
        return repoSegment;
    }

    private static async Task<bool> HandleInfoRefsAsync(HttpContext ctx, ServerConfig cfg, string repoName)
    {
        var service = ctx.Request.Query["service"].ToString();
        if (service != "git-upload-pack" && service != "git-receive-pack")
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync("Missing or invalid 'service' query parameter (expected git-upload-pack or git-receive-pack).");
            return true;
        }

        var repoPath = ResolveRepoPath(cfg, repoName);
        if (repoPath is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            await ctx.Response.WriteAsync($"Repository '{repoName}' not found at {cfg.RepoRoot}.");
            return true;
        }

        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = service == "git-upload-pack" ? UploadPackContentType : ReceivePackContentType;
        ctx.Response.Headers.CacheControl = "no-cache";

        var capabilities = service == "git-upload-pack" ? UploadPackCapabilities : ReceivePackCapabilities;

        using var repo = new Repository(repoPath);
        var refs = repo.Refs
            .Where(r => r.IsLocalBranch || r.IsTag || r.IsRemoteTrackingBranch || r.IsNote)
            .OrderBy(r => r.CanonicalName, StringComparer.Ordinal)
            .ToList();

        // smart-HTTP v2: announce service name as the first pkt-line, then flush
        // before the ref advertisement block. Without the service header, git
        // clients reject the response with "expected service, got flush packet".
        await Pkt.WriteServiceHeaderAsync(ctx.Response.Body, service);
        await Pkt.WriteFlushAsync(ctx.Response.Body);
        if (refs.Count == 0)
        {
            // Empty repo - advertise only capabilities, no refs. Git client will report
            // "warning: no common commits" or similar, which is correct for an empty repo.
            await Pkt.WriteDataAsync(ctx.Response.Body, $" capabilities^{capabilities}\n");
        }
        else
        {
            var first = true;
            foreach (var r in refs)
            {
                var sha = r.TargetIdentifier;
                var canonical = r.CanonicalName;
                if (first)
                {
                    await Pkt.WriteDataAsync(ctx.Response.Body, $"{sha} {canonical}^{capabilities}\n");
                    first = false;
                }
                else
                {
                    await Pkt.WriteDataAsync(ctx.Response.Body, $"{sha} {canonical}\n");
                }
            }
        }
        await Pkt.WriteFlushAsync(ctx.Response.Body);

        return true;
    }

    /// <summary>
    /// Delegates to native <c>git upload-pack --stateless-rpc</c> via Process spawn.
    /// After successful exit (git applied the pack to client-side worktree),
    /// invokes <see cref="PostCheckoutHandler"/> to re-materialize the configured
    /// worktree from BP-DB so the user sees canonical XML formatting on
    /// the very next filesystem access.
    /// </summary>
    private static async Task<bool> HandleUploadPackAsync(
        HttpContext ctx,
        string repoName,
        ServerConfig cfg,
        PostCheckoutHandler postCheckout)
    {
        var repoPath = ResolveRepoPath(cfg, repoName);
        if (repoPath is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            await ctx.Response.WriteAsync($"Repository '{repoName}' not found.");
            return true;
        }

        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = UploadPackContentType;

        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(repoPath);
        psi.ArgumentList.Add("upload-pack");
        psi.ArgumentList.Add("--stateless-rpc");

        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await ctx.Response.WriteAsync("bpgit-server: failed to start git upload-pack process.");
            return true;
        }

        var stderrTask = proc.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(GitRpcTimeout);
        try
        {
            var copyIn = ctx.Request.Body.CopyToAsync(proc.StandardInput.BaseStream, cts.Token);
            try { await copyIn; }
            catch (OperationCanceledException) { /* client disconnect or timeout */ }
            proc.StandardInput.Close();

            var copyOut = proc.StandardOutput.BaseStream.CopyToAsync(ctx.Response.Body, cts.Token);
            try { await copyOut; }
            catch (OperationCanceledException) { /* client disconnect or timeout */ }

            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[bpgit-server /git-upload-pack] kill after timeout failed: {ex.Message}");
            }

            if (!ctx.Response.HasStarted)
            {
                ctx.Response.Clear();
                ctx.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                await ctx.Response.WriteAsync($"bpgit-server: git upload-pack timed out after {GitRpcTimeout.TotalMinutes:0} min.");
            }
            else
            {
                ctx.Abort();
            }
        }
        finally
        {
            var stderr = await stderrTask;
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Console.Error.WriteLine($"[bpgit-server /git-upload-pack] {stderr.TrimEnd()}");
            }
        }

        // Post-Checkout hook: re-materialize worktree from BP-DB after successful
        // fetch/clone. Runs only when git-exit was 0 (no header-sent abort).
        if (proc.ExitCode == 0)
        {
            try
            {
                var result = await postCheckout.HandleAsync(cfg.WorktreePath, cts.Token);
                Console.WriteLine(
                    $"[bpgit-server /git-upload-pack] PostCheckout materialization: " +
                    $"written={result.Written} deleted={result.Deleted} skipped={result.Skipped} errors={result.Errors.Count}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[bpgit-server /git-upload-pack] PostCheckout failed: {ex.Message}");
            }
        }

        return true;
    }

    /// <summary>
    /// Receive-pack endpoint: delegates to <see cref="PushOrchestrator"/> which
    /// orchestrates git-CLI delegation + Pre-Receive (side-effect post-apply per
    /// SPEC-pre-receive-wiring.md §1.3) + Post-Receive worktree materialization.
    /// </summary>
    private static async Task<bool> HandleReceivePackAsync(
        HttpContext ctx,
        string repoName,
        ServerConfig cfg,
        PushOrchestrator push)
    {
        var repoPath = ResolveRepoPath(cfg, repoName);
        if (repoPath is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            await ctx.Response.WriteAsync($"Repository '{repoName}' not found.");
            return true;
        }

        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = ReceivePackContentType;

        // 5-min timeout (per Finding #6) applies via PushOrchestrator.DefaultTimeout.
        using var cts = new CancellationTokenSource(GitRpcTimeout);

        try
        {
            await push.ExecuteAsync(repoPath, ctx.Request.Body, ctx.Response.Body, cts.Token);
        }
        catch (TimeoutException ex)
        {
            if (!ctx.Response.HasStarted)
            {
                ctx.Response.Clear();
                ctx.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                await ctx.Response.WriteAsync($"bpgit-server: {ex.Message}");
            }
            else
            {
                ctx.Abort();
            }
        }

        return true;
    }
}

/// <summary>
/// Helpers for the git pkt-line format (see gitprotocol-pack.txt):
/// each packet is <c>4-hex-digit length</c> + payload + LF, where length includes the
/// 4 length bytes and the LF. Flush packet is <c>0000</c>. Delim packet is <c>0001</c>.
/// </summary>
internal static class Pkt
{
    public static async Task WriteDataAsync(Stream stream, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        // pkt-line length per gitprotocol-pack.txt:
        // "The length of the packet, including the 4 bytes of the length itself,
        //  but not including the packet payload's LF terminator."
        // -> length = 4 (length bytes) + payload bytes (LF terminator is NOT counted).
        var len = bytes.Length + 4;
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)len);
        await stream.WriteAsync(header);
        await stream.WriteAsync(bytes);
        await stream.WriteAsync(new byte[] { (byte)'\n' });
    }

    /// <summary>
    /// Writes the service announcement pkt-line per smart-HTTP v2:
    /// <c>&lt;len&gt;git-upload-pack\n</c>. The terminating <c>0000</c> flush
    /// that separates the header from the ref advertisement must be emitted
    /// separately by the caller.
    /// </summary>
    public static Task WriteServiceHeaderAsync(Stream stream, string service)
        => WriteDataAsync(stream, service);

    public static async Task WriteFlushAsync(Stream stream)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes("0000"));
    }

    public static async Task WriteDelimAsync(Stream stream)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes("0001"));
    }
}