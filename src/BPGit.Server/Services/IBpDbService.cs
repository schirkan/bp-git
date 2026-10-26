using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BPGit.Data;
using BPGit.Data.Models;

namespace BPGit.Server.Services;

/// <summary>
/// DB operations used by BP synchronization and WorktreeSyncService.MaterializeAsync.
///
/// Extracted as interface (Phase 4c-Test-Welle nach #6385) to enable mocking
/// without a live SQL Server. BpDbService remains the concrete implementation
/// (uses Microsoft.Data.SqlClient + SSPI/SQL auth per ServerConfig).
///
/// The interface enables unit tests to substitute database operations without
/// requiring an installed Blue Prism instance or SQL Server.
/// </summary>
public interface IBpDbService
{
    Task<IReadOnlyList<BpProcessRow>> GetAllProcessesAsync(CancellationToken ct = default);
    Task<FolderStructure> GetFolderStructureAsync(CancellationToken ct = default);

    /// <summary>Lookup BPAProcess.processid by name. Returns null wenn nicht gefunden (Phase 4b pre-receive).</summary>
    Task<Guid?> LookupProcessIdByNameAsync(string name);

}
