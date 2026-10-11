using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Calendar;

public sealed record DaySlot(
    DateTime Date,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsToday,
    IReadOnlyList<CalendarEvent> Events,
    IReadOnlyList<TaskItem> DueTasks);

public class DayModel : LifeHubPageModel
{
    private readonly ICalendarService _calendar;

    public DayModel(AppShell shell, ICalendarService calendar) : base(shell)
    {
        _calendar = calendar;
        PageTitle = "Kalender – Tag";
        NavSection = "calendar";
    }

    public DaySlot Day { get; private set; } = null!;
    public DateTime PrevDay { get; private set; }
    public DateTime NextDay { get; private set; }
    public string DayTitle { get; private set; } = string.Empty;
    public DateTime TodayLocal { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? year, int? month, int? day, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        TodayLocal = TimeZoneCatalog.ToLocal(DateTimeOffset.UtcNow, ShellTimeZoneId).Date;

        var today = TodayLocal;
        var date = new DateTime(
            Math.Clamp(year ?? today.Year, 2000, 2100),
            Math.Clamp(month ?? today.Month, 1, 12),
            Math.Clamp(day ?? today.Day, 1, 28));

        var dayEnd = date.AddDays(1);
        var startUtc = TimeZoneCatalog.ToUtc(date, ShellTimeZoneId);
        var endUtc = TimeZoneCatalog.ToUtc(dayEnd, ShellTimeZoneId);

        var events = await _calendar.GetEventsAsync(userId, startUtc, endUtc, ct).ConfigureAwait(false);
        var dueTasks = await _calendar.GetDueTasksAsync(userId, startUtc, endUtc, ct).ConfigureAwait(false);

        Day = new DaySlot(
            date,
            startUtc,
            endUtc,
            date == TodayLocal,
            events.Where(e => e.IsAllDay ? FallsOnDay(e, date) : true).OrderBy(e => e.StartsAt).ToList(),
            dueTasks.OrderBy(t => t.DueAt).ToList());

        PrevDay = date.AddDays(-1);
        NextDay = date.AddDays(1);
        DayTitle = date.ToString("dddd, dd. MMMM yyyy", German);
        PageTitle = $"Kalender – {date:dd.MM.yyyy}";
        return Page();
    }

    private bool FallsOnDay(CalendarEvent e, DateTime localDate)
    {
        var startDay = TimeZoneCatalog.ToLocal(e.StartsAt, ShellTimeZoneId).Date;
        var endDay = TimeZoneCatalog.ToLocal(e.EndsAt, ShellTimeZoneId).Date;
        return localDate >= startDay && localDate < endDay;
    }
}