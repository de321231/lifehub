using DESD.LifeHub.Core.Enums;

namespace DESD.LifeHub.Core.Entities;

/// <summary>
/// Erinnerung zu einer Aufgabe oder einem Termin. Wird vom Hintergrunddienst
/// genau einmal ausgelöst (atomare Statusbeanspruchung) und erzeugt eine
/// <see cref="Notification"/>.
/// </summary>
public class Reminder
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Message { get; set; }

    /// <summary>Auslösezeitpunkt in UTC.</summary>
    public DateTimeOffset RemindAt { get; set; }

    /// <summary>Zeitzone des Benutzers zum Zeitpunkt der Erstellung (Anzeige).</summary>
    public string TimeZoneId { get; set; } = "Europe/Berlin";

    public ReminderStatus Status { get; set; } = ReminderStatus.Pending;

    public ReminderChannel Channel { get; set; } = ReminderChannel.InApp;

    public Guid? TaskItemId { get; set; }

    public TaskItem? TaskItem { get; set; }

    public Guid? CalendarEventId { get; set; }

    public CalendarEvent? CalendarEvent { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? TriggeredAt { get; set; }

    public DateTimeOffset? DismissedAt { get; set; }

    /// <summary>
    /// Kennzeichnet, ob die Benachrichtigung erfolgreich zugestellt wurde. Für den
    /// In-App-Kanal bedeutet das: <see cref="Notification"/> wurde geschrieben.
    /// </summary>
    public bool Delivered { get; set; }

    /// <summary>Grund für <see cref="ReminderStatus.Failed"/> (ohne Geheimnisse).</summary>
    public string? FailureReason { get; set; }
}
