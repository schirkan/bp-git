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
            BpPasswordEnv = cfg.BpPasswordEnv,
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
    options.FallbackPolicy = options.DefaultPolicy;
});

builder.Services.AddSingleton(cfg);

// BP-DB Connection String (Windows Integrated Auth default für localdb)
string BuildConnectionString(ServerConfig c) =>
    c.BpAuth.Equals("sso", StringComparison.OrdinalIgnoreCase)
        ? $"Server={c.BpServer};Database={c.BpDatabase};Integrated Security=SSPI;TrustServerCertificate=true;"
        : $"Server={c.BpServer};Database={c.BpDatabase};User Id={c.BpUser};Password={Environment.GetEnvironmentVariable(c.BpPasswordEnv)};TrustServerCertificate=true;";

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

// Git smart-HTTP endpoints. Auth required via Negotiate (Win Integrated).
app.Map("/{repo}/**", async (HttpContext ctx, string repo, ServerConfig srvCfg) =>
{
    var handled = await GitHttpHandler.HandleAsync(ctx, srvCfg);
    if (!handled)
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        await ctx.Response.WriteAsync($"bpgit-server: no route for {ctx.Request.Path}\n");
    }
});

Console.WriteLine($"[bpgit-server] Listening on {string.Join(", ", cfg.ListenUrls)}");
Console.WriteLine($"[bpgit-server] Bare repo: {cfg.BareRepoPath}");
Console.WriteLine($"[bpgit-server] Try: git ls-remote {cfg.ListenUrls.First()}");

app.Run();
return 0;

// Make Program top-level friendly for tests.
public partial class Program { }
