// Projektweite using-Direktiven für das Web-Projekt.
// Ohne diese Direktiven fehlen in den Razor-Pagemodellen u. a. [AllowAnonymous]
// (Microsoft.AspNetCore.Authorization) und die MVC-/PageModel-Typen.
global using Microsoft.AspNetCore.Authorization;
global using Microsoft.AspNetCore.Identity;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.Mvc.RazorPages;
global using Microsoft.EntityFrameworkCore;
