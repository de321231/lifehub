namespace DESD.LifeHub.Core.Entities;

public enum WorkspaceType
{
    Personal = 0,
    Company = 1
}

/// <summary>
/// Workspace für Privatnutzer (Personal) oder Unternehmen (Company).
/// Funktionen nutzen denselben Code; Berechtigungen und Sichtbarkeit hängen vom Workspace ab.
/// </summary>
public class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Eindeutige URL-Kennung für Firmen-Workspaces (z. B. "avw-k9x3").</summary>
    public string? CompanyId { get; set; }

    public WorkspaceType Type { get; set; } = WorkspaceType.Personal;

    /// <summary>Besitzer des Workspaces (z. B. Firmenadmin).</summary>
    public Guid OwnerUserId { get; set; }

    public ApplicationUser? Owner { get; set; }

    /// <summary>Standardorganisation für den Workspace (optional).</summary>
    public Guid? OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<WorkspaceMembership> Memberships { get; set; } = new List<WorkspaceMembership>();
}

/// <summary>Mitgliedschaft eines Benutzers in einem Workspace.</summary>
public class WorkspaceMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Workspace? Workspace { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}
