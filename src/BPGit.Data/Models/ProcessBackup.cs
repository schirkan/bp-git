using System;

namespace BPGit.Data.Models;

/// <summary>
/// Read-only view of BPAProcessBackup joined with BPAProcess (name) and
/// BPAUser (username). Backup rows are written by the BP runtime when a
/// process is saved, so they serve as the BP-side history timeline.
/// </summary>
public class ProcessBackup
{
    public Guid ProcessId { get; set; }
    public string? Name { get; set; }
    public DateTime? BackupDate { get; set; }
    public Guid? UserId { get; set; }
    public string? Username { get; set; }
    public bool HasCompressedXml { get; set; }
    public bool HasXml { get; set; }
}
