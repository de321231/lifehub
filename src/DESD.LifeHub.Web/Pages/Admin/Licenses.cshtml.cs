using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Licensing;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Admin;

/// <summary>
/// Systemverwaltung der Lizenzen: Benutzerstatus, Fehlerbilder und Erzeugung von
/// Firmen-Kontingenten über die OpenLicensd-Admin-API.
/// </summary>
public class LicensesModel : LifeHubPageModel
{
    private readonly LicenseService _licenses;
    private readonly ICompanyLicenseService _companyLicenses;
    private readonly Microsoft.Extensions.Options.IOptions<DESD.LifeHub.Infrastructure.Options.OpenLicensdOptions> _openLicensd;

    public LicensesModel(
        AppShell shell,
        LicenseService licenses,
        ICompanyLicenseService companyLicenses,
        Microsoft.Extensions.Options.IOptions<DESD.LifeHub.Infrastructure.Options.OpenLicensdOptions> openLicensd) : base(shell)
    {
        _licenses = licenses;
        _companyLicenses = companyLicenses;
        _openLicensd = openLicensd;
        PageTitle = "Lizenzen";
        NavSection = "admin-licenses";
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    /// <summary>Nur Konten anzeigen, die keine gültige Lizenz besitzen.</summary>
    [BindProperty(SupportsGet = true)]
    public bool OnlyMissing { get; set; }

    [BindProperty]
    public GenerateInputModel Generate { get; set; } = new();

    public IReadOnlyList<LicenseAdminRow> Rows { get; private set; } = [];

    public IReadOnlyList<CompanyOverviewRow> Companies { get; private set; } = [];

    public bool AdminApiConfigured { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct).ConfigureAwait(false);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var rows = await _licenses.GetAdminOverviewAsync(Search, ct).ConfigureAwait(false);
        if (OnlyMissing)
        {
            rows = rows
                .Where(r => !r.IsActive || !r.HasLicense || r.Status != LicenseStatus.Valid)
                .ToList();
        }

        Rows = rows;
        Companies = await _companyLicenses.GetOperatorOverviewAsync(ct).ConfigureAwait(false);
        AdminApiConfigured = _openLicensd.Value.AdminApiConfigured;
    }

    public async Task<IActionResult> OnPostGenerateAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct).ConfigureAwait(false);
            return Page();
        }

        var expiresAt = LocalToUtc(Generate.ExpiresAt);

        var result = await _companyLicenses
            .GenerateAsync(
                Generate.OrganizationId,
                Generate.Count,
                UserId(),
                Generate.LabelPrefix,
                expiresAt,
                Generate.MaxActivations,
                ct)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            var hint = result.Errors.Count > 0
                ? $" ({result.Errors.Count} Fehler: {string.Join("; ", result.Errors.Take(3))})"
                : string.Empty;
            SetSuccess($"{result.Created} von {result.Requested} Lizenzen erzeugt.{hint}");
        }
        else
        {
            SetError(result.FatalError ?? result.Errors.FirstOrDefault() ?? "Es wurden keine Lizenzen erzeugt.");
        }

        return RedirectToPage(new { Search, OnlyMissing });
    }

    public async Task<IActionResult> OnPostRevalidateAsync(Guid id, CancellationToken ct)
    {
        var result = await _licenses
            .RevalidateAsync(id, HttpContext.Connection.RemoteIpAddress?.ToString(), force: true, ct)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            SetSuccess("Die Lizenz wurde erneut geprüft und ist gültig.");
        }
        else
        {
            SetError(result.Message);
        }

        return RedirectToPage(new { Search, OnlyMissing });
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid id, CancellationToken ct)
    {
        await _licenses
            .RemoveAsync(id, UserId(), asAdmin: true, HttpContext.Connection.RemoteIpAddress?.ToString(), ct)
            .ConfigureAwait(false);

        SetSuccess("Die Lizenzzuordnung wurde entfernt.");
        return RedirectToPage(new { Search, OnlyMissing });
    }

    public async Task<IActionResult> OnPostRevokePoolAsync(Guid id, Guid orgId, CancellationToken ct)
    {
        var result = await _companyLicenses.RevokeAsync(orgId, id, UserId(), ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Die Firmenlizenz wurde gesperrt und aus dem Pool genommen.");
        }
        else
        {
            SetError(result.Error ?? "Die Lizenz konnte nicht gesperrt werden.");
        }

        return RedirectToPage(new { Search, OnlyMissing });
    }

    public string StatusBadge(LicenseStatus status) => Ui.LicenseBadge(status);

    public string StatusText(LicenseStatus status) => Ui.LicenseStatusText(status);

    private Guid UserId()
        => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    public sealed class GenerateInputModel
    {
        [Required(ErrorMessage = "Bitte wählen Sie eine Firma.")]
        [Display(Name = "Firma")]
        public Guid OrganizationId { get; set; }

        [Range(1, 500, ErrorMessage = "Bitte zwischen 1 und 500 Lizenzen erzeugen.")]
        [Display(Name = "Anzahl")]
        public int Count { get; set; } = 5;

        [Display(Name = "Ablaufdatum (optional)")]
        [DataType(DataType.Date)]
        public DateTime? ExpiresAt { get; set; }

        [Range(1, 100, ErrorMessage = "Zwischen 1 und 100 Aktivierungen.")]
        [Display(Name = "Aktivierungen pro Lizenz")]
        public int? MaxActivations { get; set; } = 1;

        [StringLength(120)]
        [Display(Name = "Bezeichnung (optional)")]
        public string? LabelPrefix { get; set; }
    }
}
