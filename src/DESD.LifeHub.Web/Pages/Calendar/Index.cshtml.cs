using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Calendar;

public sealed record MonthDay(
    DateTime Date,
    int DayNumber,
    bool IsOtherMonth,
    bool IsToday,
    IReadOnlyList<CalendarEvent> Events,
    IReadOnlyList<TaskItem> DueTasks);

public class CalendarIndexModel : LifeHubPageModel
{
    private readonly ICalendarService _calendar;

    public CalendarIndexModel(AppShell shell, ICalendarService calendar) : base(shell)
    {
        _calendar = calendar;
        PageTitle = "Kalender";
        NavSection = "calendar";
    }

    public int Year { get; private set; }
    public int Month { get; private set; }
    public string MonthTitle { get; private set; } = string.Empty;
    public int PrevYear { get; private set; }
    public int PrevMonth { get; private set; }
    public int NextYear { get; private set; }
    public int NextMonth { get; private set; }
    public IReadOnlyList<MonthDay> Days { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? year, int? month, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var localNow = TimeZoneCatalog.ToLocal(DateTimeOffset.UtcNow, ShellTimeZoneId);

        Year = year ?? localNow.Year;
        Month = month ?? localNow.Month;

        Year = Math.Clamp(Year, 2000, 2100);
        if (Month < 1)
        {
            Month = 12;
            Year--;
        }
        else if (Month > 12)
        {
            Month = 1;
            Year++;
        }

        var firstOfMonth = new DateTime(Year, Month, 1);
        var offsetToMonday = ((int)firstOfMonth.DayOfWeek + 6) % 7; // Montag = 0
        var gridStart = firstOfMonth.AddDays(-offsetToMonday);
        var gridEnd = gridStart.AddDays(42);

        var startUtc = TimeZoneCatalog.ToUtc(gridStart, ShellTimeZoneId);
        var endUtc = TimeZoneCatalog.ToUtc(gridEnd, ShellTimeZoneId);

        var events = await _calendar.GetEventsAsync(userId, startUtc, endUtc, ct).ConfigureAwait(false);
        var dueTasks = await _calendar.GetDueTasksAsync(userId, startUtc, endUtc, ct).ConfigureAwait(false);

        var days = new List<MonthDay>(42);
        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
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

            days.Add(new MonthDay(
                date,
                date.Day,
                date.Month != Month,
                date == localNow.Date,
                dayEvents,
                dayTasks));
        }

        Days = days;
        MonthTitle = $"{German.DateTimeFormat.GetMonthName(Month)} {Year}";

        (PrevYear, PrevMonth) = Month == 1 ? (Year - 1, 12) : (Year, Month - 1);
        (NextYear, NextMonth) = Month == 12 ? (Year + 1, 1) : (Year, Month + 1);

        return Page();
    }

    /// <summary>Liegt ein ganztägiger Termin auf dem lokalen Kalendertag?</summary>
    private bool FallsOnDay(CalendarEvent e, DateTime localDate)
    {
        var startDay = TimeZoneCatalog.ToLocal(e.StartsAt, ShellTimeZoneId).Date;
        var endDay = TimeZoneCatalog.ToLocal(e.EndsAt, ShellTimeZoneId).Date;
        return localDate >= startDay && localDate < endDay;
    }
}