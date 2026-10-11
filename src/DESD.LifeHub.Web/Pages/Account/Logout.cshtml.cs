using DESD.LifeHub.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DESD.LifeHub.Web.Pages.Account;

public class LogoutModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;

    public LogoutModel(SignInManager<ApplicationUser> signInManager) => _signInManager = signInManager;

    public IActionResult OnGet() => RedirectToPage("/Dashboard");

    public async Task<IActionResult> OnPostAsync()
    {
        await _signInManager.SignOutAsync().ConfigureAwait(false);
        return RedirectToPage("/Account/Login");
    }
}