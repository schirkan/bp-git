using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BPGit.Data.Models;
using Microsoft.Data.SqlClient;

namespace BPGit.Server.Services;

/// <summary>
/// DB-Wrapper fuer BP-DB-Lookups (BPAProcess + BPATree + BPAGroup + BPAGroupProcess).
/// Wird vom PreReceive-Prozess-Lookup und von WorktreeSyncService
/// (BP-DB → Server-Worktree/Git) verwendet.
///
/// Connection-Pooling laeuft automatisch via .NET's built-in <see cref="SqlConnection"/>
/// pool (Connection-String identisch fuer mehrere Calls).
/// </summary>
public sealed class BpDbService : IBpDbService
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

    /// <summary>
    /// Read all processes with name + xml content. Used by WorktreeSyncService for materialization.
    /// </summary>
    public async Task<IReadOnlyList<BpProcessRow>> GetAllProcessesAsync(CancellationToken ct = default)
    {
        var results = new List<BpProcessRow>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT processid, name, processxml
            FROM BPAProcess
            WHERE name IS NOT NULL AND name <> ''";
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
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
        await conn.OpenAsync(ct);

        // Trees (only Processes=2 and Objects=3)
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name FROM BPATree WHERE id IN (2, 3) ORDER BY id";
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                trees.Add(new Tree { Id = rdr.GetInt32(0), Name = rdr.GetString(1) });
        }

        // Groups (only for our Trees)
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, treeid, name FROM BPAGroup WHERE treeid IN (2, 3)";
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                groups.Add(new Group
                {
                    Id = rdr.GetGuid(0),
                    TreeId = rdr.GetInt32(1),
                    Name = rdr.GetString(2),
                    IsRestricted = false
                });
        }

        // Memberships (M:N Process-Group). Use a single parameter containing the
        // comma-separated group IDs and SQL Server's STRING_SPLIT, so the IN-list
        // comes from a typed parameter (no string concatenation). Mirrors the
        // Parameterized group-id lookup for the selected BP trees.
        if (groups.Count > 0)
        {
            var groupIdsCsv = string.Join(",", groups.Select(g => g.Id));
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT groupid, processid
                    FROM BPAGroupProcess
                    WHERE groupid IN (SELECT TRY_CONVERT(uniqueidentifier, value) FROM STRING_SPLIT(@groupIdsCsv, ','))";
                cmd.Parameters.AddWithValue("@groupIdsCsv", groupIdsCsv);
                await using var rdr = await cmd.ExecuteReaderAsync(ct);
                while (await rdr.ReadAsync(ct))
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

public sealed record BpProcessRow(Guid ProcessId, string Name, string? XmlContent);