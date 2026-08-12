using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace BPGit.Server.Services;

/// <summary>
/// DB-Wrapper fuer BP-DB-Lookups (BPAProcess + BPAProcessLock).
/// Wird vom pre-receive Hook verwendet, um <c>processid</c> via Name zu ermitteln
/// und Lock-Konflikte vor dem <c>/import</c> zu erkennen.
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
}

public sealed record BpaProcessLockInfo(
    DateTime LockDateTime,
    Guid UserId,
    string? MachineName,
    string? Username);