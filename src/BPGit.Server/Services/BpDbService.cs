using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BPGit.Data.Models;
using Microsoft.Data.SqlClient;

namespace BPGit.Server.Services;

/// <summary>
/// DB-Wrapper fuer BP-DB-Lookups (BPAProcess + BPAProcessLock + BPATree + BPAGroup + BPAGroupProcess).
/// Wird vom pre-receive Hook (processid-Lookup) und von WorktreeSyncService
/// (Materialization BP-DB → Worktree) verwendet.
///
/// Connection-Pooling laeuft automatisch via .NET's built-in <see cref="SqlConnection"/>
/// pool (Connection-String identisch fuer mehrere Calls).
/// </summary>
public sealed class BpDbService
{
    private readonly string _connectionString;

    public BpDbService(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>Lookup <c>BPAProcess.processid</c> by name. Returns null wenn nicht gefunden.</summary>
    public async Task<Guid?> LookupProcessIdByNameAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 processid FROM BPAProcess WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", name);
        var result = await cmd.ExecuteScalarAsync();
        return result is Guid g ? g : null;
    }

    /// <summary>Lookup <c>BPAProcess.lastmodifieddate</c> by processid. Wird fuer Optimistic-Lock-Check verwendet.</summary>
    public async Task<DateTime?> GetLastModifiedAsync(Guid processId)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT lastmodifieddate FROM BPAProcess WHERE processid = @id";
        cmd.Parameters.AddWithValue("@id", processId);
        var result = await cmd.ExecuteScalarAsync();
        return result is DateTime dt ? dt : null;
    }

    /// <summary>Lookup <c>BPAProcessLock</c> fuer eine processid. Returns null wenn kein Lock.</summary>
    public async Task<BpaProcessLockInfo?> GetProcessLockAsync(Guid processId)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT l.lockdatetime, l.userid, l.machinename, u.username
            FROM BPAProcessLock l
            LEFT JOIN BPAUser u ON u.userid = l.userid
            WHERE l.processid = @id";
        cmd.Parameters.AddWithValue("@id", processId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (await rdr.ReadAsync())
        {
            return new BpaProcessLockInfo(
                LockDateTime: rdr.GetDateTime(0),
                UserId: rdr.GetGuid(1),
                MachineName: rdr.IsDBNull(2) ? null : rdr.GetString(2),
                Username: rdr.IsDBNull(3) ? null : rdr.GetString(3));
        }
        return null;
    }

    /// <summary>Lookup <c>BPAProcess.name</c> by processid (inverse Lookup fuer Status-Reporting).</summary>
    public async Task<string?> GetProcessNameAsync(Guid processId)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM BPAProcess WHERE processid = @id";
        cmd.Parameters.AddWithValue("@id", processId);
        var result = await cmd.ExecuteScalarAsync();
        return result is string s ? s : null;
    }

    /// <summary>
    /// Read all processes with name + xml content. Used by WorktreeSyncService for materialization.
    /// </summary>
    public async Task<IReadOnlyList<BpProcessRow>> GetAllProcessesAsync(CancellationToken ct = default)
    {
        var results = new List<BpProcessRow>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT processid, name, processxml
            FROM BPAProcess
            WHERE name IS NOT NULL AND name <> ''";
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            results.Add(new BpProcessRow(
                ProcessId: rdr.GetGuid(0),
                Name: rdr.GetString(1),
                XmlContent: rdr.IsDBNull(2) ? null : rdr.GetString(2)));
        }
        return results;
    }

    /// <summary>
    /// Read folder structure: filtered Trees (Processes + Objects only), their Groups,
    /// and the M:N ProcessMemberships. Used by WorktreeSyncService for materialization.
    /// BPAGroupGroup (nested folders) is supported via flat M:N — we resolve nested paths
    /// in WorktreeSyncService by walking the chain.
    /// </summary>
    public async Task<FolderStructure> GetFolderStructureAsync(CancellationToken ct = default)
    {
        var trees = new List<Tree>();
        var groups = new List<Group>();
        var memberships = new List<ProcessMembership>();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // Trees (only Processes=2 and Objects=3)
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name FROM BPATree WHERE id IN (2, 3) ORDER BY id";
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                trees.Add(new Tree { Id = rdr.GetInt32(0), Name = rdr.GetString(1) });
        }

        // Groups (only for our Trees)
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, treeid, name FROM BPAGroup WHERE treeid IN (2, 3)";
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                groups.Add(new Group
                {
                    Id = rdr.GetGuid(0),
                    TreeId = rdr.GetInt32(1),
                    Name = rdr.GetString(2),
                    IsRestricted = false
                });
        }

        // Memberships (M:N Process-Group)
        if (groups.Count > 0)
        {
            var groupIds = string.Join(",", groups.Select(g => $"'{g.Id}'"));
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT groupid, processid FROM BPAGroupProcess WHERE groupid IN ({groupIds})";
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                    memberships.Add(new ProcessMembership
                    {
                        GroupId = rdr.GetGuid(0),
                        ProcessId = rdr.GetGuid(1)
                    });
            }
        }

        return new FolderStructure(trees, groups, memberships);
    }
}

public sealed record BpaProcessLockInfo(
    DateTime LockDateTime,
    Guid UserId,
    string? MachineName,
    string? Username);

public sealed record BpProcessRow(Guid ProcessId, string Name, string? XmlContent);