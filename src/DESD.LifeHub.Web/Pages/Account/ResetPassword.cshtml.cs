using System.ComponentModel.DataAnnotations;
using DESD.LifeHub.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DESD.LifeHub.Web.Pages.Account;

[AllowAnonymous]
public class ResetPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;

    public ResetPasswordModel(UserManager<ApplicationUser> users)
    {
        _users = users;
    }

    [BindProperty(SupportsGet = true)]
    public Guid? UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Code { get; set; }

    [BindProperty]
    public ResetPasswordInputModel Input { get; set; } = new();

    public bool LinkMissing => !UserId.HasValue || string.IsNullOrWhiteSpace(Code);

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Dashboard");
        }

        if (LinkMissing)
        {
            ModelState.AddModelError(string.Empty, "Der Link zum Zurücksetzen ist unvollständig.");
        }

        return Page();
    }

    [EnableRateLimiting("register")]
    public async Task<IActionResult> OnPostAsync()
    {
        if (LinkMissing)
        {
            ModelState.AddModelError(string.Empty, "Der Link zum Zurücksetzen ist unvollständig.");
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

        var user = await _users.FindByIdAsync(UserId!.Value.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Das Benutzerkonto wurde nicht gefunden.");
            return Page();
        }

        var result = await _users.ResetPasswordAsync(user, Code!, Input.Password).ConfigureAwait(false);
        if (result.Succeeded)
        {
            ViewData["ResetSucceeded"] = true;
            Input = new ResetPasswordInputModel();
            return Page();
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError("Input.Password", Translate(error));
        }

        return Page();
    }

    private static string Translate(IdentityError error) => error.Code switch
    {
        "PasswordTooShort" => "Das Passwort ist zu kurz (mindestens 12 Zeichen).",
        "PasswordRequiresNonAlphanumeric" => "Das Passwort benötigt mindestens ein Sonderzeichen.",
        "PasswordRequiresDigit" => "Das Passwort benötigt mindestens eine Zahl.",
        "PasswordRequiresLower" => "Das Passwort benötigt mindestens einen Kleinbuchstaben.",
        "PasswordRequiresUpper" => "Das Passwort benötigt mindestens einen Grossbuchstaben.",
        "PasswordRequiresUniqueChars" => "Das Passwort benötigt mindestens 5 verschiedene Zeichen.",
        "InvalidToken" => "Der Link ist ungültig oder abgelaufen. Bitte fordern Sie einen neuen an.",
        _ => error.Description,
    };

    public sealed class ResetPasswordInputModel
    {
        [Required(ErrorMessage = "Bitte geben Sie ein neues Passwort an.")]
        [StringLength(100, MinimumLength = 12, ErrorMessage = "Das Passwort muss mindestens 12 Zeichen enthalten.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bitte wiederholen Sie das neue Passwort.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}