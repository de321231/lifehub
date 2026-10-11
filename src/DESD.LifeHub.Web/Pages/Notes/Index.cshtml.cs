using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Notes;

public class IndexModel : LifeHubPageModel
{
    private readonly INoteService _notes;

    public IndexModel(AppShell shell, INoteService notes) : base(shell)
    {
        _notes = notes;
        PageTitle = "Notizen";
        NavSection = "notes";
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tag { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OnlyFavorites { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OnlyArchived { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool IncludeArchived { get; set; }

    public IReadOnlyList<Note> Notes { get; private set; } = [];
    public IReadOnlyList<string> Categories { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        Categories = await _notes.GetCategoriesAsync(userId, ct).ConfigureAwait(false);

        var filter = new NoteFilter
        {
            Search = Search,
            Category = Category,
            Tag = Tag,
            OnlyFavorites = OnlyFavorites,
            IncludeArchived = IncludeArchived,
            OnlyArchived = OnlyArchived,
        };

        Notes = await _notes.ListAsync(userId, filter, ct).ConfigureAwait(false);
        return Page();
    }

    public async Task<IActionResult> OnPostToggleFavoriteAsync(Guid id, CancellationToken ct)
    {
        await _notes.ToggleFavoriteAsync(UserId(), id, ct).ConfigureAwait(false);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleArchiveAsync(Guid id, CancellationToken ct)
    {
        var result = await _notes.ToggleArchiveAsync(UserId(), id, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            SetError(result.Error ?? "Die Notiz konnte nicht verschoben werden.");
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        var result = await _notes.DeleteAsync(UserId(), id, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Die Notiz wurde gelöscht.");
        }
        else
        {
            SetError(result.Error ?? "Die Notiz konnte nicht gelöscht werden.");
        }

        return RedirectToPage();
    }

    public bool HasActiveFilter => !string.IsNullOrWhiteSpace(Search)
        || !string.IsNullOrWhiteSpace(Category)
        || !string.IsNullOrWhiteSpace(Tag)
        || OnlyFavorites
        || OnlyArchived
        || IncludeArchived;

    private Guid UserId()
        => Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
}