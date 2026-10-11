namespace DESD.LifeHub.Core.Entities;

/// <summary>In-App-Benachrichtigung, entsteht aus ausgelösten Erinnerungen.</summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }

    /// <summary>Relativer Zielpfad, z. B. <c>/Tasks/Details?id=…</c>.</summary>
    public string? Url { get; set; }

    public bool IsRead { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>Quell-Erinnerung; Ursache für die Deduplizierung.</summary>
    public Guid? ReminderId { get; set; }

    public Reminder? Reminder { get; set; }
}
