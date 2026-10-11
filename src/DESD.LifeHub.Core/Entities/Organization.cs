using DESD.LifeHub.Core.Enums;

namespace DESD.LifeHub.Core.Entities;

/// <summary>
/// Mandant. Jeder Benutzer besitzt genau eine persönliche Organisation; Firmen
/// können zusätzliche Organisationen mit mehreren Mitgliedern anlegen.
/// Inhalte ohne <c>OrganizationId</c> sind strikt privat.
/// </summary>
public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Eindeutiger, URL-tauglicher Kurzname.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Persönlicher Bereich: genau ein Mitglied, keine Einladungen.</summary>
    public bool IsPersonal { get; set; }

    public Guid OwnerUserId { get; set; }

    public ApplicationUser? Owner { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<OrganizationMember> Members { get; set; } = new List<OrganizationMember>();

    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();

    public ICollection<Note> Notes { get; set; } = new List<Note>();

    public ICollection<CalendarEvent> Events { get; set; } = new List<CalendarEvent>();

    public ICollection<Room> Rooms { get; set; } = new List<Room>();

    public ICollection<CompanyAnnouncement> Announcements { get; set; } = new List<CompanyAnnouncement>();

    public ICollection<AbsenceRequest> AbsenceRequests { get; set; } = new List<AbsenceRequest>();
}

/// <summary>Mitgliedschaft eines Benutzers in einer Organisation.</summary>
public class OrganizationMember
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public OrganizationRole Role { get; set; } = OrganizationRole.Member;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? InvitedByUserId { get; set; }
}
