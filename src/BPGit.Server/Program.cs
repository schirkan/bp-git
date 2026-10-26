using BPGit.Data;
using BPGit.Server.Commands;
using BPGit.Server.GitHttp;
using BPGit.Server.Services;
using Microsoft.AspNetCore.Authentication.Negotiate;

// Server entry point. The optional --serve, /s, and -s flags are retained for
// compatibility; without a flag the binary also starts the server.

public static partial class Program
{
    static Task<int> Main(string[] args)
    {
        var serverArgs = args.Length > 0 && IsServerFlag(args[0])
            ? args[1..]
            : args;
        if (serverArgs.Length > 0 && IsHelpFlag(serverArgs[0]))
        {
            PrintHelp();
            return Task.FromResult(0);
        }

        return Task.FromResult(RunServer(serverArgs));
    }

    static bool IsServerFlag(string flag) =>
        flag == "--serve" || flag == "/s" || flag == "-s";

    static bool IsHelpFlag(string flag) =>
        flag == "--help" || flag == "-h" || flag == "/?";

    static void PrintHelp()
    {
        Console.WriteLine("bpgit - Blue Prism Git HTTP server");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  bpgit [config-path]              Start the server");
        Console.WriteLine("  bpgit [--serve] init [repo-name] Initialize a bare repository");
        Console.WriteLine("  bpgit --help                     Show this help");
    }

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

        // `bpgit [--serve] init [repo]` initializes the bare repository and exits.
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
                AutomateCPath = cfg.AutomateCPath,
            };

            return InitCommand.Run(cfg);
        }

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls(cfg.ListenUrls.ToArray());

        // ------------------------------------------------------------------
        // Auth-Setup
        //
        // Heutiger Stand (post Code-Review 2026-08-30):
        //  - Es gibt keine /admin/*-Diagnose-Endpunkte. /healthz bleibt anonym,
        //    damit Monitoring ohne Git-Client-Anmeldung möglich ist.
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
        builder.Services.AddSingleton<RepositoryOperationLock>();

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
        builder.Services.AddSingleton<BpToGitSyncService>();

        // Pre-/Post-Receive-Handler als C# im HTTP-Handler (kein Git-Hook-Script, Stand 2026-09-10):
        //   PreReceiveHandler   - side-effect post-apply für git-receive-pack
        //   PostReceiveHandler  - Worktree-Materialization nach git-receive-pack
        // PushOrchestrator orchestriert receive-pack (body-puffer + git-CLI delegation + handler invocation).
        builder.Services.AddSingleton<PostReceiveHandler>();
        builder.Services.AddSingleton<PushOrchestrator>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        // Health endpoint: anonymous, no auth required (standard for monitoring/lb).
        app.MapGet("/healthz", () => Results.Ok(new
        {
            status = "ok",
            server = "bpgit-server",
            version = typeof(Program).Assembly.GetName().Version?.ToString(),
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
                                PushOrchestrator push,
                                BpToGitSyncService bpToGitSync) =>
        {
            var handled = await GitHttpHandler.HandleAsync(ctx, srvCfg, push, bpToGitSync);
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
