using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Tasks;

public class BoardModel : LifeHubPageModel
{
    private readonly ITaskService _tasks;
    private readonly IOrganizationAccessService _access;

    public BoardModel(AppShell shell, ITaskService tasks, IOrganizationAccessService access) : base(shell)
    {
        _tasks = tasks;
        _access = access;
        PageTitle = "Aufgaben-Board";
        NavSection = "tasks";
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public TaskPriority? Priority { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? OrganizationId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OnlyMine { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OnlyOverdue { get; set; }

    public IReadOnlyList<Organization> Organizations { get; private set; } = [];
    public IReadOnlyList<string> Categories { get; private set; } = [];
    public System.Collections.Generic.Dictionary<TaskItemStatus, IReadOnlyList<TaskItem>> Columns { get; private set; } = new();

    public static readonly TaskItemStatus[] Statuses =
    [
        TaskItemStatus.Open,
        TaskItemStatus.InProgress,
        TaskItemStatus.Blocked,
        TaskItemStatus.Done,
        TaskItemStatus.Cancelled,
    ];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var userId = UserId();
        Organizations = await _access.GetSelectableOrganizationsAsync(userId, ct).ConfigureAwait(false);
        Categories = await _tasks.GetCategoriesAsync(userId, ct).ConfigureAwait(false);

        var filter = new TaskFilter
        {
            Search = Search,
            Priority = Priority,
            Category = Category,
            OrganizationId = OrganizationId,
            OnlyMine = OnlyMine,
            OnlyOverdue = OnlyOverdue,
            Sort = TaskSort.PriorityDescending,
        };

        var all = await _tasks.ListAsync(userId, filter, ct).ConfigureAwait(false);

        Columns = new System.Collections.Generic.Dictionary<TaskItemStatus, IReadOnlyList<TaskItem>>();
        foreach (var status in Statuses)
        {
            Columns[status] = all.Where(t => t.Status == status).ToList();
        }

        return Page();
    }

    public bool HasActiveFilter => !string.IsNullOrWhiteSpace(Search)
        || Priority.HasValue || !string.IsNullOrWhiteSpace(Category)
        || OrganizationId.HasValue || OnlyMine || OnlyOverdue;

    private Guid UserId()
        => Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
}