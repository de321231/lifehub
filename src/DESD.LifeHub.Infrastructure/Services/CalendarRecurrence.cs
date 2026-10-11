using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>
/// Entfaltet wiederkehrende Termine in einzelne Vorkommen innerhalb eines Zeitraums.
/// Die Berechnung ist deterministisch, begrenzt (Schutz vor Endlosschleifen) und
/// arbeitet ausschliesslich in UTC. Ein Vorkommen ist eine losgelöste Kopie des
/// Serientermins mit angepasstem Beginn/Ende; die <c>Id</c> bleibt die der Serie.
/// </summary>
public static class CalendarRecurrence
{
    private const int MaxOccurrences = 5000;

    public static IEnumerable<CalendarEvent> Expand(CalendarEvent series, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (series.RecurrenceFrequency == RecurrenceFrequency.None)
        {
            yield return series;
            yield break;
        }

        var from = TimeUtil.ToUtc(fromUtc);
        var to = TimeUtil.ToUtc(toUtc);
        var duration = series.EndsAt - series.StartsAt;
        if (duration <= TimeSpan.Zero)
        {
            duration = TimeSpan.FromMinutes(30);
        }

        var interval = Math.Max(1, series.RecurrenceInterval);
        var until = series.RecurrenceUntil.HasValue ? TimeUtil.ToUtc(series.RecurrenceUntil.Value) : (DateTimeOffset?)null;

        var cursor = series.StartsAt;

        // Schnellvorlauf: bei täglicher/wöchentlicher Serie weit in der Vergangenheit
        // nicht jeden einzelnen Tag durchlaufen.
        if (cursor < from)
        {
            cursor = FastForward(cursor, series.RecurrenceFrequency, interval, from, duration);
        }

        var produced = 0;
        while (cursor < to && produced < MaxOccurrences)
        {
            if (until.HasValue && cursor > until.Value)
            {
                break;
            }

            var end = cursor + duration;
            if (end > from && cursor < to)
            {
                yield return Clone(series, cursor, end);
                produced++;
            }

            cursor = Advance(cursor, series.RecurrenceFrequency, interval);
        }
    }

    private static DateTimeOffset FastForward(
        DateTimeOffset cursor,
        RecurrenceFrequency frequency,
        int interval,
        DateTimeOffset from,
        TimeSpan duration)
    {
        // Schrittweise Annäherung; bei Monats-/Jahresrhythmus in Schleife, sonst rechnerisch.
        if (frequency is RecurrenceFrequency.Daily or RecurrenceFrequency.Weekly)
        {
            var step = frequency == RecurrenceFrequency.Daily
                ? TimeSpan.FromDays(interval)
                : TimeSpan.FromDays(7 * interval);

            var gap = from - cursor;
            var steps = (long)(gap.Ticks / step.Ticks);
            if (steps > 1)
            {
                cursor = cursor.AddTicks(steps * step.Ticks);
            }
        }

        var guard = 0;
        while (cursor + duration <= from && guard++ < MaxOccurrences)
        {
            cursor = Advance(cursor, frequency, interval);
        }

        return cursor;
    }

    private static DateTimeOffset Advance(DateTimeOffset cursor, RecurrenceFrequency frequency, int interval) => frequency switch
    {
        RecurrenceFrequency.Daily => cursor.AddDays(interval),
        RecurrenceFrequency.Weekly => cursor.AddDays(7 * interval),
        RecurrenceFrequency.Monthly => cursor.AddMonths(interval),
        RecurrenceFrequency.Yearly => cursor.AddYears(interval),
        _ => cursor.AddDays(interval),
    };

    private static CalendarEvent Clone(CalendarEvent source, DateTimeOffset start, DateTimeOffset end) => new()
    {
        Id = source.Id,
        OwnerUserId = source.OwnerUserId,
        Owner = source.Owner,
        OrganizationId = source.OrganizationId,
        Title = source.Title,
        Description = source.Description,
        Location = source.Location,
        StartsAt = start,
        EndsAt = end,
        IsAllDay = source.IsAllDay,
        ColorCategory = source.ColorCategory,
        RoomId = source.RoomId,
        Room = source.Room,
        RecurrenceFrequency = source.RecurrenceFrequency,
        RecurrenceInterval = source.RecurrenceInterval,
        RecurrenceUntil = source.RecurrenceUntil,
        DisplayTimeZoneId = source.DisplayTimeZoneId,
        TaskItemId = source.TaskItemId,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        Participants = source.Participants,
    };
}
