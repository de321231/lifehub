using DESD.LifeHub.Core.Abstractions;
using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Data;
using DESD.LifeHub.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>
/// Erinnerungsdienst. Erzeugt Erinnerungen, löst sie genau einmal aus und erzeugt
/// daraus In-App-Benachrichtigungen.
///
/// Doppelte Benachrichtigungen werden durch eine atomare Statusbeanspruchung
/// (Pending → Triggered per bedingtem UPDATE) verhindert. Auch bei parallelen
/// Instanzen kann eine Erinnerung daher nur einmal ausgelöst werden.
///
/// E-Mail-Benachrichtigungen sind vorbereitet: Meldungen werden an
/// <see cref="IEmailSender"/> übergeben, der ohne konfigurierten Provider nur
/// protokolliert und ausdrücklich als "nicht zugestellt" markiert.
/// </summary>
public interface IReminderService
{
    Task<ServiceResult> CreateManualAsync(
        Guid userId,
        string title,
        string? message,
        DateTimeOffset remindAtUtc,
        Guid? taskItemId = null,
        Guid? calendarEventId = null,
        string timeZoneId = "Europe/Berlin",
        CancellationToken ct = default);

    Task SyncForTaskAsync(Guid userId, TaskItem task, int? leadMinutes, bool enabled, CancellationToken ct = default);

    Task CreateForEventAsync(Guid userId, CalendarEvent calendarEvent, int? leadMinutes, CancellationToken ct = default);

    Task<IReadOnlyList<Reminder>> ListUpcomingAsync(Guid userId, int count = 20, CancellationToken ct = default);

    Task<IReadOnlyList<Reminder>> ListTriggeredAsync(Guid userId, int count = 50, CancellationToken ct = default);

    Task<Reminder?> GetAsync(Guid userId, Guid reminderId, CancellationToken ct = default);

    Task<ServiceResult> DismissAsync(Guid userId, Guid reminderId, CancellationToken ct = default);

    Task<ServiceResult> SnoozeAsync(Guid userId, Guid reminderId, int minutes, CancellationToken ct = default);

    Task<ServiceResult> DeleteAsync(Guid userId, Guid reminderId, CancellationToken ct = default);

    Task<int> CountPendingAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Beansprucht alle fälligen Erinnerungen atomar und erzeugt die zugehörigen
    /// Benachrichtigungen. Rückgabe: Anzahl tatsächlich ausgelöster Erinnerungen.
    /// </summary>
    Task<int> ProcessDueAsync(int batchSize, CancellationToken ct = default);
}

