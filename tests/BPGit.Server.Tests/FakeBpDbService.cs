using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BPGit.Data.Models;
using BPGit.Server.Services;

namespace BPGit.Server.Tests;

/// <summary>
/// In-memory test double fuer IBpDbService. Liefert geseeded
/// Process- und Folder-Daten fuer WorktreeSyncService.MaterializeAsync
/// ohne SQL Server.
///
/// Per #6385: minimale Mock-Surface, nur das was WorktreeSyncService
/// tatsaechlich ruft. Pre-receive-Tests koennen spaeter erweitern.
/// </summary>
public sealed class FakeBpDbService : IBpDbService
{
    public IReadOnlyList<BpProcessRow> Processes { get; }
    public FolderStructure Folders { get; }

    /// <summary>Lookup-Map: BPAProcess.name -> processid. Tests setzen Werte hier rein.</summary>
    public Dictionary<string, Guid> NameToProcessId { get; } = new();
    /// <summary>Lock-Map: processid -> lock-info. Tests setzen Werte hier rein.</summary>
    public Dictionary<Guid, BpaProcessLockInfo> Locks { get; } = new();

    public FakeBpDbService(IReadOnlyList<BpProcessRow> processes, FolderStructure folders)
    {
        Processes = processes;
        Folders = folders;
    }

    public Task<IReadOnlyList<BpProcessRow>> GetAllProcessesAsync(CancellationToken ct = default)
        => Task.FromResult(Processes);

    public Task<FolderStructure> GetFolderStructureAsync(CancellationToken ct = default)
        => Task.FromResult(Folders);

    public Task<Guid?> LookupProcessIdByNameAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Task.FromResult<Guid?>(null);
        return NameToProcessId.TryGetValue(name, out var id)
            ? Task.FromResult<Guid?>(id)
            : Task.FromResult<Guid?>(null);
    }

    public Task<BpaProcessLockInfo?> GetProcessLockAsync(Guid processId)
        => Locks.TryGetValue(processId, out var l)
            ? Task.FromResult<BpaProcessLockInfo?>(l)
            : Task.FromResult<BpaProcessLockInfo?>(null);
}
