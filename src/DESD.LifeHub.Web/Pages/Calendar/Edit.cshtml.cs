using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Calendar;

public class EditModel : LifeHubPageModel
{
    private readonly ICalendarService _calendar;
    private readonly IOrganizationAccessService _access;
    private readonly IOrganizationService _organizations;
    private readonly IRoomService _rooms;

    public EditModel(
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
        PageTitle = "Termin bearbeiten";
        NavSection = "calendar";
    }

    [BindProperty]
    public Guid Id { get; set; }

    [BindProperty]
    public EventFormModel Input { get; set; } = new();

    public IReadOnlyList<Organization> Organizations { get; private set; } = [];

    public IReadOnlyList<Room> Rooms { get; private set; } = [];

    public IReadOnlyList<MemberOption> Members { get; private set; } = [];

    public Guid UserIdValue { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Id = id;
        UserIdValue = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        var item = await _calendar.GetAsync(UserIdValue, id, ct).ConfigureAwait(false);
        if (item is null)
        {
            SetError("Der Termin wurde nicht gefunden.");
            return RedirectToPage("/Calendar/Index");
        }

        var canEdit = item.OwnerUserId == UserIdValue
                      || (item.OrganizationId.HasValue &&
                          await _access.CanManageOrganizationAsync(UserIdValue, item.OrganizationId.Value, ct).ConfigureAwait(false));
        if (!canEdit)
        {
            SetError("Sie sind nicht berechtigt, diesen Termin zu bearbeiten.");
            return RedirectToPage("/Calendar/Details", new { id });
        }

        await LoadLookupsAsync(ct).ConfigureAwait(false);

        Input = new EventFormModel
        {
            Title = item.Title,
            Description = item.Description,
            Location = item.Location,
            StartsAt = LocalInputDate(item.StartsAt),
            EndsAt = LocalInputDate(item.EndsAt),
            IsAllDay = item.IsAllDay,
            ColorCategory = item.ColorCategory,
            RoomId = item.RoomId,
            RecurrenceFrequency = item.RecurrenceFrequency,
            RecurrenceInterval = item.RecurrenceInterval,
            RecurrenceUntilDate = item.RecurrenceUntil is { } until ? TimeZoneCatalog.ToLocal(until, ShellTimeZoneId).Date : null,
            OrganizationId = item.OrganizationId,
            CreateReminder = true,
            ParticipantUserIds = item.Participants.Select(p => p.UserId).ToList(),
        };

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

        var result = await _calendar.UpdateAsync(UserIdValue, Id, input, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Der Termin wurde gespeichert.");
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
