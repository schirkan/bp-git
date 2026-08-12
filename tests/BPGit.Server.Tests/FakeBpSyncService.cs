using System.Collections.Generic;
using System.Threading.Tasks;
using BPGit.Server.Services;

namespace BPGit.Server.Tests;

/// <summary>
/// In-memory test double fuer IBpSyncService. Recording-Mock
/// (jeder Aufruf wird in einer Liste gefuehrt, optional Result vorgebbar).
/// </summary>
public sealed class FakeBpSyncService : IBpSyncService
{
    public List<(string xml, string oldName, string newName)> ModifyCalls { get; } = new();
    public List<(string xml, string name)> AddCalls { get; } = new();
    public List<string> DeleteCalls { get; } = new();

    public ImportResult NextModifyResult { get; set; } = ImportResult.Success(null);
    public ImportResult NextAddResult { get; set; } = ImportResult.Success(null);
    public DeleteResult NextDeleteResult { get; set; } = DeleteResult.NotImplemented("default");

    public Task<ImportResult> ModifyAsync(string xmlContent, string oldName, string newName)
    {
        ModifyCalls.Add((xmlContent, oldName, newName));
        return Task.FromResult(NextModifyResult);
    }

    public Task<ImportResult> AddAsync(string xmlContent, string name)
    {
        AddCalls.Add((xmlContent, name));
        return Task.FromResult(NextAddResult);
    }

    public Task<DeleteResult> DeleteAsync(string name)
    {
        DeleteCalls.Add(name);
        return Task.FromResult(NextDeleteResult);
    }
}