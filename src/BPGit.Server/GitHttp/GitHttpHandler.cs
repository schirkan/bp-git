using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LibGit2Sharp;

namespace BPGit.Server.GitHttp;

/// <summary>
/// Implements the git smart-HTTP protocol (v2) endpoints:
/// <list type="bullet">
///   <item><c>GET  /{repo}/info/refs?service=git-upload-pack</c>  — ref advertisement for fetch/clone</item>
///   <item><c>POST /{repo}/git-upload-pack</c>                   — pack download (Phase 4b: full implementation)</item>
///   <item><c>GET  /{repo}/info/refs?service=git-receive-pack</c> — ref advertisement for push</item>
///   <item><c>POST /{repo}/git-receive-pack</c>                  — pack upload (Phase 4b: full implementation)</item>
/// </list>
///
/// Phase 4a scope: <c>/info/refs</c> is fully implemented (pkt-line format with ref advertisement
/// and capability list). <c>/git-upload-pack</c> and <c>/git-receive-pack</c> return
/// <c>501 Not Implemented</c> with a clear explanation. Phase 4b/c will implement the pack
/// protocol using LibGit2Sharp's <see cref="LibGit2Sharp.Network"/> primitives.
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
    /// Routes <c>GET /{repo}/info/refs</c> and the two POST endpoints.
    /// Returns <c>true</c> if the request was handled (even with 501); <c>false</c> if no
    /// git route matched (caller should return 404).
    /// </summary>
    public static async Task<bool> HandleAsync(HttpContext ctx, ServerConfig cfg)
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
            return await HandleUploadPackStubAsync(ctx, repo2);
        }

        // Route: /{repo}/git-receive-pack  (POST)
        var receiveMatch = MatchRoute(path, suffix: "/git-receive-pack");
        if (receiveMatch is { } repo3 && HttpMethods.IsPost(ctx.Request.Method))
        {
            return await HandleReceivePackStubAsync(ctx, repo3);
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

        await Pkt.WriteFlushAsync(ctx.Response.Body);
        if (refs.Count == 0)
        {
            // Empty repo — advertise only capabilities, no refs. Git client will report
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

    private static async Task<bool> HandleUploadPackStubAsync(HttpContext ctx, string repoName)
    {
        ctx.Response.StatusCode = StatusCodes.Status501NotImplemented;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.Response.WriteAsync(
            $"bpgit-server: POST /{repoName}/git-upload-pack is not yet implemented.\n" +
            $"This endpoint will be implemented in Phase 4b — see context/SPEC-git-server.md Kapitel 7.\n");
        return true;
    }

    private static async Task<bool> HandleReceivePackStubAsync(HttpContext ctx, string repoName)
    {
        ctx.Response.StatusCode = StatusCodes.Status501NotImplemented;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.Response.WriteAsync(
            $"bpgit-server: POST /{repoName}/git-receive-pack is not yet implemented.\n" +
            $"This endpoint will be implemented in Phase 4b — see context/SPEC-git-server.md Kapitel 7.\n");
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
        var len = bytes.Length + 4 + 1; // 4 length bytes + payload + LF
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)len);
        await stream.WriteAsync(header);
        await stream.WriteAsync(bytes);
        await stream.WriteAsync(new byte[] { (byte)'\n' });
    }

    public static async Task WriteFlushAsync(Stream stream)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes("0000"));
    }

    public static async Task WriteDelimAsync(Stream stream)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes("0001"));
    }
}
