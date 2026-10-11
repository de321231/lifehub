using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Admin;

public class UsersModel : LifeHubPageModel
{
    private readonly IUserAccountService _accounts;

    public UsersModel(AppShell shell, IUserAccountService accounts) : base(shell)
    {
        _accounts = accounts;
        PageTitle = "Benutzerverwaltung";
        NavSection = "admin-users";
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public IReadOnlyList<AdminUserRow> Users { get; private set; } = [];

    /// <summary>Einmalig erzeugter Link zum Zurücksetzen eines Passworts (nur für die aktuelle Antwort).</summary>
    public string? ResetLink { get; private set; }

    public string? ResetLinkForEmail { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Users = await _accounts.ListUsersAsync(Search, ct).ConfigureAwait(false);
        return Page();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var result = await _accounts.SetActiveAsync(UserId(), id, isActive, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess(isActive ? "Das Konto wurde aktiviert." : "Das Konto wurde deaktiviert.");
        }
        else
        {
            SetError(result.Error ?? "Die Aktion konnte nicht ausgeführt werden.");
        }

        return RedirectToPage(new { Search });
    }

    public async Task<IActionResult> OnPostToggleAdminAsync(Guid id, bool isAdmin, CancellationToken ct)
    {
        var result = await _accounts.SetAdminRoleAsync(UserId(), id, isAdmin, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess(isAdmin ? "Die Administratorrolle wurde zugewiesen." : "Die Administratorrolle wurde entzogen.");
        }
        else
        {
            SetError(result.Error ?? "Die Aktion konnte nicht ausgeführt werden.");
        }

        return RedirectToPage(new { Search });
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(Guid id, CancellationToken ct)
    {
        Users = await _accounts.ListUsersAsync(Search, ct).ConfigureAwait(false);

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var result = await _accounts.GeneratePasswordResetLinkAsync(id, baseUrl, ct).ConfigureAwait(false);

        if (result.Succeeded)
        {
            ResetLink = result.Value;
            ResetLinkForEmail = Users.FirstOrDefault(u => u.UserId == id)?.Email;

            SetSuccess("Es wurde ein Link zum Zurücksetzen des Passworts erzeugt. Der Link wird nur einmal angezeigt.");
            return Page();
        }

        SetError(result.Error ?? "Der Link konnte nicht erzeugt werden.");
        return RedirectToPage(new { Search });
    }

    public string RoleText(string role) => IdentityRoles.Display(role);

    private Guid UserId()
        => Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
}