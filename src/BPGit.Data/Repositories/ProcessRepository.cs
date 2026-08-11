using BPGit.Data.Connection;
using BPGit.Data.Models;
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

    /// <summary>
    /// BP-side per-edit audit history. BPAAuditEvents is written by the BP runtime
    /// for every action (login, license change, process import, release overwrite, etc.)
    /// and includes oldXML + newXML columns for full version reconstruction.
    /// LEFT JOIN BPAUser (SrcUserID has no FK in BP's schema, so users can be
    /// orphaned after deletions) for the acting-user display.
    /// LEFT JOIN BPAProcess for the target process name when TgtProcID is set.
    /// </summary>
    public async Task<IReadOnlyList<ProcessAuditEvent>> GetAuditHistoryAsync(
        int limit,
        Guid? processId = null,
        DateTime? since = null,
        string? sCode = null,
        CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.OpenAsync(ct);

        var sql = @"
            SELECT TOP (@limit)
                a.eventid             AS EventId,
                a.eventdatetime       AS EventDateTime,
                a.sCode               AS SCode,
                a.sNarrative          AS SNarrative,
                a.gSrcUserID          AS SrcUserId,
                u.username            AS Username,
                a.gTgtProcID          AS TgtProcId,
                p.name                AS TgtProcName,
                a.gTgtResourceID      AS TgtResourceId,
                a.EditSummary         AS EditSummary,
                a.comments            AS Comments,
                CASE WHEN a.oldXML IS NOT NULL THEN 1 ELSE 0 END AS HasOldXml,
                CASE WHEN a.newXML IS NOT NULL THEN 1 ELSE 0 END AS HasNewXml
            FROM dbo.BPAAuditEvents a
            LEFT JOIN dbo.BPAUser u ON u.userid = a.gSrcUserID
            LEFT JOIN dbo.BPAProcess p ON p.processid = a.gTgtProcID
            WHERE 1 = 1
                AND (@processId IS NULL OR a.gTgtProcID = @processId)
                AND (@since    IS NULL OR a.eventdatetime >= @since)
                AND (@sCode    IS NULL OR a.sCode = @sCode)
            ORDER BY a.eventdatetime DESC, a.eventid DESC";

        var rows = await conn.QueryAsync<ProcessAuditEvent>(
            new CommandDefinition(
                sql,
                new { limit, processId, since, sCode },
                cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// Loads the oldXML + newXML for a specific audit event (full payload for diff
    /// or rollback scenarios). oldXML can be NULL for "create" events (e.g. P006 on
    /// a brand-new process); newXML contains the full XML after the action.
    /// </summary>
    public async Task<AuditXmlPayload?> GetAuditXmlAsync(int eventId, CancellationToken ct = default)
    {
        using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        const string sql = @"
            SELECT oldXML AS OldXml, newXML AS NewXml
            FROM dbo.BPAAuditEvents
            WHERE eventid = @eventId";
        return await conn.QueryFirstOrDefaultAsync<AuditXmlPayload>(
            new CommandDefinition(sql, new { eventId }, cancellationToken: ct));
    }

    public class AuditXmlPayload
    {
        public string? OldXml { get; set; }
        public string? NewXml { get; set; }
    }
}
