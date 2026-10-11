using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Services;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

namespace DESD.LifeHub.Web.Pages.Tasks;

public class DetailsModel : LifeHubPageModel
{
    private readonly ITaskService _tasks;
    private readonly IOrganizationAccessService _access;
    private readonly ITaskCollaborationService _collab;

    public DetailsModel(
        AppShell shell,
        ITaskService tasks,
        IOrganizationAccessService access,
        ITaskCollaborationService collab) : base(shell)
    {
        _tasks = tasks;
        _access = access;
        _collab = collab;
        PageTitle = "Aufgabe";
        NavSection = "tasks";
    }

    public TaskItem? Task { get; private set; }

    public bool CanEdit { get; private set; }

    public Guid UserIdValue { get; private set; }

    /// <summary>Organisationen des Benutzers für die Anzeige des Bereichsnamens.</summary>
    public IReadOnlyList<Organization> Organizations { get; private set; } = [];

    public IReadOnlyList<TaskAttachment> Attachments { get; private set; } = [];

    public IReadOnlyList<TaskComment> Comments { get; private set; } = [];

    [BindProperty]
    public string CommentBody { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        UserIdValue = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        Task = await _tasks.GetAsync(UserIdValue, id, ct).ConfigureAwait(false);
        if (Task is null)
        {
            SetError("Die Aufgabe wurde nicht gefunden.");
            return RedirectToPage("/Tasks/Index");
        }

        Organizations = await _access.GetSelectableOrganizationsAsync(UserIdValue, ct).ConfigureAwait(false);
        CanEdit = await CanEditAsync(Task, ct).ConfigureAwait(false);

        Attachments = await _collab.ListAttachmentsAsync(UserIdValue, id, ct).ConfigureAwait(false);
        Comments = await _collab.ListCommentsAsync(UserIdValue, id, ct).ConfigureAwait(false);

        PageTitle = Task.Title;
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        var result = await _tasks.DeleteAsync(UserIdValue, id, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Die Aufgabe wurde gelöscht.");
            return RedirectToPage("/Tasks/Index");
        }

        SetError(result.Error ?? "Die Aufgabe konnte nicht gelöscht werden.");
        return RedirectToPage("/Tasks/Details", new { id });
    }

    public async Task<IActionResult> OnPostAddCommentAsync(Guid id, CancellationToken ct)
    {
        var result = await _collab.AddCommentAsync(UserIdValue, id, CommentBody, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Kommentar hinzugefügt.");
            CommentBody = string.Empty;
        }
        else
        {
            SetError(result.Error ?? "Der Kommentar konnte nicht hinzugefügt werden.");
        }

        return RedirectToPage("/Tasks/Details", new { id });
    }

    public async Task<IActionResult> OnPostDeleteCommentAsync(Guid id, Guid commentId, CancellationToken ct)
    {
        var result = await _collab.DeleteCommentAsync(UserIdValue, commentId, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Kommentar entfernt.");
        }
        else
        {
            SetError(result.Error ?? "Der Kommentar konnte nicht entfernt werden.");
        }

        return RedirectToPage("/Tasks/Details", new { id });
    }

    public async Task<IActionResult> OnPostUploadAttachmentAsync(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            SetError("Bitte wählen Sie eine Datei aus.");
            return RedirectToPage("/Tasks/Details", new { id });
        }

        await using var stream = file.OpenReadStream();
        var result = await _collab.AddAttachmentAsync(UserIdValue, id, file.FileName, file.ContentType, stream, file.Length, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Anhang hinzugefügt.");
        }
        else
        {
            SetError(result.Error ?? "Der Anhang konnte nicht hinzugefügt werden.");
        }

        return RedirectToPage("/Tasks/Details", new { id });
    }

    public async Task<IActionResult> OnPostDeleteAttachmentAsync(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var result = await _collab.DeleteAttachmentAsync(UserIdValue, attachmentId, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            SetSuccess("Anhang entfernt.");
        }
        else
        {
            SetError(result.Error ?? "Der Anhang konnte nicht entfernt werden.");
        }

        return RedirectToPage("/Tasks/Details", new { id });
    }

    /// <summary>Name des Bereichs bzw. "Privat".</summary>
    public string OrgName(TaskItem task)
        => task.OrganizationId is { } orgId
            ? Organizations.FirstOrDefault(o => o.Id == orgId)?.Name ?? "Organisation"
            : "Privat";

    /// <summary>Anzeigename der zugewiesenen Person.</summary>
    public string AssigneeName(TaskItem task)
    {
        if (task.AssignedToUserId is null || task.AssignedTo is null)
        {
            return "Niemand";
        }

        return string.IsNullOrWhiteSpace(task.AssignedTo.DisplayName)
            ? task.AssignedTo.Email ?? "Zugewiesene Person"
            : task.AssignedTo.DisplayName;
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
}