namespace DESD.LifeHub.Core.Entities;

/// <summary>
/// Anhang einer Aufgabe. Die Datei selbst liegt ausserhalb der Datenbank in einem
/// konfigurierten Verzeichnis; <see cref="StorageKey"/> benennt sie dort eindeutig.
/// Dateiname und Inhaltstyp stammen ausschliesslich vom Server.
/// </summary>
public class TaskAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TaskItemId { get; set; }

    public TaskItem? TaskItem { get; set; }

    /// <summary>Anzeigename (bereinigt, ohne Pfadanteile).</summary>
    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    /// <summary>Eindeutiger, serverseitig erzeugter Speicherschlüssel (kein Benutzerpfad).</summary>
    public string StorageKey { get; set; } = string.Empty;

    public Guid UploadedByUserId { get; set; }

    public ApplicationUser? UploadedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Kommentar zu einer Aufgabe; nur für berechtigte Betrachter sichtbar.</summary>
public class TaskComment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TaskItemId { get; set; }

    public TaskItem? TaskItem { get; set; }

    public Guid AuthorUserId { get; set; }

    public ApplicationUser? Author { get; set; }

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
