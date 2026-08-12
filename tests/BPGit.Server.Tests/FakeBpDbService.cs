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

    public FakeBpDbService(IReadOnlyList<BpProcessRow> processes, FolderStructure folders)
    {
        Processes = processes;
        Folders = folders;
    }

    public Task<IReadOnlyList<BpProcessRow>> GetAllProcessesAsync(CancellationToken ct = default)
        => Task.FromResult(Processes);

    public Task<FolderStructure> GetFolderStructureAsync(CancellationToken ct = default)
        => Task.FromResult(Folders);
}
