using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DESD.LifeHub.Web.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class ErrorModel : PageModel
{
    public string? RequestCode { get; private set; }

    public bool IsNotFound { get; private set; }

    public void OnGet(int? code)
    {
        RequestCode = code?.ToString();
        IsNotFound = code is 404;
    }
}