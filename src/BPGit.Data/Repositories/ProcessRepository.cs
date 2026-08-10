using BPGit.Data.Connection;
using Dapper;
using System;
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

    public async Task<Process?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        var sql = SelectColumns + " WHERE processid = @id";
        return await conn.QueryFirstOrDefaultAsync<Process>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    /// <summary>
    /// UPSERT for BPAProcess (head row + processxml). Idempotent on identical content
    /// via caller-side hash check; this method always writes when called.
    /// </summary>
    public async Task<int> UpdateAsync(Process process, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        const string sql = @"
            UPDATE BPAProcess SET
                ProcessType           = @ProcessType,
                name                  = @name,
                description           = @description,
                version               = @version,
                lastmodifieddate      = @lastmodifieddate,
                lastmodifiedby        = @lastmodifiedby,
                AttributeID           = @AttributeID,
                processxml            = @processxml,
                runmode               = @runmode,
                sharedObject          = @sharedObject,
                forceLiteralForm      = @forceLiteralForm,
                useLegacyNamespace    = @useLegacyNamespace,
                hasStartupParameters  = @hasStartupParameters,
                wspublishname         = @wspublishname
            WHERE processid = @processid";
        return await conn.ExecuteAsync(new CommandDefinition(sql, process, cancellationToken: ct));
    }

    /// <summary>
    /// Returns the LockedBy GUID if the process is currently locked, null otherwise.
    /// Used by commit to short-circuit when someone else has the process open.
    /// </summary>
    public async Task<Guid?> GetLockOwnerAsync(Guid processId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        const string sql = "SELECT userid FROM BPAProcessLock WHERE ProcessID = @processId";
        return await conn.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(sql, new { processId }, cancellationToken: ct));
    }
}
