using System;
using System.Collections.Generic;

namespace BPGit.Data.Models;

/// <summary>
/// Read-only view of BPATree (folder-structure root node). Per #6287, bpgit only cares
/// about Trees named "Processes" (id=2) and "Objects" (id=3) — the other 4 Trees
/// (Tiles, Queues, Resources, users) are BP-Studio-specific and excluded.
/// </summary>
public class Tree
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// Read-only view of BPAGroup (sub-folder within a Tree). Folder-name is unique within
/// a Tree (single UNIQUE constraint UNQ_BPAGroup_name on name+treeid).
/// Per #6287: folders can be nested via BPAGroupGroup(groupid, memberid).
/// </summary>
public class Group
{
    public Guid Id { get; set; }
    public int TreeId { get; set; }
    public string Name { get; set; } = "";
    public bool IsRestricted { get; set; }
}

/// <summary>
/// M:N mapping between BPAProcess and BPAGroup. A single Process can live in multiple
/// Groups, and a single Group can contain multiple Processes.
/// Per #6289: in worktree, this leads to file duplication — same XML in multiple folder paths.
/// </summary>
public class ProcessMembership
{
    public Guid ProcessId { get; set; }
    public Guid GroupId { get; set; }
}

/// <summary>
/// Composite structure returned by ProcessRepository.GetFolderStructureAsync():
/// filtered Trees (Processes + Objects only) + their Groups + M:N Memberships.
/// </summary>
public record FolderStructure(
    List<Tree> Trees,
    List<Group> Groups,
    List<ProcessMembership> Memberships);
