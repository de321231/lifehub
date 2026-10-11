using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DESD.LifeHub.Web.Pages.Dashboard;

public class DashboardModel : LifeHubPageModel
{
    private readonly ITaskService _tasks;
    private readonly INoteService _notes;
    private readonly ICalendarService _calendar;
    private readonly IReminderService _reminders;
    private readonly IAnnouncementService _announcements;

    public DashboardModel(
        AppShell shell,
        ITaskService tasks,
        INoteService notes,
        ICalendarService calendar,
        IReminderService reminders,
        IAnnouncementService announcements)
        : base(shell)
    {
        _tasks = tasks;
        _notes = notes;
        _calendar = calendar;
        _reminders = reminders;
        _announcements = announcements;
        PageTitle = "Übersicht";
        NavSection = "dashboard";
    }

    public DashboardData Tasks { get; private set; } = new();
    public IReadOnlyList<Note> RecentNotes { get; private set; } = [];
    public IReadOnlyList<CalendarEvent> UpcomingEvents { get; private set; } = [];
    public IReadOnlyList<Reminder> UpcomingReminders { get; private set; } = [];
    public IReadOnlyList<CompanyAnnouncement> Announcements { get; private set; } = [];

    public string Greeting { get; private set; } = string.Empty;

    public DateTimeOffset NowUtc { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        NowUtc = DateTimeOffset.UtcNow;

        var start = TimeZoneCatalog.LocalDayStartUtc(NowUtc, ShellTimeZoneId);
        var end = TimeZoneCatalog.LocalDayStartUtc(NowUtc.AddDays(1), ShellTimeZoneId);

        var hour = TimeZoneCatalog.ToLocal(NowUtc, ShellTimeZoneId).Hour;
        Greeting = hour switch
        {
            < 5 => "Gute Nacht",
            < 11 => "Guten Morgen",
            < 18 => "Guten Tag",
            _ => "Guten Abend",
        };

        Tasks = await _tasks.GetDashboardAsync(userId, start, end, ct).ConfigureAwait(false);
        RecentNotes = await _notes.GetRecentAsync(userId, 5, ct).ConfigureAwait(false);
        UpcomingEvents = await _calendar.GetUpcomingAsync(userId, NowUtc, 6, ct).ConfigureAwait(false);
        UpcomingReminders = await _reminders.ListUpcomingAsync(userId, 5, ct).ConfigureAwait(false);
        Announcements = await _announcements.GetActiveForUserAsync(userId, 5, ct).ConfigureAwait(false);

        return Page();
    }
}