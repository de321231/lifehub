using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Calendar;

public class CreateModel : LifeHubPageModel
{
    private readonly ICalendarService _calendar;
    private readonly IOrganizationAccessService _access;
    private readonly IOrganizationService _organizations;
    private readonly IRoomService _rooms;

    public CreateModel(
        AppShell shell,
        ICalendarService calendar,
        IOrganizationAccessService access,
        IOrganizationService organizations,
        IRoomService rooms) : base(shell)
    {
        _calendar = calendar;
        _access = access;
        _organizations = organizations;
        _rooms = rooms;
        PageTitle = "Neuer Termin";
        NavSection = "calendar";
    }

    [BindProperty]
    public EventFormModel Input { get; set; } = new();

    public IReadOnlyList<Organization> Organizations { get; private set; } = [];

    public IReadOnlyList<Room> Rooms { get; private set; } = [];

    public IReadOnlyList<MemberOption> Members { get; private set; } = [];

    public Guid UserIdValue { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? year, int? month, int? day, CancellationToken ct)
    {
        UserIdValue = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        await LoadLookupsAsync(ct).ConfigureAwait(false);

        Input = new EventFormModel();
        var nowLocal = TimeZoneCatalog.ToLocal(DateTimeOffset.UtcNow, ShellTimeZoneId).DateTime;

        if (year.HasValue && month.HasValue && day.HasValue)
        {
            var date = new DateTime(Math.Clamp(year.Value, 2000, 2100), Math.Clamp(month.Value, 1, 12), Math.Clamp(day.Value, 1, 28));
            Input.StartsAt = new DateTime(date.Year, date.Month, date.Day, 9, 0, 0);
            Input.EndsAt = Input.StartsAt.Value.AddHours(1);
        }
        else
        {
            var nextFullHour = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, nowLocal.Hour, 0, 0).AddHours(1);
            Input.StartsAt = nextFullHour;
            Input.EndsAt = nextFullHour.AddHours(1);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        UserIdValue = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        await LoadLookupsAsync(ct).ConfigureAwait(false);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!TryBuildInput(out var input))
        {
            return Page();
        }

        var result = await _calendar.CreateAsync(UserIdValue, input, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Der Termin wurde angelegt.");
            return RedirectToPage("/Calendar/Details", new { id = result.Id });
        }

        ApplyServiceResult(result);
        return Page();
    }

    private bool TryBuildInput(out EventInput input)
    {
        input = null!;
        var start = LocalToUtc(Input.StartsAt);
        var end = LocalToUtc(Input.EndsAt);
        if (!start.HasValue || !end.HasValue || end.Value <= start.Value)
        {
            ModelState.AddModelError("Input.EndsAt", "Das Ende muss nach dem Beginn liegen.");
            return false;
        }

        input = new EventInput
        {
            Title = Input.Title,
            Description = Input.Description,
            Location = Input.Location,
            StartsAt = start.Value,
            EndsAt = end.Value,
            IsAllDay = Input.IsAllDay,
            ColorCategory = Input.ColorCategory,
            RoomId = Input.RoomId,
            RecurrenceFrequency = Input.RecurrenceFrequency,
            RecurrenceInterval = Input.RecurrenceInterval,
            RecurrenceUntil = RecurrenceUntilUtc(),
            DisplayTimeZoneId = ShellTimeZoneId,
            OrganizationId = Input.OrganizationId,
            CreateReminder = Input.CreateReminder,
            ReminderLeadMinutes = Input.ReminderLeadMinutes,
            ParticipantUserIds = Input.ParticipantUserIds,
        };

        return true;
    }

    private DateTimeOffset? RecurrenceUntilUtc()
    {
        if (Input.RecurrenceFrequency == RecurrenceFrequency.None || !Input.RecurrenceUntilDate.HasValue)
        {
            return null;
        }

        // Serienende inklusive: Tagesende in der Benutzerzeitzone.
        var date = Input.RecurrenceUntilDate.Value.Date.AddDays(1).AddSeconds(-1);
        return TimeZoneCatalog.ToUtc(date, ShellTimeZoneId);
    }

    private async Task LoadLookupsAsync(CancellationToken ct)
    {
        Organizations = await _access.GetSelectableOrganizationsAsync(UserIdValue, ct).ConfigureAwait(false);

        var allRooms = new List<Room>();
        var allMembers = new List<MemberOption>();
        foreach (var org in Organizations)
        {
            allRooms.AddRange(await _rooms.GetRoomsAsync(UserIdValue, org.Id, ct).ConfigureAwait(false));
            var members = await _organizations.GetMembersAsync(UserIdValue, org.Id, ct).ConfigureAwait(false);
            allMembers.AddRange(members
                .Where(m => m.UserId != UserIdValue && m.IsActive)
                .Select(m => new MemberOption(m.UserId, m.DisplayName, org.Id)));
        }

        Rooms = allRooms;
        Members = allMembers;
    }
}
