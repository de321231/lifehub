using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Calendar;

public class DetailsModel : LifeHubPageModel
{
    private readonly ICalendarService _calendar;
    private readonly IOrganizationAccessService _access;
    private readonly IOrganizationService _organizations;

    public DetailsModel(
        AppShell shell,
        ICalendarService calendar,
        IOrganizationAccessService access,
        IOrganizationService organizations) : base(shell)
    {
        _calendar = calendar;
        _access = access;
        _organizations = organizations;
        PageTitle = "Termin";
        NavSection = "calendar";
    }

    public CalendarEvent? Event { get; private set; }

    /// <summary>Organisationen des Benutzers für die Bereichsanzeige.</summary>
    public IReadOnlyList<Organization> Organizations { get; private set; } = [];

    /// <summary>Einladbare Mitglieder (nur Firmentermine, ohne bereits Eingeladene).</summary>
    public IReadOnlyList<OrganizationMemberView> InvitableMembers { get; private set; } = [];

    public bool CanEdit { get; private set; }

    /// <summary>Eigene Teilnehmerzeile (falls eingeladen).</summary>
    public CalendarEventParticipant? MyParticipation { get; private set; }

    public Guid UserIdValue { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        UserIdValue = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        Event = await _calendar.GetAsync(UserIdValue, id, ct).ConfigureAwait(false);
        if (Event is null)
        {
            SetError("Der Termin wurde nicht gefunden.");
            return RedirectToPage("/Calendar/Index");
        }

        Organizations = await _access.GetSelectableOrganizationsAsync(UserIdValue, ct).ConfigureAwait(false);
        CanEdit = await CanEditAsync(Event, ct).ConfigureAwait(false);
        MyParticipation = Event.Participants.FirstOrDefault(p => p.UserId == UserIdValue);

        if (Event.OrganizationId.HasValue && CanEdit)
        {
            var members = await _organizations.GetMembersAsync(UserIdValue, Event.OrganizationId.Value, ct).ConfigureAwait(false);
            var invited = Event.Participants.Select(p => p.UserId).ToHashSet();
            InvitableMembers = members.Where(m => m.IsActive && !invited.Contains(m.UserId) && m.UserId != UserIdValue).ToList();
        }

        PageTitle = Event.Title;
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        var result = await _calendar.DeleteAsync(UserIdValue, id, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Der Termin wurde gelöscht.");
            return RedirectToPage("/Calendar/Index");
        }

        SetError(result.Error ?? "Der Termin konnte nicht gelöscht werden.");
        return RedirectToPage("/Calendar/Details", new { id });
    }

    public async Task<IActionResult> OnPostRespondAsync(Guid id, ParticipantResponse response, CancellationToken ct)
    {
        UserIdValue = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _calendar.RespondAsync(UserIdValue, id, response, ct).ConfigureAwait(false);

        if (result.Succeeded)
        {
            SetSuccess($"Ihre Antwort wurde gespeichert: {Ui.ParticipantResponseText(response)}.");
        }
        else
        {
            SetError(result.Error ?? "Die Antwort konnte nicht gespeichert werden.");
        }

        return RedirectToPage("/Calendar/Details", new { id });
    }

    public async Task<IActionResult> OnPostInviteAsync(Guid id, Guid memberUserId, CancellationToken ct)
    {
        UserIdValue = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _calendar.InviteParticipantsAsync(UserIdValue, id, [memberUserId], ct).ConfigureAwait(false);

        if (result.Succeeded)
        {
            SetSuccess("Der Teilnehmer wurde eingeladen.");
        }
        else
        {
            SetError(result.Error ?? "Der Teilnehmer konnte nicht eingeladen werden.");
        }

        return RedirectToPage("/Calendar/Details", new { id });
    }

    public string OrgName(CalendarEvent item)
        => item.OrganizationId is { } organizationId
            ? Organizations.FirstOrDefault(o => o.Id == organizationId)?.Name ?? "Organisation"
            : "Privat";

    private async Task<bool> CanEditAsync(CalendarEvent item, CancellationToken ct)
    {
        if (item.OwnerUserId == UserIdValue)
        {
            return true;
        }

        if (item.OrganizationId.HasValue)
        {
            return await _access.CanManageOrganizationAsync(UserIdValue, item.OrganizationId.Value, ct).ConfigureAwait(false);
        }

        return false;
    }
}
