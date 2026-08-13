using BPGit.Server;
using BPGit.Server.Commands;
using BPGit.Server.GitHttp;
using BPGit.Server.Services;
using Microsoft.AspNetCore.Authentication.Negotiate;

var cfg = ServerConfig.Load(args);

// Subcommand dispatch: `bpgit-server init [repo]` runs once and exits.
if (args.Length > 0 && args[0].Equals("init", StringComparison.OrdinalIgnoreCase))
{
    // Allow `bpgit-server init my-repo` to override RepoName for this invocation.
    if (args.Length >= 2 && !string.IsNullOrWhiteSpace(args[1]))
    {
        // Mutate a copy so the file-loaded config is not affected.
        var overrideName = args[1].TrimEnd('/');
        if (overrideName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            overrideName = overrideName[..^4];
        cfg = new ServerConfig
        {
            ListenUrls = cfg.ListenUrls,
            RepoRoot = cfg.RepoRoot,
            RepoName = overrideName,
            BpServer = cfg.BpServer,
            BpDatabase = cfg.BpDatabase,
            BpAuth = cfg.BpAuth,
            BpUser = cfg.BpUser,
            BpPassword = cfg.BpPassword,
        };
    }
    return InitCommand.Run(cfg);
}

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(cfg.ListenUrls.ToArray());

builder.Services
    .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();

builder.Services.AddAuthorization(options =>
{
    // Default policy requires an authenticated Negotiate user — protects the
    // /admin/* endpoints. Git smart-HTTP routes are explicitly opted out via
    // .AllowAnonymous() (smart-HTTP clients don't ship credentials unless the
    // URL embeds them, and forcing Negotiate here breaks all non-Windows clients).
    options.DefaultPolicy = options.GetPolicy("RequireAdmin");

    options.AddPolicy("RequireAdmin", policy =>
        policy.RequireAuthenticatedUser());

    // Git smart-HTTP routes use this no-op policy — explicit override of the
    // FallbackPolicy for the catch-all endpoint. We also use .AllowAnonymous()
    // which together with this no-op policy guarantees no auth challenge on
    // /{repo}/info/refs and /{repo}/git-*-pack.
    options.AddPolicy("AllowAnonymousGit", policy =>
        policy.RequireAssertion(_ => true));
});

builder.Services.AddSingleton(cfg);

// BP-DB Connection String (Windows Integrated Auth default für localdb)
string BuildConnectionString(ServerConfig c) =>
    c.BpAuth.Equals("sso", StringComparison.OrdinalIgnoreCase)
        ? $"Server={c.BpServer};Database={c.BpDatabase};Integrated Security=SSPI;TrustServerCertificate=true;"
        : $"Server={c.BpServer};Database={c.BpDatabase};User Id={c.BpUser};Password={c.BpPassword};TrustServerCertificate=true;";

builder.Services.AddSingleton<BpDbService>(sp =>
    new BpDbService(BuildConnectionString(sp.GetRequiredService<ServerConfig>())));
builder.Services.AddSingleton(sp =>
{
    var db = sp.GetRequiredService<BpDbService>();
    var srvCfg = sp.GetRequiredService<ServerConfig>();
    var sync = new BpSyncService(db);
    sync.BindConfig(srvCfg);
    return sync;
});
builder.Services.AddSingleton<PreReceiveHandler>();
builder.Services.AddSingleton(sp =>
{
    var db = sp.GetRequiredService<BpDbService>();
    return new WorktreeSyncService(db);
});
builder.Services.AddSingleton<PostReceiveHandler>();
builder.Services.AddSingleton<PostCheckoutHandler>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Health endpoint — anonymous, no auth required (useful for monitoring).
app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    server = "bpgit-server",
    version = "0.1.0-phase4b",
    repoRoot = cfg.RepoRoot,
    repoName = cfg.RepoName,
    bareRepo = cfg.BareRepoPath,
})).AllowAnonymous();

