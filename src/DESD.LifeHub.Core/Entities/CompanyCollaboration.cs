using DESD.LifeHub.Core.Enums;

namespace DESD.LifeHub.Core.Entities;

/// <summary>
/// Firmenankündigung. Autoren sind Manager/Owner einer Organisation; sichtbar ist sie
/// für alle aktiven Mitglieder, solange sie veröffentlicht und nicht abgelaufen ist.
/// </summary>
public class CompanyAnnouncement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    public Guid AuthorUserId { get; set; }

    public ApplicationUser? Author { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public AnnouncementPriority Priority { get; set; } = AnnouncementPriority.Normal;

    /// <summary>Angeheftete Ankündigungen erscheinen zuerst.</summary>
    public bool IsPinned { get; set; }

    /// <summary>Ab wann die Ankündigung sichtbar ist (UTC).</summary>
    public DateTimeOffset PublishAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Optionales Ablaufdatum (UTC).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Abwesenheitsantrag (Urlaub, Krankheit, Homeoffice …) im Firmenkontext.</summary>
public class AbsenceRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    /// <summary>Erster Abwesenheitstag (inklusive, lokales Datum).</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Letzter Abwesenheitstag (inklusive, lokales Datum).</summary>
    public DateOnly EndDate { get; set; }

    public AbsenceType Type { get; set; } = AbsenceType.Vacation;

    public AbsenceStatus Status { get; set; } = AbsenceStatus.Requested;

    public string? Note { get; set; }

    public Guid? DecidedByUserId { get; set; }

    public ApplicationUser? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int DayCount => EndDate.DayNumber - StartDate.DayNumber + 1;
}