public sealed class ReminderService : IReminderService
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly IEmailSender _email;
    private readonly ReminderOptions _options;
    private readonly ILogger<ReminderService> _logger;

    public ReminderService(
        AppDbContext db,
        IClock clock,
        IEmailSender email,
        IOptions<ReminderOptions> options,
        ILogger<ReminderService> logger)
    {
        _db = db;
        _clock = clock;
        _email = email;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServiceResult> CreateManualAsync(
        Guid userId,
        string title,
        string? message,
        DateTimeOffset remindAtUtc,
        Guid? taskItemId = null,
        Guid? calendarEventId = null,
        string timeZoneId = "Europe/Berlin",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return ServiceResult.Fail("Bitte geben Sie einen Titel für die Erinnerung an.");
        }

        var reminder = new Reminder
        {
            UserId = userId,
            Title = title.Trim(),
            Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
            RemindAt = TimeUtil.ToUtc(remindAtUtc),
            TimeZoneId = timeZoneId,
            TaskItemId = taskItemId,
            CalendarEventId = calendarEventId,
            Status = ReminderStatus.Pending,
            Channel = ReminderChannel.InApp,
            CreatedAt = _clock.UtcNow,
        };

        _db.Reminders.Add(reminder);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(reminder.Id);
    }

    public async Task SyncForTaskAsync(Guid userId, TaskItem task, int? leadMinutes, bool enabled, CancellationToken ct = default)
    {
        // Vorhandene, noch nicht ausgelöste Erinnerungen dieser Aufgabe ersetzen.
        await _db.Reminders
            .Where(r => r.TaskItemId == task.Id && r.Status == ReminderStatus.Pending)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        if (!enabled || task.DueAt is null)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var lead = Math.Clamp(leadMinutes ?? _options.DefaultLeadMinutes, 0, 60 * 24 * 30);
        var remindAt = task.DueAt.Value.AddMinutes(-lead);

        _db.Reminders.Add(new Reminder
        {
            UserId = userId,
            Title = task.Title,
            Message = $"Aufgabe fällig: {task.Title}",
            RemindAt = remindAt,
            TaskItemId = task.Id,
            Status = ReminderStatus.Pending,
            Channel = ReminderChannel.InApp,
            CreatedAt = _clock.UtcNow,
        });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task CreateForEventAsync(Guid userId, CalendarEvent calendarEvent, int? leadMinutes, CancellationToken ct = default)
    {
        await _db.Reminders
            .Where(r => r.CalendarEventId == calendarEvent.Id && r.Status == ReminderStatus.Pending)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        var lead = Math.Clamp(leadMinutes ?? _options.DefaultLeadMinutes, 0, 60 * 24 * 30);

        _db.Reminders.Add(new Reminder
        {
            UserId = userId,
            Title = calendarEvent.Title,
            Message = string.IsNullOrWhiteSpace(calendarEvent.Location)
                ? "Termin beginnt bald."
                : $"Termin beginnt bald – Ort: {calendarEvent.Location}",
            RemindAt = calendarEvent.StartsAt.AddMinutes(-lead),
            CalendarEventId = calendarEvent.Id,
            TimeZoneId = calendarEvent.DisplayTimeZoneId,
            Status = ReminderStatus.Pending,
            Channel = ReminderChannel.InApp,
            CreatedAt = _clock.UtcNow,
        });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Reminder>> ListUpcomingAsync(Guid userId, int count = 20, CancellationToken ct = default)
        => await _db.Reminders
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.Status == ReminderStatus.Pending)
            .OrderBy(r => r.RemindAt)
            .Take(Math.Clamp(count, 1, 200))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Reminder>> ListTriggeredAsync(Guid userId, int count = 50, CancellationToken ct = default)
        => await _db.Reminders
            .AsNoTracking()
            .Where(r => r.UserId == userId
                        && (r.Status == ReminderStatus.Triggered || r.Status == ReminderStatus.Dismissed))
            .OrderByDescending(r => r.TriggeredAt ?? r.RemindAt)
            .Take(Math.Clamp(count, 1, 200))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<Reminder?> GetAsync(Guid userId, Guid reminderId, CancellationToken ct = default)
        => await _db.Reminders
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == reminderId && r.UserId == userId, ct)
            .ConfigureAwait(false);

    public async Task<ServiceResult> DismissAsync(Guid userId, Guid reminderId, CancellationToken ct = default)
    {
        var affected = await _db.Reminders
            .Where(r => r.Id == reminderId && r.UserId == userId && r.Status == ReminderStatus.Triggered)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReminderStatus.Dismissed)
                .SetProperty(r => r.DismissedAt, _clock.UtcNow), ct)
            .ConfigureAwait(false);

        return affected > 0
            ? ServiceResult.Ok(reminderId)
            : ServiceResult.Fail("Die Erinnerung wurde nicht gefunden oder ist bereits erledigt.");
    }

    public async Task<ServiceResult> SnoozeAsync(Guid userId, Guid reminderId, int minutes, CancellationToken ct = default)
    {
        var reminder = await _db.Reminders
            .FirstOrDefaultAsync(r => r.Id == reminderId && r.UserId == userId, ct)
            .ConfigureAwait(false);

        if (reminder is null)
        {
            return ServiceResult.Fail("Die Erinnerung wurde nicht gefunden.");
        }

        reminder.RemindAt = _clock.UtcNow.AddMinutes(Math.Clamp(minutes, 1, 60 * 24 * 30));
        reminder.Status = ReminderStatus.Pending;
        reminder.TriggeredAt = null;
        reminder.DismissedAt = null;
        reminder.Delivered = false;
        reminder.FailureReason = null;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(reminderId);
    }

    public async Task<ServiceResult> DeleteAsync(Guid userId, Guid reminderId, CancellationToken ct = default)
    {
        var affected = await _db.Reminders
            .Where(r => r.Id == reminderId && r.UserId == userId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        return affected > 0
            ? ServiceResult.Ok(reminderId)
            : ServiceResult.Fail("Die Erinnerung wurde nicht gefunden.");
    }

    public Task<int> CountPendingAsync(Guid userId, CancellationToken ct = default)
        => _db.Reminders.AsNoTracking().CountAsync(
            r => r.UserId == userId && r.Status == ReminderStatus.Pending && r.RemindAt <= _clock.UtcNow, ct);

    public async Task<int> ProcessDueAsync(int batchSize, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var candidateIds = await _db.Reminders
            .AsNoTracking()
            .Where(r => r.Status == ReminderStatus.Pending && r.RemindAt <= now)
            .OrderBy(r => r.RemindAt)
            .Take(Math.Clamp(batchSize, 1, 2000))
            .Select(r => r.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (candidateIds.Count == 0)
        {
            return 0;
        }

        var claimedIds = new List<Guid>(candidateIds.Count);
        foreach (var id in candidateIds)
        {
            // Bedingtes UPDATE: nur wer den Status von Pending auf Triggered setzt,
            // darf die Erinnerung auslösen. Verhindert Doppelauslösung.
            var affected = await _db.Reminders
                .Where(r => r.Id == id && r.Status == ReminderStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, ReminderStatus.Triggered)
                    .SetProperty(r => r.TriggeredAt, now), ct)
                .ConfigureAwait(false);

            if (affected == 1)
            {
                claimedIds.Add(id);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }

        if (claimedIds.Count == 0)
        {
            return 0;
        }

        var claimed = await _db.Reminders
            .Where(r => claimedIds.Contains(r.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var userIds = claimed.Select(r => r.UserId).Distinct().ToList();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.InAppNotificationsEnabled, u.EmailNotificationsEnabled, u.IsActive })
            .ToDictionaryAsync(u => u.Id, ct)
            .ConfigureAwait(false);

        foreach (var reminder in claimed)
        {
            users.TryGetValue(reminder.UserId, out var user);

            if (user is not null && user.InAppNotificationsEnabled)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = reminder.UserId,
                    Title = reminder.Title,
                    Body = reminder.Message,
                    Url = reminder.TaskItemId.HasValue
                        ? $"/Tasks/Details?id={reminder.TaskItemId.Value}"
                        : reminder.CalendarEventId.HasValue
                            ? $"/Calendar/Details?id={reminder.CalendarEventId.Value}"
                            : "/Reminders",
                    ReminderId = reminder.Id,
                    CreatedAt = now,
                });

                reminder.Delivered = true;
            }
            else
            {
                reminder.Delivered = false;
                reminder.FailureReason = "In-App-Benachrichtigungen sind für dieses Konto deaktiviert.";
            }

            if (_options.EmailChannelEnabled && user is not null && user.EmailNotificationsEnabled)
            {
                if (_email.IsConfigured)
                {
                    await _email.SendAsync(new EmailMessage(
                        user.Email ?? string.Empty,
                        reminder.Title,
                        reminder.Message ?? reminder.Title), ct).ConfigureAwait(false);
                }
                else
                {
                    // Ehrlich: es wird kein Versand vorgetäuscht.
                    reminder.FailureReason = "E-Mail-Versand ist nicht konfiguriert (kein SMTP-Provider hinterlegt).";
                    _logger.LogInformation(
                        "Erinnerung {ReminderId} wurde ausgelöst; E-Mail-Zustellung übersprungen, da kein Provider konfiguriert ist.",
                        reminder.Id);
                }
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("{Count} Erinnerung(en) ausgelöst.", claimed.Count);
        return claimed.Count;
    }
}
