using DESD.LifeHub.Core.Enums;

namespace DESD.LifeHub.Core.Entities;

/// <summary>
/// Kalendertermin. Alle Zeitangaben werden in UTC gespeichert; die Anzeige erfolgt
/// in der in <see cref="DisplayTimeZoneId"/> hinterlegten Zeitzone.
/// </summary>
public class CalendarEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerUserId { get; set; }

    public ApplicationUser? Owner { get; set; }

    public Guid? OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Location { get; set; }

    /// <summary>Inklusiver Beginn in UTC.</summary>
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>Exklusives Ende in UTC. Serverseitig auf > StartsAt validiert.</summary>
    public DateTimeOffset EndsAt { get; set; }

    public bool IsAllDay { get; set; }

    /// <summary>
    /// Farbkategorie als Schlüssel der Palette (z. B. "blue", "green"). Leer bedeutet
    /// die Standardfarbe. Die Zuordnung zu konkreten Farben erfolgt in der Oberfläche.
    /// </summary>
    public string? ColorCategory { get; set; }

    /// <summary>Optionaler Besprechungsraum der Organisation.</summary>
    public Guid? RoomId { get; set; }

    public Room? Room { get; set; }

    // ------------------------------------------------------------------ Serie
    // Ein wiederkehrender Termin wird als ein Datensatz gespeichert. Beim Anzeigen
    // werden die Vorkommen im gewünschten Zeitraum serverseitig berechnet.

    public RecurrenceFrequency RecurrenceFrequency { get; set; } = RecurrenceFrequency.None;

    /// <summary>Abstand zwischen zwei Vorkommen (>= 1).</summary>
    public int RecurrenceInterval { get; set; } = 1;

    /// <summary>Letzter zulässiger Tag der Serie (UTC, inklusiv). Null = unbegrenzt.</summary>
    public DateTimeOffset? RecurrenceUntil { get; set; }

    /// <summary>
    /// IANA-Zeitzone, in der der Termin angelegt wurde. Ganztägige Termine werden
    /// bei der Anzeige in dieser Zone auf Tagesgrenzen gelegt.
    /// </summary>
    public string DisplayTimeZoneId { get; set; } = "Europe/Berlin";

    /// <summary>Optionale Verknüpfung zu einer Aufgabe (Fälligkeiten).</summary>
    public Guid? TaskItemId { get; set; }

    public TaskItem? TaskItem { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<Reminder> Reminders { get; set; } = new List<Reminder>();

    public ICollection<CalendarEventParticipant> Participants { get; set; } = new List<CalendarEventParticipant>();

    public bool IsRecurring => RecurrenceFrequency != RecurrenceFrequency.None;
}

/// <summary>Teilnehmer eines Termins inklusive Antwortstatus.</summary>
public class CalendarEventParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CalendarEventId { get; set; }

    public CalendarEvent? CalendarEvent { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public ParticipantResponse Response { get; set; } = ParticipantResponse.Invited;

    /// <summary>Organisator ist der Ersteller des Termins.</summary>
    public bool IsOrganizer { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? RespondedAt { get; set; }
}
