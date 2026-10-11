using System.ComponentModel.DataAnnotations;
using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace DESD.LifeHub.Web.Pages.Tasks;

public class EditModel : LifeHubPageModel
{
    private readonly ITaskService _tasks;
    private readonly IOrganizationAccessService _access;
    private readonly IOrganizationService _organizations;

    public EditModel(
        AppShell shell,
        ITaskService tasks,
        IOrganizationAccessService access,
        IOrganizationService organizations)
        : base(shell)
    {
        _tasks = tasks;
        _access = access;
        _organizations = organizations;
        PageTitle = "Aufgabe bearbeiten";
        NavSection = "tasks";
    }

    [BindProperty]
    public Guid Id { get; set; }

    [BindProperty]
    public TaskFormModel Input { get; set; } = new();

    public IReadOnlyList<Organization> Organizations { get; private set; } = [];
    public IReadOnlyList<OrganizationMemberView> Members { get; private set; } = [];
    public IReadOnlyList<string> Categories { get; private set; } = [];

    public Guid UserIdValue { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Id = id;
        UserIdValue = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        var task = await _tasks.GetAsync(UserIdValue, id, ct).ConfigureAwait(false);
        if (task is null)
        {
            SetError("Die Aufgabe wurde nicht gefunden.");
            return RedirectToPage("/Tasks/Index");
        }

        if (!await CanEditAsync(task, ct).ConfigureAwait(false))
        {
            SetError("Sie sind nicht berechtigt, diese Aufgabe zu bearbeiten.");
            return RedirectToPage("/Tasks/Details", new { id });
        }

        await LoadOptionsAsync(task.OrganizationId, ct).ConfigureAwait(false);

        Input = new TaskFormModel
        {
            Title = task.Title,
            Description = task.Description,
            Status = task.Status,
            Priority = task.Priority,
            Category = task.Category,
            Tags = task.Tags,
            DueAt = LocalInputDate(task.DueAt),
            OrganizationId = task.OrganizationId,
            AssignedToUserId = task.AssignedToUserId,
            CreateReminder = task.DueAt.HasValue,
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        UserIdValue = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        await LoadOptionsAsync(Input.OrganizationId, ct).ConfigureAwait(false);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var input = new TaskInput
        {
            Title = Input.Title,
            Description = Input.Description,
            Status = Input.Status,
            Priority = Input.Priority,
            Category = Input.Category,
            Tags = Input.Tags,
            DueAt = LocalToUtc(Input.DueAt),
            OrganizationId = Input.OrganizationId,
            AssignedToUserId = Input.OrganizationId.HasValue ? Input.AssignedToUserId : null,
            CreateReminder = Input.CreateReminder,
            ReminderLeadMinutes = Input.ReminderLeadMinutes,
        };

        var result = await _tasks.UpdateAsync(UserIdValue, Id, input, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Die Aufgabe wurde gespeichert.");
            return RedirectToPage("/Tasks/Details", new { id = result.Id });
        }

        ApplyServiceResult(result);
        return Page();
    }

    private async Task<bool> CanEditAsync(TaskItem task, CancellationToken ct)
    {
        if (task.OwnerUserId == UserIdValue)
        {
            return true;
        }

        if (task.OrganizationId.HasValue)
        {
            return await _access.CanManageOrganizationAsync(UserIdValue, task.OrganizationId.Value, ct).ConfigureAwait(false);
        }

        return false;
    }

    private async Task LoadOptionsAsync(Guid? organizationId, CancellationToken ct)
    {
        Organizations = await _access.GetSelectableOrganizationsAsync(UserIdValue, ct).ConfigureAwait(false);
        Categories = await _tasks.GetCategoriesAsync(UserIdValue, ct).ConfigureAwait(false);

        if (organizationId.HasValue)
        {
            Members = await _organizations.GetMembersAsync(UserIdValue, organizationId.Value, ct).ConfigureAwait(false);
        }
    }

    public sealed class TaskFormModel
    {
        [Required(ErrorMessage = "Bitte geben Sie einen Titel an.")]
        [StringLength(200, ErrorMessage = "Der Titel darf höchstens 200 Zeichen enthalten.")]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public TaskItemStatus Status { get; set; } = TaskItemStatus.Open;

        public TaskPriority Priority { get; set; } = TaskPriority.Normal;

        public string? Category { get; set; }

        public string? Tags { get; set; }

        /// <summary>Fälligkeit in lokaler Benutzerzeit.</summary>
        public DateTime? DueAt { get; set; }

        public bool CreateReminder { get; set; }

        public int? ReminderLeadMinutes { get; set; }

        public Guid? OrganizationId { get; set; }

        public Guid? AssignedToUserId { get; set; }
    }
}