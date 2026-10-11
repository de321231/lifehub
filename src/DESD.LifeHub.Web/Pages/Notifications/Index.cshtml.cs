using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Notifications;

public class IndexModel : LifeHubPageModel
{
    private readonly INotificationService _notifications;

    public IndexModel(AppShell shell, INotificationService notifications) : base(shell)
    {
        _notifications = notifications;
        PageTitle = "Benachrichtigungen";
        NavSection = "notifications";
    }

    public IReadOnlyList<Notification> Notifications { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Notifications = await _notifications.ListAsync(UserId(), 100, ct).ConfigureAwait(false);
        return Page();
    }

    public async Task<IActionResult> OnPostMarkReadAsync(Guid id, CancellationToken ct)
    {
        await _notifications.MarkReadAsync(UserId(), id, ct).ConfigureAwait(false);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostMarkAllReadAsync(CancellationToken ct)
    {
        await _notifications.MarkAllReadAsync(UserId(), ct).ConfigureAwait(false);
        SetSuccess("Alle Benachrichtigungen wurden als gelesen markiert.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        var result = await _notifications.DeleteAsync(UserId(), id, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Die Benachrichtigung wurde gelöscht.");
        }
        else
        {
            SetError(result.Error ?? "Die Benachrichtigung konnte nicht gelöscht werden.");
        }

        return RedirectToPage();
    }

    private Guid UserId()
        => Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
}