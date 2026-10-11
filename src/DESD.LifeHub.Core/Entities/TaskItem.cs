using DESD.LifeHub.Core.Enums;

namespace DESD.LifeHub.Core.Entities;

/// <summary>
/// Aufgabe. <c>OrganizationId == null</c> bedeutet: ausschliesslich privat,
/// nur für <see cref="OwnerUserId"/> sichtbar.
/// </summary>
public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Ersteller/verantwortlicher Benutzer.</summary>
    public Guid OwnerUserId { get; set; }

    public ApplicationUser? Owner { get; set; }

    /// <summary>Persönlicher Bereich (null) oder gemeinsamer Organisationsbereich.</summary>
    public Guid? OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    /// <summary>Optionale Zuweisung an ein anderes Organisationsmitglied.</summary>
    public Guid? AssignedToUserId { get; set; }

    public ApplicationUser? AssignedTo { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TaskItemStatus Status { get; set; } = TaskItemStatus.Open;

    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    public string? Category { get; set; }

    /// <summary>Komma-getrennte Schlagwörter, normalisiert in Kleinschreibung.</summary>
    public string? Tags { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? DueAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<Note> Notes { get; set; } = new List<Note>();

    public ICollection<Reminder> Reminders { get; set; } = new List<Reminder>();

    public ICollection<TaskAttachment> Attachments { get; set; } = new List<TaskAttachment>();

    public ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();

    public bool IsOverdue(DateTimeOffset now) =>
        DueAt.HasValue
        && DueAt.Value < now
        && Status is not (TaskItemStatus.Done or TaskItemStatus.Cancelled);
}
