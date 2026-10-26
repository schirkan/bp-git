using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BPGit.Data;

namespace BPGit.Server.Services;

/// <summary>
/// Orchestriert das processid-Lookup + <c>/import</c> pro Git-Diff-Eintrag.
/// Wird vom <see cref="GitHttp.GitHttpHandler"/> pre-receive-Pfad aufgerufen.
///
/// Siehe <c>specs/SPEC-git-server.md</c> Kapitel 4 (processid-Mapping) +
/// Kapitel 7 (Server-Side Hooks).
///
/// XML-Konventionen:
/// <list type="bullet">
///   <item>Leading XML-Comments werden vor <c>/import</c> gestrippt (per Martin #6277).</item>
///   <item>Temp-File wird in <c>%TEMP%\bpgit-server-import-{guid}.xml</c> abgelegt.</item>
/// </list>
/// </summary>
public sealed class BpSyncService : IBpSyncService
{
    private readonly IBpDbService _db;

    public BpSyncService(IBpDbService db)
    {
        _db = db;
    }

    /// <summary>
    /// Modifiziert einen existierenden Process (oder umbenennt via XML-Edit).
    /// Lookup-Order: erst neuer Name (falls BP schon aktualisiert), dann alter Name.
    /// </summary>
    public async Task<ImportResult> ModifyAsync(string xmlContent, string oldName, string newName)
    {
        var processId = await _db.LookupProcessIdByNameAsync(newName)
                       ?? await _db.LookupProcessIdByNameAsync(oldName);
        if (processId is null)
        {
            return ImportResult.Failure(
                $"Modify: processid not found (newName='{newName}', oldName='{oldName}'). " +
                $"Wahrscheinlich Rename ohne vorherigen Pull oder BP-DB wurde extern geloescht.");
        }

        return await ImportAsync(xmlContent, forceId: processId.Value, label: $"Modify {oldName}->{newName}");
    }

    /// <summary>
    /// Legt einen neuen Process an. Erwartet dass kein existierender Process mit dem Namen existiert.
    /// </summary>
    public async Task<ImportResult> AddAsync(string xmlContent, string name)
    {
        var existing = await _db.LookupProcessIdByNameAsync(name);
        if (existing.HasValue)
        {
            return ImportResult.Failure(
                $"Add: process with name '{name}' already exists (processid={existing}). " +
                $"Wahrscheinlich Konflikt mit existierendem Process — fuehre zuerst git pull aus.");
        }
        // /import ohne /forceid: BP legt neuen Process mit NEUER processid an
        return await ImportAsync(xmlContent, forceId: null, label: $"Add {name}");
    }

    /// <summary>
    /// Reports process deletion as unsupported. Referential links to
    /// <c>BPAProcessBackup</c>, <c>BPAObject</c>, and <c>BPARelease</c> require
    /// cascade handling, which is out of scope by design.
    /// </summary>
    public async Task<DeleteResult> DeleteAsync(string name)
    {
        var processId = await _db.LookupProcessIdByNameAsync(name);
        if (processId is null)
        {
            return DeleteResult.NotFound($"Delete: process '{name}' not found in BP-DB.");
        }

        return DeleteResult.NotImplemented(
            $"Delete of process '{name}' (processid={processId}) is not supported by design.");
    }

    private async Task<ImportResult> ImportAsync(string xmlContent, Guid? forceId, string label)
    {
        var cleanXml = XmlSanitizer.StripLeadingXmlComments(xmlContent);
        var tmpFileName = $"bpgit-server-import-{(forceId?.ToString() ?? Guid.NewGuid().ToString("N"))}.xml";
        var tmpFile = Path.Combine(Path.GetTempPath(), tmpFileName);
        await File.WriteAllTextAsync(tmpFile, cleanXml);

        try
        {
            var args = new List<string> { "/import", tmpFile };
            if (forceId.HasValue)
            {
                args.Add("/forceid");
                args.Add(forceId.Value.ToString());
            }
            args.Add("/overwrite");

            AutomateCRunner.RunResult result;
            try
            {
                result = AutomateCRunner.Run(_cfg!, args.ToArray());
            }
            catch (Exception ex)
            {
                return ImportResult.Failure($"{label}: CLI failed: {ex.Message}");
            }

            if (result.ExitCode != 0)
            {
                return ImportResult.Failure(
                    $"{label}: AutomateC exit {result.ExitCode}. " +
                    $"stderr: {result.StdErr.Trim()}");
            }

            return ImportResult.Success(forceId);
        }
        finally
        {
            try { File.Delete(tmpFile); } catch { /* best effort */ }
        }
    }

    // StripLeadingXmlComments moved to XmlSanitizer (refactoring Martin #6401, dedup'd with WorktreeSyncService).

    // _cfg wird spaeter injiziert (Phase 4b MVP: hardcoded null, folgt in Program.cs-DI-Wiring)
    // Wir nutzen eine Property damit ModifyAsync etc. darauf zugreifen koennen
    private ServerConfig? _cfg;

    public void BindConfig(ServerConfig cfg) => _cfg = cfg;
}

public sealed record ImportResult(bool Ok, string? Message)
{
    public Guid? ProcessId { get; init; }

    public static ImportResult Success(Guid? processId) => new(true, null) { ProcessId = processId };
    public static ImportResult Failure(string message) => new(false, message);
}

public sealed record DeleteResult(bool Ok, string? Message, bool IsNotFound = false)
{
    public static DeleteResult NotFound(string message) => new(false, message, true);
    public static DeleteResult NotImplemented(string message) => new(false, message);
}