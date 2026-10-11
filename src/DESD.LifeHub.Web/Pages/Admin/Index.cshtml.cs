using DESD.LifeHub.Infrastructure.Licensing;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Admin;

public class IndexModel : LifeHubPageModel
{
    private readonly IUserAccountService _accounts;
    private readonly LicenseService _licenses;

    public IndexModel(AppShell shell, IUserAccountService accounts, LicenseService licenses) : base(shell)
    {
        _accounts = accounts;
        _licenses = licenses;
        PageTitle = "Verwaltung";
        NavSection = "admin-overview";
    }

    public int TotalUsers { get; private set; }
    public int ActiveUsers { get; private set; }
    public int AdminUsers { get; private set; }
    public int ValidLicenses { get; private set; }
    public int OpenInvitations { get; private set; }
    public IReadOnlyList<AdminUserRow> RecentUsers { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var users = await _accounts.ListUsersAsync(ct: ct).ConfigureAwait(false);
        TotalUsers = users.Count;
        ActiveUsers = users.Count(u => u.IsActive);
        AdminUsers = users.Count(u => u.Roles.Contains(IdentityRoles.Admin));

        ValidLicenses = await _licenses.CountValidLicensesAsync(ct).ConfigureAwait(false);

        var invitations = await _accounts.ListOpenInvitationsAsync(ct).ConfigureAwait(false);
        OpenInvitations = invitations.Count;

        RecentUsers = users
            .OrderByDescending(u => u.CreatedAt)
            .Take(10)
            .ToList();

        return Page();
    }
}