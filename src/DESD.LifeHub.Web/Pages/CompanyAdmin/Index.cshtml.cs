using System.Security.Claims;
using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Data;
using DESD.LifeHub.Infrastructure.Licensing;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DESD.LifeHub.Web.Pages.CompanyAdmin;

/// <summary>
/// Firmenverwaltung unter <c>/{FIRMEN-ID}/admin</c>. Zeigt Mitarbeiter und deren
/// Lizenzstatus, den Firmenpool sowie Zuweisung/Freigabe. Zugriff nur für Manager
/// oder Eigentümer der jeweiligen Firma; Mitarbeiter sehen niemals Rohschlüssel.
/// </summary>
public class IndexModel : LifeHubPageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyLicenseService _companyLicenses;

    public IndexModel(AppShell shell, AppDbContext db, ICompanyLicenseService companyLicenses) : base(shell)
    {
        _db = db;
        _companyLicenses = companyLicenses;
        PageTitle = "Firmenverwaltung";
        NavSection = string.Empty;
    }

    [BindProperty(SupportsGet = true)]
    public string Slug { get; set; } = string.Empty;

    public Organization? CurrentOrganization { get; private set; }

    public CompanyPoolSummary Pool { get; private set; } = new()
    {
        OrganizationId = Guid.Empty,
        Total = 0,
        Assigned = 0,
        Free = 0,
        Revoked = 0,
    };

    public IReadOnlyList<CompanyEmployeeRow> Employees { get; private set; } = [];

    public IReadOnlyList<CompanyLicenseView> Licenses { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken ct)
    {
        var organization = await ResolveAuthorizedAsync(slug, ct).ConfigureAwait(false);
        if (organization is null)
        {
            return Forbid();
        }

        Slug = slug;
        CurrentOrganization = organization;
        PageTitle = organization.Name;
        await LoadAsync(organization.Id, ct).ConfigureAwait(false);
        return Page();
    }

    public async Task<IActionResult> OnPostAssignAsync(string slug, Guid licenseId, Guid userId, CancellationToken ct)
    {
        var organization = await ResolveAuthorizedAsync(slug, ct).ConfigureAwait(false);
        if (organization is null)
        {
            return Forbid();
        }

        var result = await _companyLicenses
            .AssignAsync(organization.Id, licenseId, userId, UserId(), ct)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            SetSuccess("Die Lizenz wurde dem Mitarbeiter zugewiesen.");
        }
        else
        {
            SetError(result.Error ?? "Die Lizenz konnte nicht zugewiesen werden.");
        }

        return RedirectToPage(new { slug });
    }

    public async Task<IActionResult> OnPostReleaseAsync(string slug, Guid licenseId, CancellationToken ct)
    {
        var organization = await ResolveAuthorizedAsync(slug, ct).ConfigureAwait(false);
        if (organization is null)
        {
            return Forbid();
        }

        var result = await _companyLicenses
            .ReleaseAsync(organization.Id, licenseId, UserId(), ct)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            SetSuccess("Die Lizenz wurde freigegeben und steht wieder im Pool zur Verfügung.");
        }
        else
        {
            SetError(result.Error ?? "Die Lizenz konnte nicht freigegeben werden.");
        }

        return RedirectToPage(new { slug });
    }

    private async Task LoadAsync(Guid organizationId, CancellationToken ct)
    {
        Pool = await _companyLicenses.GetPoolSummaryAsync(organizationId, ct).ConfigureAwait(false);
        Employees = await _companyLicenses.GetEmployeesAsync(organizationId, ct).ConfigureAwait(false);
        Licenses = await _companyLicenses.ListAsync(organizationId, ct).ConfigureAwait(false);
    }

    private async Task<Organization?> ResolveAuthorizedAsync(string slug, CancellationToken ct)
    {
        var normalized = (slug ?? string.Empty).Trim().ToLowerInvariant();

        var organization = await _db.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => !o.IsPersonal && o.Slug.ToLower() == normalized, ct)
            .ConfigureAwait(false);

        if (organization is null)
        {
            return null;
        }

        var role = await _db.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == organization.Id && m.UserId == UserId())
            .Select(m => (OrganizationRole?)m.Role)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return role is null || role == OrganizationRole.Member ? null : organization;
    }

    private Guid UserId()
        => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    public string RoleText(OrganizationRole role) => Ui.RoleText(role);

    public string StatusText(LicenseStatus status) => Ui.LicenseStatusText(status);

    public string StatusBadge(LicenseStatus status) => Ui.LicenseBadge(status);
}
