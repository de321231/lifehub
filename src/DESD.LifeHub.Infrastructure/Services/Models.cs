using DESD.LifeHub.Core.Enums;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>Einheitliches Ergebnis einer Schreiboperation der Fachservices.</summary>
public sealed record ServiceResult
{
    public required bool Succeeded { get; init; }
    public Guid? Id { get; init; }
    public string? Error { get; init; }

    /// <summary>Feldbezogene Fehler für die Formularvalidierung.</summary>
    public IReadOnlyDictionary<string, string> FieldErrors { get; init; } = new Dictionary<string, string>();

    public static ServiceResult Ok(Guid? id = null) => new() { Succeeded = true, Id = id };

    public static ServiceResult Fail(string error, IReadOnlyDictionary<string, string>? fieldErrors = null)
        => new() { Succeeded = false, Error = error, FieldErrors = fieldErrors ?? new Dictionary<string, string>() };
}

public enum TaskSort
{
    DueAscending = 0,
    DueDescending = 1,
    CreatedDescending = 2,
    CreatedAscending = 3,
    PriorityDescending = 4,
    TitleAscending = 5,
}

public sealed record TaskFilter
{
    public string? Search { get; init; }
    public TaskItemStatus? Status { get; init; }
    public TaskPriority? Priority { get; init; }
    public string? Category { get; init; }
    public string? Tag { get; init; }

    /// <summary>null = alle sichtbaren Bereiche; Guid = genau diese Organisation.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>Nur Einträge, für die der Benutzer Ersteller ist.</summary>
    public bool OnlyMine { get; init; }

    public bool OnlyOverdue { get; init; }
    public bool OnlyOpen { get; init; }
    public TaskSort Sort { get; init; } = TaskSort.DueAscending;
}

public sealed record TaskInput
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public TaskItemStatus Status { get; init; } = TaskItemStatus.Open;
    public TaskPriority Priority { get; init; } = TaskPriority.Normal;
    public string? Category { get; init; }
    public string? Tags { get; init; }

    /// <summary>Fälligkeit in UTC.</summary>
    public DateTimeOffset? DueAt { get; init; }

    public Guid? OrganizationId { get; init; }
    public Guid? AssignedToUserId { get; init; }

    /// <summary>Vorlaufzeit für die automatische Erinnerung; null = Standard aus Konfiguration.</summary>
    public int? ReminderLeadMinutes { get; init; }

    /// <summary>Soll überhaupt eine Erinnerung angelegt werden?</summary>
    public bool CreateReminder { get; init; }
}

public sealed record NoteInput
{
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string? Tags { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsArchived { get; init; }
    public Guid? OrganizationId { get; init; }
    public bool IsSharedWithOrganization { get; init; }
    public Guid? TaskItemId { get; init; }
}

public sealed record NoteFilter
{
    public string? Search { get; init; }
    public string? Category { get; init; }
    public string? Tag { get; init; }
    public bool OnlyFavorites { get; init; }
    public bool IncludeArchived { get; init; }
    public bool OnlyArchived { get; init; }
}

public sealed record EventInput
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Location { get; init; }

    /// <summary>Beginn in UTC.</summary>
    public DateTimeOffset StartsAt { get; init; }

    /// <summary>Ende in UTC (exklusiv).</summary>
    public DateTimeOffset EndsAt { get; init; }

    public bool IsAllDay { get; init; }

    /// <summary>Farbkategorie-Schlüssel (Palette) oder null für Standard.</summary>
    public string? ColorCategory { get; init; }

    public Guid? RoomId { get; init; }

    public RecurrenceFrequency RecurrenceFrequency { get; init; } = RecurrenceFrequency.None;

    /// <summary>Abstand zwischen den Vorkommen (>= 1).</summary>
    public int RecurrenceInterval { get; init; } = 1;

    /// <summary>Serienende (UTC, inklusiv) oder null für unbegrenzt.</summary>
    public DateTimeOffset? RecurrenceUntil { get; init; }

    public string DisplayTimeZoneId { get; init; } = "Europe/Berlin";
    public Guid? OrganizationId { get; init; }
    public Guid? TaskItemId { get; init; }
    public bool CreateReminder { get; init; }
    public int? ReminderLeadMinutes { get; init; }

    /// <summary>Einzuladende Teilnehmer (Organisationsmitglieder). Nur bei Organisationsterminen.</summary>
    public IReadOnlyCollection<Guid> ParticipantUserIds { get; init; } = [];
}

public sealed record RoomInput
{
    public Guid OrganizationId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Location { get; init; }
    public int? Capacity { get; init; }
    public string? ColorCategory { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record AnnouncementInput
{
    public Guid OrganizationId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public AnnouncementPriority Priority { get; init; } = AnnouncementPriority.Normal;
    public bool IsPinned { get; init; }
    public DateTimeOffset PublishAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record AbsenceInput
{
    public Guid OrganizationId { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public AbsenceType Type { get; init; } = AbsenceType.Vacation;
    public string? Note { get; init; }
}
