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
public class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IOrganizationService _organizations;
    private readonly IAuditService _audit;
    private readonly BootstrapOptions _bootstrap;

    public RegisterModel(
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signInManager,
        IOrganizationService organizations,
        IAuditService audit,
        IOptions<BootstrapOptions> bootstrap)
    {
        _users = users;
        _signInManager = signInManager;
        _organizations = organizations;
        _audit = audit;
        _bootstrap = bootstrap.Value;
    }

    [BindProperty]
    public RegisterInputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool RegistrationAllowed => _bootstrap.AllowSelfRegistration;

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Dashboard");
        }

        if (!_bootstrap.AllowSelfRegistration)
        {
            return RedirectToPage("/Account/Login",
                new { message = "registration_disabled" });
        }

        return Page();
    }

    [EnableRateLimiting("register")]
    public async Task<IActionResult> OnPostAsync()
    {
        if (!_bootstrap.AllowSelfRegistration)
        {
            ModelState.AddModelError(string.Empty, "Die Selbstregistrierung ist deaktiviert.");
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Input.Password != Input.ConfirmPassword)
        {
            ModelState.AddModelError("Input.ConfirmPassword", "Die Passwörter stimmen nicht überein.");
            return Page();
        }

        var existing = await _users.FindByEmailAsync(Input.Email.Trim()).ConfigureAwait(false);
        if (existing is not null)
        {
            ModelState.AddModelError("Input.Email", "Diese E-Mail-Adresse wird bereits verwendet.");
            return Page();
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = Input.Email.Trim(),
            Email = Input.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = Input.DisplayName.Trim(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var create = await _users.CreateAsync(user, Input.Password).ConfigureAwait(false);
        if (!create.Succeeded)
        {
            foreach (var error in create.Errors)
            {
                ModelState.AddModelError("Input.Password", Translate(error));
            }

            return Page();
        }

        await _users.AddToRoleAsync(user, IdentityRoles.User).ConfigureAwait(false);
        await _organizations.EnsurePersonalOrganizationAsync(user.Id, user.DisplayName, user.Email).ConfigureAwait(false);
        await _audit.WriteAsync("user.registered", user.Id,
            entityType: nameof(ApplicationUser), entityId: user.Id.ToString()).ConfigureAwait(false);

        await _signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return Redirect(ReturnUrl);
        }

        return RedirectToPage("/License/Index", new { newAccount = true });
    }

    private static string Translate(IdentityError error) => error.Code switch
    {
        "PasswordTooShort" => "Das Passwort ist zu kurz (mindestens 12 Zeichen).",
        "PasswordRequiresNonAlphanumeric" => "Das Passwort benötigt mindestens ein Sonderzeichen.",
        "PasswordRequiresDigit" => "Das Passwort benötigt mindestens eine Zahl.",
        "PasswordRequiresLower" => "Das Passwort benötigt mindestens einen Kleinbuchstaben.",
        "PasswordRequiresUpper" => "Das Passwort benötigt mindestens einen Grossbuchstaben.",
        "PasswordRequiresUniqueChars" => "Das Passwort benötigt mindestens 5 verschiedene Zeichen.",
        _ => "Die Passwortregeln wurden nicht erfüllt.",
    };

    public sealed class RegisterInputModel
    {
        [Required(ErrorMessage = "Bitte geben Sie einen Namen an.")]
        [StringLength(120, ErrorMessage = "Der Name darf höchstens 120 Zeichen enthalten.")]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bitte geben Sie eine E-Mail-Adresse an.")]
        [EmailAddress(ErrorMessage = "Bitte geben Sie eine gültige E-Mail-Adresse an.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bitte geben Sie ein Passwort an.")]
        [StringLength(100, MinimumLength = 12, ErrorMessage = "Das Passwort muss mindestens 12 Zeichen enthalten.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bitte wiederholen Sie das Passwort.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}