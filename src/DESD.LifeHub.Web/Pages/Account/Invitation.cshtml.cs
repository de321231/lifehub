using System.ComponentModel.DataAnnotations;
using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DESD.LifeHub.Web.Pages.Account;

[AllowAnonymous]
public class InvitationModel : PageModel
{
    private readonly IUserAccountService _accounts;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public InvitationModel(IUserAccountService accounts, SignInManager<ApplicationUser> signInManager)
    {
        _accounts = accounts;
        _signInManager = signInManager;
    }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public InvitationInputModel Input { get; set; } = new();

    public bool TokenMissing => string.IsNullOrWhiteSpace(Token);

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Dashboard");
        }

        if (TokenMissing)
        {
            ModelState.AddModelError(string.Empty,
                "Der Einladungslink ist unvollständig. Bitte verwenden Sie den vollständigen Link aus Ihrer Einladung.");
        }

        return Page();
    }

    [EnableRateLimiting("register")]
    public async Task<IActionResult> OnPostAsync()
    {
        if (TokenMissing)
        {
            ModelState.AddModelError(string.Empty,
                "Der Einladungslink ist unvollständig. Bitte verwenden Sie den vollständigen Link aus Ihrer Einladung.");
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

        var result = await _accounts.AcceptInvitationAsync(Token!, Input.Password).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Die Einladung konnte nicht eingelöst werden.");
            return Page();
        }

        if (result.Id is { } userId)
        {
            var user = await _signInManager.UserManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
            if (user is not null)
            {
                await _signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);
                return RedirectToPage("/Dashboard", new { welcomed = true });
            }
        }

        return RedirectToPage("/Account/Login");
    }

    public sealed class InvitationInputModel
    {
        [Required(ErrorMessage = "Bitte geben Sie ein Passwort an.")]
        [StringLength(100, MinimumLength = 12, ErrorMessage = "Das Passwort muss mindestens 12 Zeichen enthalten.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bitte wiederholen Sie das Passwort.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}