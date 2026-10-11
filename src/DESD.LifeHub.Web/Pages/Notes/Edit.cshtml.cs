using System.ComponentModel.DataAnnotations;
using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Notes;

public class EditModel : LifeHubPageModel
{
    private readonly INoteService _notes;
    private readonly IOrganizationAccessService _access;

    public EditModel(AppShell shell, INoteService notes, IOrganizationAccessService access) : base(shell)
    {
        _notes = notes;
        _access = access;
        PageTitle = "Notiz bearbeiten";
        NavSection = "notes";
    }

    [BindProperty]
    public Guid Id { get; set; }

    [BindProperty]
    public NoteFormModel Input { get; set; } = new();

    public IReadOnlyList<Organization> Organizations { get; private set; } = [];
    public IReadOnlyList<string> Categories { get; private set; } = [];

    public Guid UserIdValue { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Id = id;
        UserIdValue = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        var note = await _notes.GetAsync(UserIdValue, id, ct).ConfigureAwait(false);
        if (note is null)
        {
            SetError("Die Notiz wurde nicht gefunden.");
            return RedirectToPage("/Notes/Index");
        }

        // Geteilte Notizen anderer Benutzer sind lesbar, aber nicht bearbeitbar.
        if (note.OwnerUserId != UserIdValue)
        {
            SetError("Sie sind nicht berechtigt, diese Notiz zu bearbeiten.");
            return RedirectToPage("/Notes/Index");
        }

        await LoadOptionsAsync(ct).ConfigureAwait(false);

        Input = new NoteFormModel
        {
            Title = note.Title,
            Content = note.Content,
            Category = note.Category,
            Tags = note.Tags,
            IsFavorite = note.IsFavorite,
            IsArchived = note.IsArchived,
            OrganizationId = note.OrganizationId,
            IsSharedWithOrganization = note.IsSharedWithOrganization,
            TaskItemId = note.TaskItemId,
        };

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
            IsArchived = Input.IsArchived,
            OrganizationId = Input.OrganizationId,
            IsSharedWithOrganization = Input.IsSharedWithOrganization && Input.OrganizationId.HasValue,
            TaskItemId = Input.TaskItemId,
        };

        var result = await _notes.UpdateAsync(UserIdValue, Id, input, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Die Notiz wurde gespeichert.");
            return RedirectToPage("/Notes/Edit", new { id = Id });
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

        public bool IsArchived { get; set; }

        public Guid? OrganizationId { get; set; }

        public bool IsSharedWithOrganization { get; set; }

        public Guid? TaskItemId { get; set; }
    }
}