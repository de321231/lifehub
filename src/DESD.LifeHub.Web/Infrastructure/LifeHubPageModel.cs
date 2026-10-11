using DESD.LifeHub.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DESD.LifeHub.Web.Infrastructure;

/// <summary>
/// Basisklasse aller Seiten. Bündelt Shell-Daten, deutsche Formatierung und die
/// einheitliche Erfolgs-/Fehlermeldung über TempData.
/// </summary>
public abstract class LifeHubPageModel : PageModel
{
    public static readonly System.Globalization.CultureInfo German = System.Globalization.CultureInfo.GetCultureInfo("de-DE");

    private readonly AppShell _shell;

    protected LifeHubPageModel(AppShell shell) => _shell = shell;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    /// <summary>Seitentitel (ohne Produktnamen).</summary>
    public string PageTitle { get; protected set; } = "DESD LifeHub";

    /// <summary>Aktiver Navigationsbereich für die Sidebar-Hervorhebung.</summary>
    public string NavSection { get; protected set; } = "";

    /// <summary>Daten der Seitenvorlage (Navigation, Benachrichtigungen, Lizenzzustand).</summary>
    public AppShell Shell => _shell;

    public string ShellDisplayName => _shell.DisplayName;

    public string ShellTimeZoneId => _shell.TimeZoneId;

    public int ShellUnreadNotifications => _shell.UnreadNotificationCount;

    public int ShellDueReminders => _shell.DueReminderCount;

    public bool ShellHasValidLicense => _shell.HasValidLicense;

    public bool ShellServingFromGrace => _shell.ServingFromGrace;

    public string? ShellLicenseMessage => _shell.LicenseMessage;

    public string ShellThemeAttribute => _shell.Theme switch
    {
        DESD.LifeHub.Core.Enums.ThemePreference.Light => "light",
        DESD.LifeHub.Core.Enums.ThemePreference.Dark => "dark",
        _ => "",
    };

    public override async Task OnPageHandlerExecutionAsync(
        PageHandlerExecutingContext context,
        PageHandlerExecutionDelegate next)
    {
        await _shell.LoadAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
        TimeZoneId = _shell.TimeZoneId;
        await base.OnPageHandlerExecutionAsync(context, next).ConfigureAwait(false);
    }

    public void SetSuccess(string message) => StatusMessage = message;

    public void SetError(string message) => ErrorMessage = message;

    public string Local(DateTimeOffset utc, string format = "dd.MM.yyyy HH:mm")
        => TimeZoneCatalog.ToLocal(utc, TimeZoneId).ToString(format, German);

    /// <summary>
    /// Konvertiert eine lokale (eingabeformular-naive) Uhrzeit in UTC. Fehlende
    /// Werte ergeben null.
    /// </summary>
    public DateTimeOffset? LocalToUtc(DateTime? local)
        => local.HasValue ? TimeZoneCatalog.ToUtc(local.Value, TimeZoneId) : null;

    /// <summary>UTX-Wert als lokale Eingabezeit für datetime-local-Felder.</summary>
    public string? LocalInput(DateTimeOffset? utc, string format = "yyyy-MM-ddTHH:mm")
        => utc.HasValue ? TimeZoneCatalog.ToLocal(utc.Value, TimeZoneId).DateTime.ToString(format, German) : null;

    /// <summary>UTX-Wert als lokale DateTime für Formulareingaben.</summary>
    public DateTime? LocalInputDate(DateTimeOffset? utc)
        => utc.HasValue ? TimeZoneCatalog.ToLocal(utc.Value, TimeZoneId).DateTime : null;

    /// <summary>Zeitzone des angemeldeten Benutzers (wird von <see cref="AppShell"/> geliefert).</summary>
    public string TimeZoneId { get; set; } = "Europe/Berlin";

    /// <summary>Übernimmt Feldprüfungen aus einem <see cref="ServiceResult"/> in den ModelState.</summary>
    public void ApplyServiceResult(ServiceResult result, string? successMessage = null)
    {
        if (result.Succeeded)
        {
            if (successMessage is not null)
            {
                SetSuccess(successMessage);
            }

            return;
        }

        if (result.FieldErrors.Count > 0)
        {
            foreach (var (field, message) in result.FieldErrors)
            {
                ModelState.AddModelError(field, message);
            }
        }
        else if (!string.IsNullOrWhiteSpace(result.Error))
        {
            ModelState.AddModelError(string.Empty, result.Error);
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Die Aktion konnte nicht abgeschlossen werden.");
        }
    }
}