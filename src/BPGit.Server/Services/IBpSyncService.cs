using System.Threading.Tasks;

namespace BPGit.Server.Services;

/// <summary>
/// Sync-Operationen des pre-receive-Hooks auf BP-DB. Phase 4b Tests-Welle
/// (#6385): extrahiert fuer Mock-Tests ohne echte BP-Runtime.
///
/// Implementierende Klassen rufen `AutomateC.exe /import /forceid /overwrite`
/// (Modify), `/import /overwrite` (Add) oder SqlCommand DELETE (Delete) auf.
/// </summary>
public interface IBpSyncService
{
    Task<ImportResult> ModifyAsync(string xmlContent, string oldName, string newName);
    Task<ImportResult> AddAsync(string xmlContent, string name);
    Task<DeleteResult> DeleteAsync(string name);
}