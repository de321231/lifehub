using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>
/// Kalenderverwaltung. Alle Zeitpunkte werden in UTC gespeichert und in der Zeitzone
/// des Benutzers angezeigt. Die Web-Schicht rechnet lokale Eingaben vor dem Aufruf
/// in UTC um; hier erfolgt die serverseitige Validierung der Zeiträume.
/// Wiederkehrende Termine werden als eine Serie gespeichert und beim Auflisten in
/// einzelne Vorkommen ("Occurrences") im angefragten Zeitraum entfaltet.
/// </summary>
public interface ICalendarService
{
    /// <summary>Termine, die den Zeitraum [fromUtc, toUtc) schneiden (Serien entfaltet).</summary>
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(Guid userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Aufgaben mit Fälligkeit im Zeitraum (Kalender-Verknüpfung).</summary>
    Task<IReadOnlyList<TaskItem>> GetDueTasksAsync(Guid userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    Task<CalendarEvent?> GetAsync(Guid userId, Guid eventId, CancellationToken ct = default);

    /// <summary>Die nächsten anstehenden Termine für das Dashboard (Serien entfaltet).</summary>
    Task<IReadOnlyList<CalendarEvent>> GetUpcomingAsync(Guid userId, DateTimeOffset fromUtc, int count, CancellationToken ct = default);

    Task<ServiceResult> CreateAsync(Guid userId, EventInput input, CancellationToken ct = default);

    Task<ServiceResult> UpdateAsync(Guid userId, Guid eventId, EventInput input, CancellationToken ct = default);

    Task<ServiceResult> DeleteAsync(Guid userId, Guid eventId, CancellationToken ct = default);

    /// <summary>Verschiebt einen Termin (Drag &amp; Drop) und behält die Dauer bei.</summary>
    Task<ServiceResult> MoveAsync(Guid userId, Guid eventId, DateTimeOffset newStartUtc, CancellationToken ct = default);

    /// <summary>Lädt weitere Organisationsmitglieder als Teilnehmer ein.</summary>
    Task<ServiceResult> InviteParticipantsAsync(Guid userId, Guid eventId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    /// <summary>Setzt die eigene Antwort (zu-/abgesagt) zu einem Termin.</summary>
    Task<ServiceResult> RespondAsync(Guid userId, Guid eventId, ParticipantResponse response, CancellationToken ct = default);
}

public sealed class CalendarService : ICalendarService
{
    private readonly AppDbContext _db;
    private readonly IOrganizationAccessService _access;
    private readonly IReminderService _reminders;

    public CalendarService(AppDbContext db, IOrganizationAccessService access, IReminderService reminders)
    {
        _db = db;
        _access = access;
        _reminders = reminders;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        Guid userId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default)
    {
        var candidates = await LoadCandidatesAsync(userId, fromUtc, toUtc, ct).ConfigureAwait(false);
        var from = TimeUtil.ToUtc(fromUtc);
        var to = TimeUtil.ToUtc(toUtc);

        var result = new List<CalendarEvent>();
        foreach (var e in candidates)
        {
            result.AddRange(CalendarRecurrence.Expand(e, from, to));
        }

        return result.OrderBy(e => e.StartsAt).ToList();
    }

    public async Task<IReadOnlyList<TaskItem>> GetDueTasksAsync(
        Guid userId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default)
    {
        var orgIds = await _access.GetOrganizationIdsAsync(userId, ct).ConfigureAwait(false);
        var ids = orgIds.ToList();
        var from = TimeUtil.ToUtc(fromUtc);
        var to = TimeUtil.ToUtc(toUtc);

        return await _db.Tasks
            .AsNoTracking()
            .Where(t =>
                ((t.OrganizationId == null && t.OwnerUserId == userId) ||
                 (t.OrganizationId != null && ids.Contains(t.OrganizationId.Value)))
                && t.DueAt != null
                && t.DueAt >= from
                && t.DueAt < to)
            .OrderBy(t => t.DueAt)
            .Take(1000)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<CalendarEvent?> GetAsync(Guid userId, Guid eventId, CancellationToken ct = default)
    {
        var item = await _db.CalendarEvents
            .AsNoTracking()
            .Include(e => e.Owner)
            .Include(e => e.Room)
            .Include(e => e.Participants).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(e => e.Id == eventId, ct)
            .ConfigureAwait(false);

        if (item is null)
        {
            return null;
        }

        return await CanViewAsync(userId, item, ct).ConfigureAwait(false) ? item : null;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetUpcomingAsync(
        Guid userId,
        DateTimeOffset fromUtc,
        int count,
        CancellationToken ct = default)
    {
        var from = TimeUtil.ToUtc(fromUtc);
        var windowEnd = from.AddDays(120);

        var candidates = await LoadCandidatesAsync(userId, from.AddDays(-31), windowEnd, ct).ConfigureAwait(false);

        return candidates
            .SelectMany(e => CalendarRecurrence.Expand(e, from, windowEnd))
            .Where(e => e.EndsAt >= from)
            .OrderBy(e => e.StartsAt)
            .Take(Math.Clamp(count, 1, 50))
            .ToList();
    }

    public async Task<ServiceResult> CreateAsync(Guid userId, EventInput input, CancellationToken ct = default)
    {
        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return ServiceResult.Fail("Bitte prüfen Sie die Eingaben.", errors);
        }

        if (input.OrganizationId.HasValue &&
            !await _access.IsMemberAsync(userId, input.OrganizationId.Value, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind kein Mitglied dieser Organisation.");
        }

        if (input.RoomId.HasValue)
        {
            var roomError = await ValidateRoomAsync(userId, input.RoomId.Value, input.OrganizationId, ct).ConfigureAwait(false);
            if (roomError is not null)
            {
                return ServiceResult.Fail(roomError);
            }
        }

        if (input.TaskItemId.HasValue)
        {
            var linked = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == input.TaskItemId.Value, ct).ConfigureAwait(false);
            var orgIds = await _access.GetVisibleOrganizationIdsAsync(userId, ct).ConfigureAwait(false);
            if (linked is null || !VisibilityRules.CanView(orgIds, linked.OrganizationId, linked.OwnerUserId, userId))
            {
                return ServiceResult.Fail("Die verknüpfte Aufgabe wurde nicht gefunden.");
            }
        }

        var item = new CalendarEvent
        {
            OwnerUserId = userId,
            OrganizationId = input.OrganizationId,
            Title = input.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim(),
            StartsAt = TimeUtil.ToUtc(input.StartsAt),
            EndsAt = TimeUtil.ToUtc(input.EndsAt),
            IsAllDay = input.IsAllDay,
            ColorCategory = string.IsNullOrWhiteSpace(input.ColorCategory) ? null : input.ColorCategory.Trim(),
            RoomId = input.RoomId,
            RecurrenceFrequency = input.RecurrenceFrequency,
            RecurrenceInterval = Math.Clamp(input.RecurrenceInterval, 1, 52),
            RecurrenceUntil = TimeUtil.ToUtc(input.RecurrenceUntil),
            DisplayTimeZoneId = input.DisplayTimeZoneId,
            TaskItemId = input.TaskItemId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.CalendarEvents.Add(item);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (input.OrganizationId.HasValue)
        {
            await SyncParticipantsAsync(userId, item, input.ParticipantUserIds, ct).ConfigureAwait(false);
        }

        if (input.CreateReminder)
        {
            await _reminders.CreateForEventAsync(userId, item, input.ReminderLeadMinutes, ct).ConfigureAwait(false);
        }

        return ServiceResult.Ok(item.Id);
    }

    public async Task<ServiceResult> UpdateAsync(Guid userId, Guid eventId, EventInput input, CancellationToken ct = default)
    {
        var item = await _db.CalendarEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return ServiceResult.Fail("Der Termin wurde nicht gefunden.");
        }

        if (!await CanEditAsync(userId, item, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diesen Termin zu bearbeiten.");
        }

        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return ServiceResult.Fail("Bitte prüfen Sie die Eingaben.", errors);
        }

        if (input.OrganizationId.HasValue &&
            !await _access.IsMemberAsync(userId, input.OrganizationId.Value, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind kein Mitglied dieser Organisation.");
        }

        if (input.RoomId.HasValue)
        {
            var roomError = await ValidateRoomAsync(userId, input.RoomId.Value, input.OrganizationId, ct).ConfigureAwait(false);
            if (roomError is not null)
            {
                return ServiceResult.Fail(roomError);
            }
        }

        item.Title = input.Title.Trim();
        item.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        item.Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim();
        item.StartsAt = TimeUtil.ToUtc(input.StartsAt);
        item.EndsAt = TimeUtil.ToUtc(input.EndsAt);
        item.IsAllDay = input.IsAllDay;
        item.ColorCategory = string.IsNullOrWhiteSpace(input.ColorCategory) ? null : input.ColorCategory.Trim();
        item.RoomId = input.RoomId;
        item.RecurrenceFrequency = input.RecurrenceFrequency;
        item.RecurrenceInterval = Math.Clamp(input.RecurrenceInterval, 1, 52);
        item.RecurrenceUntil = TimeUtil.ToUtc(input.RecurrenceUntil);
        item.DisplayTimeZoneId = input.DisplayTimeZoneId;
        item.OrganizationId = input.OrganizationId;
        item.TaskItemId = input.TaskItemId;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (input.OrganizationId.HasValue)
        {
            await SyncParticipantsAsync(userId, item, input.ParticipantUserIds, ct).ConfigureAwait(false);
        }
        else
        {
            await ClearParticipantsAsync(item.Id, ct).ConfigureAwait(false);
        }

        return ServiceResult.Ok(item.Id);
    }

    public async Task<ServiceResult> DeleteAsync(Guid userId, Guid eventId, CancellationToken ct = default)
    {
        var item = await _db.CalendarEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return ServiceResult.Fail("Der Termin wurde nicht gefunden.");
        }

        if (!await CanEditAsync(userId, item, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diesen Termin zu löschen.");
        }

        _db.CalendarEvents.Remove(item);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(eventId);
    }

    public async Task<ServiceResult> MoveAsync(Guid userId, Guid eventId, DateTimeOffset newStartUtc, CancellationToken ct = default)
    {
        var item = await _db.CalendarEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return ServiceResult.Fail("Der Termin wurde nicht gefunden.");
        }

        if (!await CanEditAsync(userId, item, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diesen Termin zu verschieben.");
        }

        var duration = item.EndsAt - item.StartsAt;
        var start = TimeUtil.ToUtc(newStartUtc);
        item.StartsAt = start;
        item.EndsAt = start + duration;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(item.Id);
    }

    public async Task<ServiceResult> InviteParticipantsAsync(Guid userId, Guid eventId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        var item = await _db.CalendarEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return ServiceResult.Fail("Der Termin wurde nicht gefunden.");
        }

        if (!await CanEditAsync(userId, item, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, Teilnehmer einzuladen.");
        }

        if (!item.OrganizationId.HasValue)
        {
            return ServiceResult.Fail("Teilnehmer können nur bei Firmenterminen eingeladen werden.");
        }

        await SyncParticipantsAsync(userId, item, userIds, ct).ConfigureAwait(false);
        return ServiceResult.Ok(item.Id);
    }

    public async Task<ServiceResult> RespondAsync(Guid userId, Guid eventId, ParticipantResponse response, CancellationToken ct = default)
    {
        var participant = await _db.CalendarEventParticipants
            .FirstOrDefaultAsync(p => p.CalendarEventId == eventId && p.UserId == userId, ct)
            .ConfigureAwait(false);

        if (participant is null)
        {
            return ServiceResult.Fail("Sie sind nicht als Teilnehmer eingetragen.");
        }

        participant.Response = response;
        participant.RespondedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(eventId);
    }

    // ------------------------------------------------------------------ intern

    private async Task<List<CalendarEvent>> LoadCandidatesAsync(Guid userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var orgIds = await _access.GetOrganizationIdsAsync(userId, ct).ConfigureAwait(false);
        var ids = orgIds.ToList();

        var sharedOwnerIds = await _db.CalendarShares
            .AsNoTracking()
            .Where(s => s.SharedWithUserId == userId)
            .Select(s => s.OwnerUserId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var participantEventIds = await _db.CalendarEventParticipants
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.CalendarEventId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var from = TimeUtil.ToUtc(fromUtc);
        var to = TimeUtil.ToUtc(toUtc);

        return await _db.CalendarEvents
            .AsNoTracking()
            .Include(e => e.Owner)
            .Include(e => e.Room)
            .Include(e => e.Participants).ThenInclude(p => p.User)
            .Where(e =>
                (((e.OrganizationId == null && (e.OwnerUserId == userId || sharedOwnerIds.Contains(e.OwnerUserId))) ||
                  (e.OrganizationId != null && ids.Contains(e.OrganizationId.Value)))
                 || participantEventIds.Contains(e.Id))
                && (
                    (e.RecurrenceFrequency == RecurrenceFrequency.None && e.StartsAt < to && e.EndsAt > from)
                    || (e.RecurrenceFrequency != RecurrenceFrequency.None
                        && e.StartsAt <= to
                        && (e.RecurrenceUntil == null || e.RecurrenceUntil >= from))))
            .OrderBy(e => e.StartsAt)
            .Take(2000)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    private async Task<bool> CanViewAsync(Guid userId, CalendarEvent item, CancellationToken ct)
    {
        var orgIds = await _access.GetVisibleOrganizationIdsAsync(userId, ct).ConfigureAwait(false);
        if (VisibilityRules.CanView(orgIds, item.OrganizationId, item.OwnerUserId, userId))
        {
            return true;
        }

        if (await _db.CalendarEventParticipants.AsNoTracking()
                .AnyAsync(p => p.CalendarEventId == item.Id && p.UserId == userId, ct).ConfigureAwait(false))
        {
            return true;
        }

        return await _db.CalendarShares.AsNoTracking()
            .AnyAsync(s => s.OwnerUserId == item.OwnerUserId && s.SharedWithUserId == userId, ct)
            .ConfigureAwait(false);
    }

    private async Task<bool> CanEditAsync(Guid userId, CalendarEvent item, CancellationToken ct)
    {
        if (item.OwnerUserId == userId)
        {
            return true;
        }

        if (item.OrganizationId.HasValue &&
            await _access.CanManageOrganizationAsync(userId, item.OrganizationId.Value, ct).ConfigureAwait(false))
        {
            return true;
        }

        return await _db.CalendarShares.AsNoTracking()
            .AnyAsync(s => s.OwnerUserId == item.OwnerUserId && s.SharedWithUserId == userId && s.CanEdit, ct)
            .ConfigureAwait(false);
    }

    private async Task<string?> ValidateRoomAsync(Guid userId, Guid roomId, Guid? organizationId, CancellationToken ct)
    {
        var room = await _db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct).ConfigureAwait(false);
        if (room is null || !room.IsActive)
        {
            return "Der gewählte Raum wurde nicht gefunden.";
        }

        if (!await _access.IsMemberAsync(userId, room.OrganizationId, ct).ConfigureAwait(false))
        {
            return "Sie sind kein Mitglied der Organisation dieses Raums.";
        }

        if (organizationId.HasValue && organizationId.Value != room.OrganizationId)
        {
            return "Der Raum gehört zu einer anderen Organisation.";
        }

        return null;
    }

    private async Task SyncParticipantsAsync(Guid ownerId, CalendarEvent item, IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        // Nur aktive Mitglieder der Terminorganisation dürfen Teilnehmer sein.
        var orgId = item.OrganizationId;
        if (orgId is null)
        {
            return;
        }

        var memberIds = await _db.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == orgId.Value && m.IsActive)
            .Select(m => m.UserId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var wanted = userIds
            .Where(id => memberIds.Contains(id))
            .ToHashSet();

        // Der Eigentümer ist immer Organisator.
        wanted.Add(ownerId);

        var existing = await _db.CalendarEventParticipants
            .Where(p => p.CalendarEventId == item.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var p in existing.Where(p => !wanted.Contains(p.UserId)))
        {
            _db.CalendarEventParticipants.Remove(p);
        }

        var existingIds = existing.Select(p => p.UserId).ToHashSet();
        foreach (var id in wanted.Where(id => !existingIds.Contains(id)))
        {
            _db.CalendarEventParticipants.Add(new CalendarEventParticipant
            {
                CalendarEventId = item.Id,
                UserId = id,
                IsOrganizer = id == ownerId,
                Response = id == ownerId ? ParticipantResponse.Accepted : ParticipantResponse.Invited,
            });
        }

        // Organisations-Flag des Eigentümers aktualisieren, falls bereits vorhanden.
        var organizerRow = existing.FirstOrDefault(p => p.UserId == ownerId);
        if (organizerRow is not null)
        {
            organizerRow.IsOrganizer = true;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task ClearParticipantsAsync(Guid eventId, CancellationToken ct)
    {
        var existing = await _db.CalendarEventParticipants
            .Where(p => p.CalendarEventId == eventId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (existing.Count > 0)
        {
            _db.CalendarEventParticipants.RemoveRange(existing);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string> Validate(EventInput input)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(input.Title))
        {
            errors["Title"] = "Bitte geben Sie einen Titel an.";
        }

        var start = TimeUtil.ToUtc(input.StartsAt);
        var end = TimeUtil.ToUtc(input.EndsAt);

        if (end <= start)
        {
            errors["EndsAt"] = "Das Ende muss nach dem Beginn liegen.";
        }

        if (end - start > TimeSpan.FromDays(366))
        {
            errors["EndsAt"] = "Ein Termin darf höchstens ein Jahr dauern.";
        }

        if (input.IsAllDay && (end - start) > TimeSpan.FromDays(31))
        {
            errors["EndsAt"] = "Ein ganztägiger Termin darf höchstens 31 Tage umfassen.";
        }

        if (input.RecurrenceFrequency != RecurrenceFrequency.None && input.RecurrenceUntil.HasValue)
        {
            if (TimeUtil.ToUtc(input.RecurrenceUntil.Value) < start)
            {
                errors["RecurrenceUntil"] = "Das Serienende darf nicht vor dem Beginn liegen.";
            }
        }

        if (!string.IsNullOrWhiteSpace(input.DisplayTimeZoneId))
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(input.DisplayTimeZoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                errors["DisplayTimeZoneId"] = "Die angegebene Zeitzone ist nicht bekannt.";
            }
        }

        return errors;
    }
}
