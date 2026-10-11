using System.ComponentModel.DataAnnotations;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Account;

[Authorize]
public class ChangePasswordModel : LifeHubPageModel
{
    private readonly IUserAccountService _accounts;

    public ChangePasswordModel(AppShell shell, IUserAccountService accounts) : base(shell)
    {
        _accounts = accounts;
        PageTitle = "Passwort ändern";
        NavSection = "";
    }

    [BindProperty]
    public ChangePasswordInputModel Input { get; set; } = new();

    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Input.NewPassword != Input.ConfirmPassword)
        {
            ModelState.AddModelError("Input.ConfirmPassword", "Die Passwörter stimmen nicht überein.");
            return Page();
        }

        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _accounts.ChangePasswordAsync(userId, Input.CurrentPassword, Input.NewPassword).ConfigureAwait(false);

        if (result.Succeeded)
        {
            SetSuccess("Ihr Passwort wurde geändert.");
            Input = new ChangePasswordInputModel();
            return Page();
        }

        ApplyServiceResult(result);
        return Page();
    }

    public sealed class ChangePasswordInputModel
    {
        [Required(ErrorMessage = "Bitte geben Sie Ihr aktuelles Passwort an.")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bitte geben Sie ein neues Passwort an.")]
        [StringLength(100, MinimumLength = 12, ErrorMessage = "Das Passwort muss mindestens 12 Zeichen enthalten.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bitte wiederholen Sie das neue Passwort.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}