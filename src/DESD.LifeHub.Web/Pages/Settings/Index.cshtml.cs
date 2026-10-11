using System.ComponentModel.DataAnnotations;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Settings;

public class IndexModel : LifeHubPageModel
{
    private readonly IUserAccountService _accounts;

    public IndexModel(AppShell shell, IUserAccountService accounts) : base(shell)
    {
        _accounts = accounts;
        PageTitle = "Einstellungen";
        NavSection = "";
    }

    [BindProperty]
    public ProfileInputModel Input { get; set; } = new();

    public string? CurrentEmail { get; private set; }

    public IReadOnlyList<string> TimeZones => TimeZoneCatalog.Suggested;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var profile = await _accounts.GetProfileAsync(UserId(), ct).ConfigureAwait(false);
        if (profile is null)
        {
            SetError("Das Benutzerkonto wurde nicht gefunden.");
            return RedirectToPage("/Dashboard");
        }

        CurrentEmail = profile.Email;
        Input = new ProfileInputModel
        {
            DisplayName = profile.DisplayName,
            TimeZoneId = profile.TimeZoneId,
            PreferredLanguage = profile.PreferredLanguage,
            Theme = profile.ThemePreference,
            InAppNotifications = profile.InAppNotificationsEnabled,
            EmailNotifications = profile.EmailNotificationsEnabled,
        };

        return Page();
    }

    public async Task<IActionResult> OnPostProfileAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadProfileMetaAsync(ct).ConfigureAwait(false);
            return Page();
        }

        var result = await _accounts.UpdateProfileAsync(
            UserId(),
            Input.DisplayName,
            Input.TimeZoneId,
            Input.PreferredLanguage,
            Input.Theme,
            Input.InAppNotifications,
            Input.EmailNotifications,
            ct).ConfigureAwait(false);

        if (result.Succeeded)
        {
            SetSuccess("Ihre Einstellungen wurden gespeichert.");
            return RedirectToPage();
        }

        ApplyServiceResult(result);
        await LoadProfileMetaAsync(ct).ConfigureAwait(false);
        return Page();
    }

    public string ThemeText(ThemePreference theme) => Ui.ThemeText(theme);

    private async Task LoadProfileMetaAsync(CancellationToken ct)
    {
        var profile = await _accounts.GetProfileAsync(UserId(), ct).ConfigureAwait(false);
        CurrentEmail = profile?.Email;
    }

    private Guid UserId()
        => Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    public sealed class ProfileInputModel
    {
        [Required(ErrorMessage = "Bitte geben Sie einen Anzeigenamen an.")]
        [StringLength(120, ErrorMessage = "Der Name darf höchstens 120 Zeichen enthalten.")]
        [Display(Name = "Anzeigename")]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bitte wählen Sie eine Zeitzone.")]
        [Display(Name = "Zeitzone")]
        public string TimeZoneId { get; set; } = "Europe/Berlin";

        [Display(Name = "Sprache")]
        public string PreferredLanguage { get; set; } = "de";

        [Display(Name = "Darstellung")]
        public ThemePreference Theme { get; set; } = ThemePreference.System;

        [Display(Name = "Benachrichtigungen in der App")]
        public bool InAppNotifications { get; set; } = true;

        [Display(Name = "E-Mail-Benachrichtigungen")]
        public bool EmailNotifications { get; set; }
    }
}