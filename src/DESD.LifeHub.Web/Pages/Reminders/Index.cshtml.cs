using System.ComponentModel.DataAnnotations;
using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Reminders;

public class IndexModel : LifeHubPageModel
{
    private readonly IReminderService _reminders;

    public IndexModel(AppShell shell, IReminderService reminders) : base(shell)
    {
        _reminders = reminders;
        PageTitle = "Erinnerungen";
        NavSection = "reminders";
    }

    [BindProperty]
    public ReminderFormModel Input { get; set; } = new();

    public IReadOnlyList<Reminder> Upcoming { get; private set; } = [];
    public IReadOnlyList<Reminder> History { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct).ConfigureAwait(false);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct).ConfigureAwait(false);
            return Page();
        }

        var remindAt = LocalToUtc(Input.RemindAt);
        if (!remindAt.HasValue)
        {
            ModelState.AddModelError("Input.RemindAt", "Bitte geben Sie einen Zeitpunkt an.");
            await LoadAsync(ct).ConfigureAwait(false);
            return Page();
        }

        var result = await _reminders.CreateManualAsync(
            UserId(),
            Input.Title,
            Input.Message,
            remindAt.Value,
            timeZoneId: ShellTimeZoneId).ConfigureAwait(false);

        if (result.Succeeded)
        {
            SetSuccess("Die Erinnerung wurde angelegt.");
            Input = new ReminderFormModel();
            return RedirectToPage();
        }

        ApplyServiceResult(result);
        await LoadAsync(ct).ConfigureAwait(false);
        return Page();
    }

    public async Task<IActionResult> OnPostDismissAsync(Guid id, CancellationToken ct)
    {
        await _reminders.DismissAsync(UserId(), id, ct).ConfigureAwait(false);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSnoozeAsync(Guid id, int minutes, CancellationToken ct)
    {
        await _reminders.SnoozeAsync(UserId(), id, Math.Clamp(minutes, 1, 60 * 24 * 30), ct).ConfigureAwait(false);
        SetSuccess($"Die Erinnerung wurde um {Math.Clamp(minutes, 1, 60 * 24 * 30)} Minuten verschoben.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        await _reminders.DeleteAsync(UserId(), id, ct).ConfigureAwait(false);
        SetSuccess("Die Erinnerung wurde gelöscht.");
        return RedirectToPage();
    }

    /// <summary>Soll eine ausgelöste Erinnerung als „jetzt fällig“ markiert werden?</summary>
    public string StatusText(ReminderStatus status) => Ui.ReminderStatusText(status);

    public string StatusBadge(ReminderStatus status) => Ui.ReminderStatusBadge(status);

    public bool IsPastReminder(Reminder r) => r.RemindAt < DateTimeOffset.UtcNow;

    private async Task LoadAsync(CancellationToken ct)
    {
        var userId = UserId();
        Upcoming = await _reminders.ListUpcomingAsync(userId, 40, ct).ConfigureAwait(false);
        History = await _reminders.ListTriggeredAsync(userId, 30, ct).ConfigureAwait(false);
    }

    private Guid UserId()
        => Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    public sealed class ReminderFormModel
    {
        [Required(ErrorMessage = "Bitte geben Sie einen Titel an.")]
        [StringLength(200, ErrorMessage = "Der Titel darf höchstens 200 Zeichen enthalten.")]
        public string Title { get; set; } = string.Empty;

        public string? Message { get; set; }

        /// <summary>Auslösezeit in lokaler Benutzerzeit.</summary>
        [Required(ErrorMessage = "Bitte geben Sie einen Zeitpunkt an.")]
        public DateTime? RemindAt { get; set; }
    }
}