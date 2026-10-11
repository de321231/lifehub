using System.ComponentModel.DataAnnotations;
using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DESD.LifeHub.Web.Pages.CompanyAdmin;

/// <summary>
/// Anmeldung für die Firmenverwaltung unter <c>/{FIRMEN-ID}/adminlogin</c>.
/// Nach erfolgreicher Anmeldung wird geprüft, dass das Konto als Manager oder
/// Eigentümer der Firma hinterlegt ist.
/// </summary>
[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public LoginModel(AppDbContext db, SignInManager<ApplicationUser> signInManager)
    {
        _db = db;
        _signInManager = signInManager;
    }

    [BindProperty(SupportsGet = true)]
    public string Slug { get; set; } = string.Empty;

    [BindProperty]
    public LoginInputModel Input { get; set; } = new();

    public string? CompanyName { get; private set; }

    public bool CompanyNotFound { get; private set; }

    private Guid? OrganizationId { get; set; }

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken ct)
    {
        Slug = slug;
        await ResolveCompanyAsync(slug, ct).ConfigureAwait(false);
        return Page();
    }

    [EnableRateLimiting("login")]
    public async Task<IActionResult> OnPostAsync(string slug, CancellationToken ct)
    {
        Slug = slug;
        await ResolveCompanyAsync(slug, ct).ConfigureAwait(false);

        if (OrganizationId is not { } organizationId)
        {
            ModelState.AddModelError(string.Empty, "Diese Firma wurde nicht gefunden.");
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _signInManager.UserManager.FindByEmailAsync(Input.Email).ConfigureAwait(false);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "E-Mail-Adresse oder Passwort ist nicht korrekt.");
            return Page();
        }

        var result = await _signInManager
            .PasswordSignInAsync(user, Input.Password, isPersistent: false, lockoutOnFailure: true)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.IsLockedOut
                ? "Das Konto ist vorübergehend gesperrt. Bitte später erneut versuchen."
                : "E-Mail-Adresse oder Passwort ist nicht korrekt.");
            return Page();
        }

        // Rolle innerhalb der Firma prüfen – nur Manager/Eigentümer dürfen verwalten.
        var role = await _db.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.UserId == user.Id)
            .Select(m => (OrganizationRole?)m.Role)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (role is null || role == OrganizationRole.Member)
        {
            await _signInManager.SignOutAsync().ConfigureAwait(false);
            ModelState.AddModelError(string.Empty,
                "Ihr Konto ist nicht als Firmenadministrator dieser Firma hinterlegt.");
            return Page();
        }

        return RedirectToPage("/CompanyAdmin/Index", new { slug = Slug });
    }

    private async Task ResolveCompanyAsync(string slug, CancellationToken ct)
    {
        var normalized = (slug ?? string.Empty).Trim().ToLowerInvariant();

        var organization = await _db.Organizations
            .AsNoTracking()
            .Where(o => !o.IsPersonal && o.Slug.ToLower() == normalized)
            .Select(o => new { o.Id, o.Name })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (organization is null)
        {
            CompanyNotFound = true;
            return;
        }

        OrganizationId = organization.Id;
        CompanyName = organization.Name;
    }

    public sealed class LoginInputModel
    {
        [Required(ErrorMessage = "Bitte geben Sie Ihre E-Mail-Adresse ein.")]
        [EmailAddress(ErrorMessage = "Bitte geben Sie eine gültige E-Mail-Adresse ein.")]
        [Display(Name = "E-Mail")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bitte geben Sie Ihr Passwort ein.")]
        [DataType(DataType.Password)]
        [Display(Name = "Passwort")]
        public string Password { get; set; } = string.Empty;
    }
}
