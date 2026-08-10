using BPGit.Data.Connection;
using BPGit.Data.Models;
using Dapper;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BPGit.Data.Repositories;

public class ProcessRepository
{
    private const string SelectColumns = @"
        SELECT processid, ProcessType, name, description, version,
               createdate, createdby, lastmodifieddate, lastmodifiedby,
               AttributeID, processxml, runmode, sharedObject,
               forceLiteralForm, useLegacyNamespace, hasStartupParameters, wspublishname
        FROM BPAProcess";

    private readonly ConnectionFactory _factory;

    public ProcessRepository(ConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<Process>> ListAllAsync(string? processType = null, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        var sql = SelectColumns;
        if (processType != null) sql += " WHERE ProcessType = @processType";
        sql += " ORDER BY name";
        var rows = await conn.QueryAsync<Process>(new CommandDefinition(sql, new { processType }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Process?> FindByIdAsync(System.Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        var sql = SelectColumns + " WHERE processid = @id";
        return await conn.QueryFirstOrDefaultAsync<Process>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }
}
