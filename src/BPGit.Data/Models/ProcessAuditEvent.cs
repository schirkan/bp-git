using System;

namespace BPGit.Data.Models;

/// <summary>
/// Read-only view of BPAAuditEvents (BP's per-edit log) joined with BPAUser for the
/// acting user's username. BPAAuditEvents is written by the BP runtime for every
/// action (login, license, process import, release overwrite, etc.) and includes
/// oldXML + newXML columns for full version reconstruction.
///
/// Event-type sCode values observed in demo DB:
///   L001 = Login (user logged in)
///   S002 = System license change
///   P004 = Process overwritten by importing a release
///   P006 = Process import (single file via /import or BP Studio)
/// </summary>
public class ProcessAuditEvent
{
    public int EventId { get; set; }
    public DateTime EventDateTime { get; set; }
    public string? SCode { get; set; }
    public string? SNarrative { get; set; }
    public Guid SrcUserId { get; set; }
    public string? Username { get; set; }
    public Guid? TgtProcId { get; set; }
    public string? TgtProcName { get; set; }
    public Guid? TgtResourceId { get; set; }
    public string? EditSummary { get; set; }
    public string? Comments { get; set; }
    public bool HasOldXml { get; set; }
    public bool HasNewXml { get; set; }
}
