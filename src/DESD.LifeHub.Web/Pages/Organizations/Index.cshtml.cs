using System.ComponentModel.DataAnnotations;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Organizations;

public class IndexModel : LifeHubPageModel
{
    private readonly IOrganizationService _organizations;

    public IndexModel(AppShell shell, IOrganizationService organizations) : base(shell)
    {
        _organizations = organizations;
        PageTitle = "Organisationen";
        NavSection = "organizations";
    }

    [BindProperty]
    public CreateOrganizationInputModel Input { get; set; } = new();

    public IReadOnlyList<OrganizationSummary> Organizations { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Organizations = await _organizations.GetForUserAsync(UserId(), ct).ConfigureAwait(false);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct).ConfigureAwait(false);
            return Page();
        }

        var result = await _organizations.CreateAsync(UserId(), Input.Name, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Die Organisation wurde angelegt.");
            Input = new CreateOrganizationInputModel();
            return RedirectToPage("/Organizations/Details", new { id = result.Id });
        }

        ApplyServiceResult(result);
        await LoadAsync(ct).ConfigureAwait(false);
        return Page();
    }

    public string RoleText(OrganizationRole role) => Ui.RoleText(role);

    private async Task LoadAsync(CancellationToken ct)
        => Organizations = await _organizations.GetForUserAsync(UserId(), ct).ConfigureAwait(false);

    private Guid UserId()
        => Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    public sealed class CreateOrganizationInputModel
    {
        [Required(ErrorMessage = "Bitte geben Sie einen Namen für die Organisation an.")]
        [StringLength(160, ErrorMessage = "Der Name darf höchstens 160 Zeichen enthalten.")]
        public string Name { get; set; } = string.Empty;
    }
}