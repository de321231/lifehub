using System.ComponentModel.DataAnnotations;
using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Notes;

public class CreateModel : LifeHubPageModel
{
    private readonly INoteService _notes;
    private readonly IOrganizationAccessService _access;

    public CreateModel(AppShell shell, INoteService notes, IOrganizationAccessService access) : base(shell)
    {
        _notes = notes;
        _access = access;
        PageTitle = "Neue Notiz";
        NavSection = "notes";
    }

    [BindProperty]
    public NoteFormModel Input { get; set; } = new();

    public IReadOnlyList<Organization> Organizations { get; private set; } = [];
    public IReadOnlyList<string> Categories { get; private set; } = [];

    public Guid UserIdValue { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? taskid, CancellationToken ct)
    {
        UserIdValue = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        if (taskid.HasValue)
        {
            Input.TaskItemId = taskid;
        }

        await LoadOptionsAsync(ct).ConfigureAwait(false);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        UserIdValue = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        await LoadOptionsAsync(ct).ConfigureAwait(false);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var input = new NoteInput
        {
            Title = Input.Title,
            Content = Input.Content,
            Category = Input.Category,
            Tags = Input.Tags,
            IsFavorite = Input.IsFavorite,
            IsArchived = false,
            OrganizationId = Input.OrganizationId,
            IsSharedWithOrganization = Input.IsSharedWithOrganization && Input.OrganizationId.HasValue,
            TaskItemId = Input.TaskItemId,
        };

        var result = await _notes.CreateAsync(UserIdValue, input, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Die Notiz wurde angelegt.");
            if (Input.TaskItemId.HasValue)
            {
                return RedirectToPage("/Tasks/Details", new { id = Input.TaskItemId.Value });
            }

            return RedirectToPage("/Notes/Edit", new { id = result.Id });
        }

        ApplyServiceResult(result);
        return Page();
    }

    private async Task LoadOptionsAsync(CancellationToken ct)
    {
        Organizations = await _access.GetSelectableOrganizationsAsync(UserIdValue, ct).ConfigureAwait(false);
        Categories = await _notes.GetCategoriesAsync(UserIdValue, ct).ConfigureAwait(false);
    }

    public sealed class NoteFormModel
    {
        [Required(ErrorMessage = "Bitte geben Sie einen Titel an.")]
        [StringLength(200, ErrorMessage = "Der Titel darf höchstens 200 Zeichen enthalten.")]
        public string Title { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public string? Category { get; set; }

        public string? Tags { get; set; }

        public bool IsFavorite { get; set; }

        public Guid? OrganizationId { get; set; }

        public bool IsSharedWithOrganization { get; set; }

        public Guid? TaskItemId { get; set; }
    }
}