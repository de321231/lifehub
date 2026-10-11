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

[ AllowAnonymous ]
public class CompanyAdminLoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IWorkspaceService _workspaceService;
    private readonly BootstrapOptions _bootstrap;

    public CompanyAdminLoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> users,
        IWorkspaceService workspaceService,
        IOptions<BootstrapOptions> bootstrap)
    {
        _signInManager = signInManager;
        _users = users;
        _workspaceService = workspaceService;
        _bootstrap = bootstrap.Value;
    }

    [ BindProperty ]
    public LoginInputModel Input { get; set; } = new();

    [ BindProperty(SupportsGet = true) ]
    public string? CompanyId { get; set; }

    [ BindProperty(SupportsGet = true) ]
    public string? ReturnUrl { get; set; }

    public bool AllowSelfRegistration => _bootstrap.AllowSelfRegistration;

    public void OnGet(string? company)
    {
        CompanyId = company;
        if (User.Identity?.IsAuthenticated == true)
        {
            Response.Redirect($"/{company}/adminlogin");
        }
    }

    [ EnableRateLimiting("login") ]
    public async Task<IActionResult> OnPostAsync(string? company)
    {
        CompanyId = company;
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
            // Check if user belongs to this company
            var workspaces = await _workspaceService.GetForUserAsync(user.Id).ConfigureAwait(false);
            var companyWorkspace = workspaces?.FirstOrDefault(w => w.CompanyId == company);
            
            if (companyWorkspace == null)
            {
                ModelState.AddModelError(string.Empty, "Sie sind kein Mitglied dieses Unternehmens.");
                return Page();
            }

            if (companyWorkspace.Type != WorkspaceType.Company)
            {
                ModelState.AddModelError(string.Empty, "Sie haben keinen Zugriff auf diesen Firmenbereich.");
                return Page();
            }

            return Redirect($"/{company}/adminlogin");
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

    public sealed class LoginInputModel
    {
        [ Required(ErrorMessage = "Bitte geben Sie Ihre E-Mail-Adresse an.") ]
        [ EmailAddress(ErrorMessage = "Bitte geben Sie eine gültige E-Mail-Adresse an.") ]
        public string Email { get; set; } = string.Empty;

        [ Required(ErrorMessage = "Bitte geben Sie Ihr Passwort an.") ]
        [ DataType(DataType.Password) ]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}