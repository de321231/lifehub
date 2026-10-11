using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DESD.LifeHub.Web.Pages;

public class IndexModel : PageModel
{
    public void OnGet()
    {
        // Landing page shown to all users; redirect to login only if accessed via direct link while logged in
        if (User.Identity?.IsAuthenticated == true && !Request.Path.Value.Contains("landing"))
        {
            Response.Redirect("/Dashboard");
        }
    }
}