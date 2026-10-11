using System.ComponentModel.DataAnnotations;
using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Options;
using DESD.LifeHub.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DESD.LifeHub.Web.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IWorkspaceService _workspaceService;
    private readonly BootstrapOptions _bootstrap;
    private readonly DESD.LifeHub.Infrastructure.Data.AppDbContext _db;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> users,
        IWorkspaceService workspaceService,
        IOptions<BootstrapOptions> bootstrap,
        DESD.LifeHub.Infrastructure.Data.AppDbContext db)
    {
        _signInManager = signInManager;
        _users = users;
        _workspaceService = workspaceService;
        _bootstrap = bootstrap.Value;
        _db = db;
    }

    [BindProperty]
    public LoginInputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool AllowSelfRegistration => _bootstrap.AllowSelfRegistration;

    public void OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            Response.Redirect("/Dashboard");
        }
    }

    [EnableRateLimiting("login")]
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _users.FindByEmailAsync(Input.Email.Trim()).ConfigureAwait(false);

        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "E-Mail-Adresse oder Passwort ist nicht korrekt.");
            return Page();
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Ihr Konto ist deaktiviert. Bitte wenden Sie sich an einen Administrator.");
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(
            user.UserName!, Input.Password, Input.RememberMe, lockoutOnFailure: true).ConfigureAwait(false);

        if (result.Succeeded)
        {
            user.LastLoginAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync().ConfigureAwait(false);
            return await HandleWorkspaceSelectionAsync(user.Id).ConfigureAwait(false);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty,
                "Das Konto ist vorübergehend gesperrt (zu viele Fehlversuche). Bitte warten Sie einige Minuten.");
            return Page();
        }

        ModelState.AddModelError(string.Empty, "E-Mail-Adresse oder Passwort ist nicht korrekt.");
        return Page();
    }

    private async Task<IActionResult> HandleWorkspaceSelectionAsync(Guid userId)
    {
        var workspaces = await _workspaceService.GetForUserAsync(userId).ConfigureAwait(false);

        if (workspaces.Count == 0)
        {
            return RedirectToPage("/Dashboard");
        }

        if (workspaces.Count == 1)
        {
            var workspace = workspaces[0];
            if (workspace.Type == WorkspaceType.Personal)
            {
                return RedirectToPage("/Dashboard");
            }
            else
            {
                return RedirectToPage("/CompanyAdmin/Index", new { slug = workspace.CompanyId });
            }
        }

        // Multiple workspaces - redirect to workspace selector page
        // The selector page will load workspaces again (or we can use TempData)
        return RedirectToPage("/Account/WorkspaceSelector");
    }

    public sealed class LoginInputModel
    {
        [Required(ErrorMessage = "Bitte geben Sie Ihre E-Mail-Adresse an.")]
        [EmailAddress(ErrorMessage = "Bitte geben Sie eine gültige E-Mail-Adresse an.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bitte geben Sie Ihr Passwort an.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}