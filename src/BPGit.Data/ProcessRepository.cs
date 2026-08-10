using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;

namespace BPGit.Data;

public class ProcessRepository
{
    private readonly string _connectionString;

    public ProcessRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<Process>> ListAllAsync(string? processType = null, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var sql = @"SELECT processid, ProcessType, name, description, version, createdate, createdby, lastmodifieddate, lastmodifiedby, AttributeID, processxml, runmode, sharedObject, forceLiteralForm, useLegacyNamespace, hasStartupParameters, wspublishname FROM BPAProcess";
        if (processType != null) sql += " WHERE ProcessType = @processType";
        sql += " ORDER BY name";
        var result = await conn.QueryAsync<Process>(new CommandDefinition(sql, new { processType }, cancellationToken: ct));
        return result.ToList();
    }

    public async Task<Process?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var sql = @"SELECT processid, ProcessType, name, description, version, createdate, createdby, lastmodifieddate, lastmodifiedby, AttributeID, processxml, runmode, sharedObject, forceLiteralForm, useLegacyNamespace, hasStartupParameters, wspublishname FROM BPAProcess WHERE processid = @id";
        return await conn.QueryFirstOrDefaultAsync<Process>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }
}
