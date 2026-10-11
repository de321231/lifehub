using System.ComponentModel.DataAnnotations;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Organizations;

public class DetailsModel : LifeHubPageModel
{
    private readonly IOrganizationService _organizations;

    public DetailsModel(AppShell shell, IOrganizationService organizations) : base(shell)
    {
        _organizations = organizations;
        PageTitle = "Organisation";
        NavSection = "organizations";
    }

    public Guid Id { get; private set; }

    public OrganizationSummary? Summary { get; private set; }

    public IReadOnlyList<OrganizationMemberView> Members { get; private set; } = [];

    [BindProperty]
    public RenameOrganizationInputModel RenameInput { get; set; } = new();

    [BindProperty]
    public AddMemberInputModel AddMemberInput { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Id = id;
        return await LoadAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<IActionResult> OnPostRenameAsync(Guid id, CancellationToken ct)
    {
        Id = id;
        var result = await _organizations.RenameAsync(UserId(), id, RenameInput.Name, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Der Name der Organisation wurde geändert.");
            return RedirectToPage(new { id });
        }

        ApplyServiceResult(result);
        return await LoadAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<IActionResult> OnPostAddMemberAsync(Guid id, CancellationToken ct)
    {
        Id = id;
        if (!ModelState.IsValid)
        {
            return await LoadAsync(id, ct).ConfigureAwait(false);
        }

        var result = await _organizations.AddMemberByEmailAsync(
            UserId(), id, AddMemberInput.Email, AddMemberInput.Role, ct).ConfigureAwait(false);

        if (result.Succeeded)
        {
            SetSuccess("Die Person wurde als Mitglied hinzugefügt.");
            AddMemberInput = new AddMemberInputModel();
            return RedirectToPage(new { id });
        }

        ApplyServiceResult(result);
        return await LoadAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<IActionResult> OnPostChangeRoleAsync(Guid id, Guid memberId, OrganizationRole role, CancellationToken ct)
    {
        var result = await _organizations.ChangeRoleAsync(UserId(), id, memberId, role, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Die Rolle wurde geändert.");
        }
        else
        {
            SetError(result.Error ?? "Die Rolle konnte nicht geändert werden.");
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveMemberAsync(Guid id, Guid memberId, CancellationToken ct)
    {
        var result = await _organizations.RemoveMemberAsync(UserId(), id, memberId, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess(memberId == UserId() ? "Sie haben die Organisation verlassen." : "Das Mitglied wurde entfernt.");
        }
        else
        {
            SetError(result.Error ?? "Das Mitglied konnte nicht entfernt werden.");
        }

        return RedirectToPage(new { id });
    }

    public string RoleText(OrganizationRole role) => Ui.RoleText(role);

    private async Task<IActionResult> LoadAsync(Guid id, CancellationToken ct)
    {
        var userId = UserId();
        Summary = await _organizations.GetSummaryAsync(userId, id, ct).ConfigureAwait(false);

        if (Summary is null)
        {
            SetError("Die Organisation wurde nicht gefunden oder Sie sind kein Mitglied.");
            return RedirectToPage("/Organizations/Index");
        }

        PageTitle = Summary.Name;
        Members = await _organizations.GetMembersAsync(userId, id, ct).ConfigureAwait(false);
        return Page();
    }

    private Guid UserId()
        => Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    public sealed class RenameOrganizationInputModel
    {
        [Required(ErrorMessage = "Bitte geben Sie einen Namen an.")]
        [StringLength(160, ErrorMessage = "Der Name darf höchstens 160 Zeichen enthalten.")]
        public string Name { get; set; } = string.Empty;
    }

    public sealed class AddMemberInputModel
    {
        [Required(ErrorMessage = "Bitte geben Sie die E-Mail-Adresse an.")]
        [EmailAddress(ErrorMessage = "Bitte geben Sie eine gültige E-Mail-Adresse an.")]
        public string Email { get; set; } = string.Empty;

        public OrganizationRole Role { get; set; } = OrganizationRole.Member;
    }
}