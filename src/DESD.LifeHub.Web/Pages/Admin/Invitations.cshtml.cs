using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Licensing;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Admin;

/// <summary>
/// Systemverwaltung der Einladungen. Der Einladungslink wird nur einmal angezeigt
/// und aus dem Rohschlüssel serverseitig erzeugt.
/// </summary>
public class InvitationsModel : LifeHubPageModel
{
    private readonly IUserAccountService _accounts;
    private readonly ICompanyLicenseService _companyLicenses;

    public InvitationsModel(
        AppShell shell,
        IUserAccountService accounts,
        ICompanyLicenseService companyLicenses) : base(shell)
    {
        _accounts = accounts;
        _companyLicenses = companyLicenses;
        PageTitle = "Einladungen";
        NavSection = "admin-invitations";
    }

    [BindProperty]
    public InviteInputModel Invite { get; set; } = new();

    public IReadOnlyList<Invitation> Open { get; private set; } = [];

    public IReadOnlyList<CompanyOverviewRow> Companies { get; private set; } = [];

    /// <summary>Einmalig erzeugter Einladungslink (nur für die aktuelle Antwort).</summary>
    public string? InviteLink { get; private set; }

    public string? InviteLinkForEmail { get; private set; }

    public DateTimeOffset? InviteExpiresAt { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct).ConfigureAwait(false);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Open = await _accounts.ListOpenInvitationsAsync(ct).ConfigureAwait(false);
        Companies = await _companyLicenses.GetOperatorOverviewAsync(ct).ConfigureAwait(false);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct).ConfigureAwait(false);
            return Page();
        }

        var organizationId = Invite.OrganizationId == Guid.Empty ? (Guid?)null : Invite.OrganizationId;

        var result = await _accounts
            .CreateInvitationAsync(UserId(), Invite.Email, Invite.DisplayName, organizationId, Invite.ValidDays, ct)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Die Einladung konnte nicht erstellt werden.");
            await LoadAsync(ct).ConfigureAwait(false);
            return Page();
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        InviteLink = $"{baseUrl}/Account/Invitation?token={Uri.EscapeDataString(result.Token ?? string.Empty)}";
        InviteLinkForEmail = Invite.Email;
        InviteExpiresAt = result.ExpiresAt;

        SetSuccess("Die Einladung wurde erstellt. Der Link wird nur einmal angezeigt.");
        await LoadAsync(ct).ConfigureAwait(false);
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid id, CancellationToken ct)
    {
        var result = await _accounts.RevokeInvitationAsync(id, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Die Einladung wurde zurückgezogen.");
        }
        else
        {
            SetError(result.Error ?? "Die Einladung konnte nicht zurückgezogen werden.");
        }

        return RedirectToPage();
    }

    private Guid UserId()
        => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    public sealed class InviteInputModel
    {
        [Required(ErrorMessage = "Bitte geben Sie eine E-Mail-Adresse an.")]
        [EmailAddress(ErrorMessage = "Bitte geben Sie eine gültige E-Mail-Adresse an.")]
        [StringLength(256)]
        [Display(Name = "E-Mail")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bitte geben Sie einen Namen an.")]
        [StringLength(120)]
        [Display(Name = "Name")]
        public string DisplayName { get; set; } = string.Empty;

        [Display(Name = "Firma (optional)")]
        public Guid? OrganizationId { get; set; }

        [Range(1, 90, ErrorMessage = "Die Gültigkeit muss zwischen 1 und 90 Tagen liegen.")]
        [Display(Name = "Gültig (Tage)")]
        public int ValidDays { get; set; } = 7;
    }
}
