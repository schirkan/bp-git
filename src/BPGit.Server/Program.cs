using BPGit.Server;
using BPGit.Server.Commands;
using BPGit.Server.GitHttp;
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

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Health endpoint — anonymous, no auth required (useful for monitoring).
app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    server = "bpgit-server",
    version = "0.1.0-phase4a",
    repoRoot = cfg.RepoRoot,
    repoName = cfg.RepoName,
    bareRepo = cfg.BareRepoPath,
})).AllowAnonymous();

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
