using BPGit.Server;
using BPGit.Server.Commands;
using BPGit.Server.GitHttp;
using BPGit.Server.Services;
using Microsoft.AspNetCore.Authentication.Negotiate;

// Unified entry point for both Server- and CLI-modes.
//
// Server-Mode (Kestrel):  bpgit-server --serve [config-path]
//                         bpgit-server /s      [config-path]
//                         bpgit-server -s      [config-path]
//                         bpgit-server --serve init [repo-name]
//                         bpgit-server --serve init my-repo
//
// CLI-Mode (default):      bpgit-server init [--install-hooks]
//                         bpgit-server pull
//                         bpgit-server status
//                         bpgit-server diff  [processid]
//                         bpgit-server log    [--limit N] [--processid guid] [--since YYYY-MM-DD] [--event SCODE]
//                         bpgit-server commit --force
//
// Internal sub-commands (server-side bare-repo bootstrap) live under --serve
// so they don't collide with the CLI's worktree-space commands.
public static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Dispatch: --serve [config-path] | /s [config-path] | -s [config-path] starts server mode
        if (args.Length > 0 && IsServerFlag(args[0]))
        {
            var serverArgs = args.Length > 1 ? args[1..] : Array.Empty<string>();
            return RunServer(serverArgs);
        }

        // CLI mode: delegate to BPGit.Cli (its Program.Main is a static method
        // callable from this host assembly).
        return await BPGit.Cli.Program.Main(args);
    }

    private static bool IsServerFlag(string flag) =>
        flag == "--serve" || flag == "/s" || flag == "-s";

    private static int RunServer(string[] args)
    {
        var cfg = ServerConfig.Load(args);

        // Server-Mode subcommand: `bpgit-server --serve init [repo]` runs once and exits.
        // Without this flag, the unified binary is in CLI mode and `init` is the CLI init.
        if (args.Length > 0 && args[0].Equals("init", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length >= 2 && !string.IsNullOrWhiteSpace(args[1]))
            {
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
            // Default policy requires an authenticated Negotiate user - protects the
            // /admin/* endpoints. Git smart-HTTP routes are explicitly opted out via
            // .AllowAnonymous() (smart-HTTP clients don't ship credentials unless the
            // URL embeds them, and forcing Negotiate here breaks all non-Windows clients).
            options.DefaultPolicy = options.GetPolicy("RequireAdmin");

            options.AddPolicy("RequireAdmin", policy =>
                policy.RequireAuthenticatedUser());

            // Git smart-HTTP routes use this no-op policy - explicit override of the
            // FallbackPolicy for the catch-all endpoint. We also use .AllowAnonymous()
            // which together with this no-op policy guarantees no auth challenge on
            // /{repo}/info/refs and /{repo}/git-*-pack.
            options.AddPolicy("AllowAnonymousGit", policy =>
                policy.RequireAssertion(_ => true));
        });

        builder.Services.AddSingleton(cfg);

        // BP-DB Connection String (Windows Integrated Auth default fuer localdb)
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

        // Health endpoint - anonymous, no auth required (useful for monitoring).
        app.MapGet("/healthz", () => Results.Ok(new
        {
            status = "ok",
            server = "bpgit-server",
            version = "0.2.0-unified-cli",
            repoRoot = cfg.RepoRoot,
            repoName = cfg.RepoName,
            bareRepo = cfg.BareRepoPath,
        })).AllowAnonymous();

        // Admin-Endpoint: BP-DB-Lookup per Name (Phase 4b MVP - Smoke-Test-Zweck).
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

        // Admin-Endpoint: Worktree-Syncronisation (Phase 4c - BP-DB -> Worktree).
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

        // Git smart-HTTP endpoints. Smart-HTTP is anonymous by protocol design - git
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
        // (Windows accepts connections on every interface), not a connectable host -
        // clients must use the machine's actual hostname / localhost / IP.
        var firstUrl = cfg.ListenUrls.First();
        var connectable = firstUrl.StartsWith("http://0.0.0.0", StringComparison.OrdinalIgnoreCase)
            ? "http://localhost" + firstUrl["http://0.0.0.0".Length..]
            : firstUrl;

        Console.WriteLine($"[bpgit-server] Listening on {string.Join(", ", cfg.ListenUrls)}");
        Console.WriteLine($"[bpgit-server] Connect URL: {connectable}  (0.0.0.0 is bind-only - clients use localhost / hostname / IP)");
        Console.WriteLine($"[bpgit-server] Bare repo: {cfg.BareRepoPath}");
        if (!Directory.Exists(cfg.BareRepoPath) || !LibGit2Sharp.Repository.IsValid(cfg.BareRepoPath))
        {
            Console.WriteLine($"[bpgit-server] WARNING: no bare repo at {cfg.BareRepoPath} - run `bpgit-server --serve init` first.");
        }
        Console.WriteLine($"[bpgit-server] Try: git ls-remote {connectable}/<repo-name>");

        app.Run();
        return 0;
    }
}
