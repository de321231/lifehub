using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Calendar;

public sealed record WeekDay(
    DateTime Date,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsToday,
    IReadOnlyList<CalendarEvent> Events,
    IReadOnlyList<TaskItem> DueTasks);

public class WeekModel : LifeHubPageModel
{
    private readonly ICalendarService _calendar;

    public WeekModel(AppShell shell, ICalendarService calendar) : base(shell)
    {
        _calendar = calendar;
        PageTitle = "Kalender – Woche";
        NavSection = "calendar";
    }

    public IReadOnlyList<WeekDay> Days { get; private set; } = [];
    public DateTime WeekStart { get; private set; }
    public DateTime WeekEndExclusive { get; private set; }
    public string RangeTitle { get; private set; } = string.Empty;
    public DateTime PrevWeekStart { get; private set; }
    public DateTime NextWeekStart { get; private set; }
    public DateTime TodayLocal { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? year, int? month, int? day, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        TodayLocal = TimeZoneCatalog.ToLocal(DateTimeOffset.UtcNow, ShellTimeZoneId).Date;

        var today = TodayLocal;
        var reference = new DateTime(
            Math.Clamp(year ?? today.Year, 2000, 2100),
            Math.Clamp(month ?? today.Month, 1, 12),
            Math.Clamp(day ?? today.Day, 1, 28));

        var offsetToMonday = ((int)reference.DayOfWeek + 6) % 7;
        WeekStart = reference.AddDays(-offsetToMonday);
        WeekEndExclusive = WeekStart.AddDays(7);

        var startUtc = TimeZoneCatalog.ToUtc(WeekStart, ShellTimeZoneId);
        var endUtc = TimeZoneCatalog.ToUtc(WeekEndExclusive, ShellTimeZoneId);

        var events = await _calendar.GetEventsAsync(userId, startUtc, endUtc, ct).ConfigureAwait(false);
        var dueTasks = await _calendar.GetDueTasksAsync(userId, startUtc, endUtc, ct).ConfigureAwait(false);

        var days = new List<WeekDay>(7);
        for (var i = 0; i < 7; i++)
        {
            var date = WeekStart.AddDays(i);
            var dayEnd = date.AddDays(1);
            var dayStartUtc = TimeZoneCatalog.ToUtc(date, ShellTimeZoneId);
            var dayEndUtc = TimeZoneCatalog.ToUtc(dayEnd, ShellTimeZoneId);

            var dayEvents = events.Where(e =>
                    e.IsAllDay
                        ? FallsOnDay(e, date)
                        : e.StartsAt < dayEndUtc && e.EndsAt > dayStartUtc)
                .OrderBy(e => e.StartsAt)
                .ToList();

            var dayTasks = dueTasks
                .Where(t => t.DueAt.HasValue && t.DueAt.Value >= dayStartUtc && t.DueAt.Value < dayEndUtc)
                .OrderBy(t => t.DueAt)
                .ToList();

            days.Add(new WeekDay(date, dayStartUtc, dayEndUtc, date == TodayLocal, dayEvents, dayTasks));
        }

        Days = days;
        PrevWeekStart = WeekStart.AddDays(-7);
        NextWeekStart = WeekStart.AddDays(7);

        var sameYear = WeekStart.Year == WeekEndExclusive.AddDays(-1).Year;
        RangeTitle = sameYear
            ? $"{WeekStart:dd.MM.} – {WeekEndExclusive.AddDays(-1):dd.MM.yyyy}"
            : $"{WeekStart:dd.MM.yyyy} – {WeekEndExclusive.AddDays(-1):dd.MM.yyyy}";

        return Page();
    }

    private bool FallsOnDay(CalendarEvent e, DateTime localDate)
    {
        var startDay = TimeZoneCatalog.ToLocal(e.StartsAt, ShellTimeZoneId).Date;
        var endDay = TimeZoneCatalog.ToLocal(e.EndsAt, ShellTimeZoneId).Date;
        return localDate >= startDay && localDate < endDay;
    }
}