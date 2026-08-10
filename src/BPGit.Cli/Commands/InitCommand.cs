using System;
using System.IO;
using System.Threading.Tasks;

namespace BPGit.Cli.Commands;

public static class InitCommand
{
    public static async Task RunAsync(string workdir)
    {
        var bpgitDir = Path.Combine(workdir, ".bpgit");
        Directory.CreateDirectory(bpgitDir);

        var configPath = Path.Combine(bpgitDir, "config.toml");
        if (!File.Exists(configPath))
        {
            var cfg = "# bpgit config - Blue Prism Git adapter\n\n[bp]\n" +
                "connection_string = \"Server=(localdb)\\\\BluePrismLocalDB;Integrated Security=SSPI;Database=BluePrism\"\n" +
                "# Optional: SQL-Auth fallback (only if SSPI unavailable)\n" +
                "# sql_user = \"bpgit_readonly\"\n" +
                "# sql_password_env = \"BPGIT_DB_PASSWORD\"\n";
            await File.WriteAllTextAsync(configPath, cfg);
            Console.WriteLine($"Created {configPath}");
        }
        else
        {
            Console.WriteLine($"{configPath} already exists");
        }
        Console.WriteLine("bpgit init complete");
    }
}
