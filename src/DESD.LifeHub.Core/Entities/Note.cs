namespace DESD.LifeHub.Core.Entities;

/// <summary>
/// Notiz. Standardmässig privat (<c>OrganizationId == null</c>). Wird nur dann für
/// eine Organisation sichtbar, wenn <see cref="IsSharedWithOrganization"/> gesetzt ist
/// und die Notiz einer Organisation zugeordnet wurde.
/// </summary>
public class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerUserId { get; set; }

    public ApplicationUser? Owner { get; set; }

    public Guid? OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? Category { get; set; }

    /// <summary>Komma-getrennte Schlagwörter.</summary>
    public string? Tags { get; set; }

    public bool IsFavorite { get; set; }

    public bool IsArchived { get; set; }

    /// <summary>Nur gemeinsam genutzte Notizen sind für Organisationsmitglieder sichtbar.</summary>
    public bool IsSharedWithOrganization { get; set; }

    /// <summary>Optionale Verknüpfung der Notiz mit einer Aufgabe.</summary>
    public Guid? TaskItemId { get; set; }

    public TaskItem? TaskItem { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
