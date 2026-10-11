using System.ComponentModel.DataAnnotations;
using DESD.LifeHub.Infrastructure.Licensing;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DESD.LifeHub.Web.Pages.License;

/// <summary>
/// Lizenzaktivierung. Der Schlüssel wird ausschliesslich serverseitig verarbeitet
/// und gelangt weder in den Browser-Zustand noch in URLs oder Protokolle.
/// </summary>
public class LicenseIndexModel : LifeHubPageModel
{
    private readonly LicenseService _license;

    public LicenseIndexModel(AppShell shell, LicenseService license) : base(shell)
    {
        _license = license;
        PageTitle = "Lizenz";
        NavSection = "";
    }

    [BindProperty]
    public LicenseInputModel Input { get; set; } = new();

    /// <summary>Anzeigedaten des hinterlegten Lizenzschlüssels (nie der Schlüssel selbst).</summary>
    public LicenseStatusView? View { get; private set; }

    public string? Reason { get; private set; }

    public bool NewAccount { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? reason = null, bool newAccount = false)
    {
        Reason = reason;
        NewAccount = newAccount;
        await LoadStatusAsync().ConfigureAwait(false);
        return Page();
    }

    /// <summary>Schlüssel speichern und unmittelbar gegen den Lizenzserver prüfen.</summary>
    [EnableRateLimiting("license")]
    public async Task<IActionResult> OnPostActivateAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadStatusAsync().ConfigureAwait(false);
            return Page();
        }

        var userId = ShellUserId();
        var result = await _license.AssignAsync(
            userId,
            Input.LicenseKey,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()).ConfigureAwait(false);

        View = result.View;

        if (result.Succeeded)
        {
            SetSuccess(result.Message);
            Input = new LicenseInputModel();
            return RedirectToPage();
        }

        ModelState.AddModelError("Input.LicenseKey", result.Message);
        return Page();
    }

    /// <summary>Hinterlegte Lizenz erneut gegen den Server prüfen.</summary>
    [EnableRateLimiting("license")]
    public async Task<IActionResult> OnPostRevalidateAsync()
    {
        var result = await _license.RevalidateAsync(ShellUserId(), force: true,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()).ConfigureAwait(false);

        View = result.View;

        if (result.Succeeded)
        {
            SetSuccess("Die Lizenz wurde erneut bestätigt.");
        }
        else
        {
            SetError(result.Message);
        }

        return RedirectToPage();
    }

    /// <summary>Hinterlegte Lizenz entfernen (Schlüssel wird unwiderruflich gelöscht).</summary>
    public async Task<IActionResult> OnPostRemoveAsync()
    {
        var userId = ShellUserId();
        await _license.RemoveAsync(userId, userId, asAdmin: false,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()).ConfigureAwait(false);
        SetSuccess("Der Lizenzschlüssel wurde von Ihrem Konto entfernt.");
        return RedirectToPage();
    }

    private Guid ShellUserId()
        => Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    private async Task LoadStatusAsync(CancellationToken ct = default)
        => View = await _license.GetStatusAsync(ShellUserId(), ct).ConfigureAwait(false);

    public sealed class LicenseInputModel
    {
        [Required(ErrorMessage = "Bitte geben Sie Ihren Lizenzschlüssel ein.")]
        [StringLength(512, MinimumLength = 10, ErrorMessage = "Der Lizenzschlüssel ist unvollständig.")]
        public string LicenseKey { get; set; } = string.Empty;
    }
}