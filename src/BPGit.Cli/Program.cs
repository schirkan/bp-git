using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using BPGit.Data;
using BPGit.Format;

namespace BPGit.Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("bpgit - Git-konformer Adapter für Blue Prism (Phase 1 Read-Only)");

        var connOpt = new Option<string>(
            "--connection",
            getDefaultValue: () => "Server=(localdb)\\BluePrismLocalDB;Integrated Security=SSPI;Database=BluePrism",
            description: "SQL Server connection string (SSPI default)");

        var outputOpt = new Option<string>(
            "--output",
            getDefaultValue: () => Directory.GetCurrentDirectory(),
            description: "Worktree output directory");

        var pullCmd = new Command("pull", "Exportiert BP-Prozesse aus der DB als XML-Dateien in den Worktree");
        pullCmd.SetHandler(async (conn, output) =>
        {
            var repo = new ProcessRepository(conn);
            var xml = new ProcessXmlSerializer();
            var dir = Path.Combine(output, "processes");
            Directory.CreateDirectory(dir);
            var processes = await repo.ListAllAsync();
            int count = 0;
            foreach (var p in processes)
            {
                if (string.IsNullOrEmpty(p.processxml)) continue;
                if (!xml.IsValid(p.processxml)) continue;
                var idDir = Path.Combine(dir, p.processid.ToString());
                Directory.CreateDirectory(idDir);
                await File.WriteAllTextAsync(Path.Combine(idDir, "process.xml"), p.processxml);
                count++;
            }
            Console.WriteLine($"Pulled {count} processes to {dir}");
        }, connOpt, outputOpt);
        root.Add(pullCmd);

        root.AddGlobalOption(connOpt);
        root.AddGlobalOption(outputOpt);

        return await root.InvokeAsync(args);
    }
}
