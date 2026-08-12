using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BPGit.Data.Models;

namespace BPGit.Server.Services;

/// <summary>
/// DB operations required by WorktreeSyncService.MaterializeAsync.
///
/// Extracted as interface (Phase 4c-Test-Welle nach #6385) to enable mocking
/// without a live SQL Server. BpDbService remains the concrete implementation
/// (uses Microsoft.Data.SqlClient + SSPI/SQL auth per ServerConfig).
///
/// Future Phase 4 pre-receive tests (PreReceiveHandler) brauchen weitere
/// Methoden (LookupProcessIdByNameAsync, GetProcessLockAsync etc.) — die
/// koennen spaeter an dieser Interface ergaenzt werden, ohne bestehende
/// Konsumenten (WorktreeSyncService) zu brechen.
/// </summary>
public interface IBpDbService
{
    Task<IReadOnlyList<BpProcessRow>> GetAllProcessesAsync(CancellationToken ct = default);
    Task<FolderStructure> GetFolderStructureAsync(CancellationToken ct = default);
}
