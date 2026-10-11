namespace DESD.LifeHub.Core.Entities;

/// <summary>
/// Besprechungsraum. Räume gehören zu einer Organisation (Firma); ein persönlicher
/// Bereich besitzt in der Regel keine Räume.
/// </summary>
public class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Location { get; set; }

    /// <summary>Optionale Sitzplatzkapazität.</summary>
    public int? Capacity { get; set; }

    /// <summary>Farbkategorie für die Kalenderanzeige.</summary>
    public string? ColorCategory { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Kalenderfreigabe: gewährt einem anderen Benutzer Einblick in den eigenen Kalender.</summary>
public class CalendarShare
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Eigentümer des freigegebenen Kalenders.</summary>
    public Guid OwnerUserId { get; set; }

    public ApplicationUser? Owner { get; set; }

    /// <summary>Benutzer, der den Kalender einsehen darf.</summary>
    public Guid SharedWithUserId { get; set; }

    public ApplicationUser? SharedWith { get; set; }

    /// <summary>Lesen erlaubt immer; Schreiben optional.</summary>
    public bool CanEdit { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