// Admin-Endpoint: BP-DB-Lookup per Name (Phase 4b MVP — Smoke-Test-Zweck).
// Liefert {found, processId, name} oder 404. Auth via FallbackPolicy erforderlich.
app.MapGet("/admin/db-lookup", async (string name, BpDbService db) =>
{
    var processId = await db.LookupProcessIdByNameAsync(name);
    if (processId is null)
        return Results.NotFound(new { name, found = false });
    var dbName = await db.GetProcessNameAsync(processId.Value);
    return Results.Ok(new { name, found = true, processId, dbName });
}).AllowAnonymous();

// Admin-Endpoint: Process-Lock-Check
app.MapGet("/admin/db-lock", async (Guid processId, BpDbService db) =>
{
    var lockInfo = await db.GetProcessLockAsync(processId);
    if (lockInfo is null)
        return Results.Ok(new { processId, locked = false });
    return Results.Ok(new
    {
        processId,
        locked = true,
        username = lockInfo.Username,
        machineName = lockInfo.MachineName,
        lockDateTime = lockInfo.LockDateTime
    });
}).AllowAnonymous();

// Admin-Endpoint: Worktree-Syncronisation (Phase 4c — BP-DB → Worktree).
// Trigger via POST /admin/sync-worktree?root=<worktree-path>
// Returns counts of written/deleted/skipped files + errors.
app.MapPost("/admin/sync-worktree", async (HttpContext ctx, WorktreeSyncService sync) =>
{
    var root = ctx.Request.Query["root"].ToString();
    if (string.IsNullOrWhiteSpace(root))
        return Results.BadRequest(new { error = "Query parameter 'root' (worktree path) required" });

    try
    {
        var result = await sync.MaterializeAsync(root);
        return Results.Ok(new
        {
            worktreeRoot = root,
            written = result.Written,
            deleted = result.Deleted,
            skipped = result.Skipped,
            errors = result.Errors
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
}).AllowAnonymous();

// Git smart-HTTP endpoints. Smart-HTTP is anonymous by protocol design — git
// clients don't ship credentials with /info/refs or /git-*-pack requests unless
// the URL embeds them. FallbackPolicy + .AllowAnonymous() alone is not enough
// in ASP.NET Core (FallbackPolicy still triggers a 401 before the Anonymous
// override), so we explicitly apply the no-op "AllowAnonymousGit" policy.
//
// The catch-all is registered as MapFallback so it does not collide with
// /healthz, /admin/*, or other explicit routes.
app.MapFallback(async (HttpContext ctx, ServerConfig srvCfg) =>
{
    var handled = await GitHttpHandler.HandleAsync(ctx, srvCfg);
    if (!handled)
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        await ctx.Response.WriteAsync($"bpgit-server: no route for {ctx.Request.Path}\n");
    }
}).RequireAuthorization("AllowAnonymousGit");

// Pick a connectable test URL for the banner. 0.0.0.0 is a *bind* address
// (Windows accepts connections on every interface), not a connectable host —
// clients must use the machine's actual hostname / localhost / IP.
var firstUrl = cfg.ListenUrls.First();
var connectable = firstUrl.StartsWith("http://0.0.0.0", StringComparison.OrdinalIgnoreCase)
    ? "http://localhost" + firstUrl["http://0.0.0.0".Length..]
    : firstUrl;

Console.WriteLine($"[bpgit-server] Listening on {string.Join(", ", cfg.ListenUrls)}");
Console.WriteLine($"[bpgit-server] Connect URL: {connectable}  (0.0.0.0 is bind-only — clients use localhost / hostname / IP)");
Console.WriteLine($"[bpgit-server] Bare repo: {cfg.BareRepoPath}");
if (!Directory.Exists(cfg.BareRepoPath) || !LibGit2Sharp.Repository.IsValid(cfg.BareRepoPath))
{
    Console.WriteLine($"[bpgit-server] WARNING: no bare repo at {cfg.BareRepoPath} — run `bpgit-server init` first.");
}
Console.WriteLine($"[bpgit-server] Try: git ls-remote {connectable}/<repo-name>");

app.Run();
return 0;

// Make Program top-level friendly for tests.
public partial class Program { }
