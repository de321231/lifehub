using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Tasks;

public class IndexModel : LifeHubPageModel
{
    private readonly ITaskService _tasks;
    private readonly IOrganizationAccessService _access;

    public IndexModel(AppShell shell, ITaskService tasks, IOrganizationAccessService access) : base(shell)
    {
        _tasks = tasks;
        _access = access;
        PageTitle = "Aufgaben";
        NavSection = "tasks";
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public TaskItemStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public TaskPriority? Priority { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tag { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? OrganizationId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OnlyMine { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OnlyOverdue { get; set; }

    [BindProperty(SupportsGet = true)]
    public TaskSort Sort { get; set; } = TaskSort.DueAscending;

    public IReadOnlyList<TaskItem> Tasks { get; private set; } = [];
    public IReadOnlyList<Organization> Organizations { get; private set; } = [];
    public IReadOnlyList<string> Categories { get; private set; } = [];
    public IReadOnlyList<string> Tags { get; private set; } = [];

    public System.Collections.Generic.HashSet<Guid> ManageableOrgIds { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var userId = UserId();
        Organizations = await _access.GetSelectableOrganizationsAsync(userId, ct).ConfigureAwait(false);

        ManageableOrgIds = new System.Collections.Generic.HashSet<Guid>();
        foreach (var org in Organizations)
        {
            if (await _access.CanManageOrganizationAsync(userId, org.Id, ct).ConfigureAwait(false))
            {
                ManageableOrgIds.Add(org.Id);
            }
        }

        Categories = await _tasks.GetCategoriesAsync(userId, ct).ConfigureAwait(false);

        var filter = BuildFilter();
        Tasks = await _tasks.ListAsync(userId, filter, ct).ConfigureAwait(false);
        Tags = Tasks.Where(t => t.Tags != null)
            .SelectMany(t => Ui.SplitTags(t.Tags))
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        var result = await _tasks.DeleteAsync(UserId(), id, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Die Aufgabe wurde gelöscht.");
        }
        else
        {
            SetError(result.Error ?? "Die Aufgabe konnte nicht gelöscht werden.");
        }

        return RedirectToPage();
    }

    public TaskFilter BuildFilter() => new()
    {
        Search = Search,
        Status = Status,
        Priority = Priority,
        Category = Category,
        Tag = Tag,
        OrganizationId = OrganizationId,
        OnlyMine = OnlyMine,
        OnlyOverdue = OnlyOverdue,
        Sort = Sort,
    };

    /// <summary>Darfs der aktuelle Benutzer die Aufgabe bearbeiten (Eigentümer oder Manager)?</summary>
    public bool CanEdit(TaskItem task, Guid userId)
        => task.OwnerUserId == userId
           || (task.OrganizationId.HasValue && ManageableOrgIds.Contains(task.OrganizationId.Value));

    public bool HasActiveFilter => !string.IsNullOrWhiteSpace(Search)
        || Status.HasValue || Priority.HasValue || !string.IsNullOrWhiteSpace(Category)
        || !string.IsNullOrWhiteSpace(Tag) || OrganizationId.HasValue || OnlyMine || OnlyOverdue;

    private Guid UserId()
        => Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
}