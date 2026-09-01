using BPGit.Data;
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
// CLI-Mode (default):      bpgit-server init
//                         bpgit-server pull
//                         bpgit-server status
//                         bpgit-server diff  [processid]
//                         bpgit-server log    [--limit N] [--processid guid] [--since YYYY-MM-DD] [--event SCODE]
//                         bpgit-server commit --force
//
// Internal sub-commands (server-side bare-repo bootstrap) live under --serve
// so they don't collide with CLI's worktree-space commands.

public static partial class Program
{
    static Task<int> Main(string[] args)
    {
        // Dispatch: --serve [config-path] | /s [config-path] | -s [config-path] starts server mode
        if (args.Length > 0 && IsServerFlag(args[0]))
        {
            var serverArgs = args.Length > 1 ? args[1..] : Array.Empty<string>();
            return Task.FromResult(RunServer(serverArgs));
        }

        // CLI mode: delegate to BPGit.Cli (its Program.Main is the static entry
        // point of the referenced assembly).
        return BPGit.Cli.Program.Main(args);
    }

    static bool IsServerFlag(string flag) =>
        flag == "--serve" || flag == "/s" || flag == "-s";

    static int RunServer(string[] args)
    {
        // First positional arg (if not "init") = config-path override. Default = <exe-dir>/bpgit.json.
        string? configPath = null;
        var remainingArgs = args;
        if (args.Length > 0 && !args[0].Equals("init", StringComparison.OrdinalIgnoreCase))
        {
            configPath = args[0];
            remainingArgs = args[1..];
        }

        var cfg = ServerConfig.Load(configPath);

        // Server-Mode subcommand: `bpgit --serve init [repo]` - runs once and exits.
        // Without flag, unified binary runs in CLI mode where `init` is the CLI init.
        if (remainingArgs.Length > 0 && remainingArgs[0].Equals("init", StringComparison.OrdinalIgnoreCase))
        {
            var overrideName = (remainingArgs.Length >= 2 && !string.IsNullOrWhiteSpace(remainingArgs[1]))
                ? remainingArgs[1].TrimEnd('/')
                : null;
            if (overrideName is not null && overrideName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                overrideName = overrideName[..^4];

            cfg = new ServerConfig
            {
                ListenUrls = cfg.ListenUrls,
                RepoRoot = cfg.RepoRoot,
                RepoName = overrideName ?? cfg.RepoName,
                SqlServer = cfg.SqlServer,
                SqlDatabase = cfg.SqlDatabase,
                SqlAuth = cfg.SqlAuth,
                SqlUser = cfg.SqlUser,
                SqlPassword = cfg.SqlPassword,
                WorktreeDir = cfg.WorktreeDir,
                SnapshotFileName = cfg.SnapshotFileName,
                AutomateCPath = cfg.AutomateCPath,
                CliAuthMode = cfg.CliAuthMode,
                CliUsername = cfg.CliUsername,
                CliPassword = cfg.CliPassword,
            };

            return InitCommand.Run(cfg);
        }

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls(cfg.ListenUrls.ToArray());

        // ------------------------------------------------------------------
        // Auth-Setup
        //
        // Heutiger Stand (post Code-Review 2026-08-30):
        //  - /admin/* und /healthz waren AllowAnonymous() — ein MVP-Smoke-Test-
        //    Kompromiss (vor Hook-Wiring, Phase 5+ geplant — siehe Spec §9 + Finding #1).
        //    Inzwischen entfernt: /admin/db-lookup, /admin/db-lock, /admin/sync-worktree.
        //    Diagnose-Funktionen wandern in CLI-Subcommands (z.B. `bpgit sync --root <path>`).
        //  - Smart-HTTP-Endpoints (/{repo}/info/refs, /{repo}/git-*-pack) bleiben anonym,
        //    weil git-Clients per Spec keine Credentials mitschicken. Das explizite
        //    `AllowAnonymousGit`-Policy-Pattern steht weiter unten.
        //  - Es gibt aktuell keine authentifizierten Endpoints. Wenn welche hinzukommen:
        //    .RequireAuthorization() auf der jeweiligen Route, NICHT auf options.DefaultPolicy
        //    (würde sonst smart-HTTP-Routes brechen).
        // ------------------------------------------------------------------
        builder.Services
            .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
            .AddNegotiate();

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("AllowAnonymousGit", policy =>
                policy.RequireAssertion(_ => true));
        });

        builder.Services.AddSingleton(cfg);

        // BP-DB Connection String: single source of truth ist
        // ServerConfig.GetEffectiveConnectionString() — kein Duplikat-Builder hier.
        builder.Services.AddSingleton<BpDbService>(sp =>
            new BpDbService(sp.GetRequiredService<ServerConfig>().GetEffectiveConnectionString()));

        builder.Services.AddSingleton<BpSyncService>(sp =>
        {
            var db = sp.GetRequiredService<BpDbService>();
            var srvCfg = sp.GetRequiredService<ServerConfig>();
            var sync = new BpSyncService(db);
            sync.BindConfig(srvCfg);
            return sync;
        });
        builder.Services.AddSingleton<PreReceiveHandler>();
        builder.Services.AddSingleton<WorktreeSyncService>(sp =>
        {
            var db = sp.GetRequiredService<BpDbService>();
            return new WorktreeSyncService(db);
        });

        // Hook-Handler (Phase 5+, per SPEC-pre-receive-wiring.md §1.3):
        //   PreReceiveHandler    - side-effect post-apply for git-receive-pack
        //   PostReceiveHandler   - worktree materialization after git-receive-pack
        //   PostCheckoutHandler  - worktree materialization after git-upload-pack (clone/fetch)
        // PushOrchestrator orchestriert receive-pack (body-puffer + git-CLI delegation + hook invocation).
        builder.Services.AddSingleton<PostReceiveHandler>();
        builder.Services.AddSingleton<PostCheckoutHandler>();
        builder.Services.AddSingleton<PushOrchestrator>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        // Health endpoint: anonymous, no auth required (standard for monitoring/lb).
        app.MapGet("/healthz", () => Results.Ok(new
        {
            status = "ok",
            server = "bpgit-server",
            version = "0.2.0-unified-cli",
            repoRoot = cfg.RepoRoot,
            repoName = cfg.RepoName,
            bareRepo = cfg.BareRepoPath,
        })).AllowAnonymous();

        // Git smart-HTTP endpoints (v2 spec):
        //   /{repo}/info/refs?service=git-upload-pack|git-receive-pack
        //   /{repo}/git-upload-pack
        //   /{repo}/git-receive-pack
        // Smart-HTTP is anonymous by protocol design - git clients don't ship
        // credentials with /info/refs or /git-*-pack requests unless they are
        // embedded in the URL. .AllowAnonymous() alone is not enough in ASP.NET
        // Core once an authentication scheme is registered (the scheme still
        // triggers 401 before the anonymous override), so we apply an explicit
        // no-op "AllowAnonymousGit" policy.
        //
        // The catch-all is registered as MapFallback so it does not collide
        // with /healthz or other explicit routes.
        app.MapFallback(async (HttpContext ctx, ServerConfig srvCfg,
                                PushOrchestrator push, PostCheckoutHandler postCheckout) =>
        {
            var handled = await GitHttpHandler.HandleAsync(ctx, srvCfg, push, postCheckout);
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
        Console.WriteLine($"[bpgit-server] Connect URL: {connectable} (0.0.0.0 bind-only - clients use localhost / hostname / IP)");
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
