using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>
/// Aufgabenverwaltung inklusive Mandantentrennung. Jede Abfrage erzwingt, dass nur
/// eigene private Aufgaben oder Aufgaben einer Organisation mit aktiver Mitgliedschaft
/// zurückgegeben werden.
/// </summary>
public interface ITaskService
{
    IQueryable<TaskItem> BuildVisibleQuery(Guid userId, IReadOnlySet<Guid> visibleOrgIds, TaskFilter filter);

    Task<IReadOnlyList<TaskItem>> ListAsync(Guid userId, TaskFilter filter, CancellationToken ct = default);

    Task<TaskItem?> GetAsync(Guid userId, Guid taskId, CancellationToken ct = default);

    Task<ServiceResult> CreateAsync(Guid userId, TaskInput input, CancellationToken ct = default);

    Task<ServiceResult> UpdateAsync(Guid userId, Guid taskId, TaskInput input, CancellationToken ct = default);

    Task<ServiceResult> ChangeStatusAsync(Guid userId, Guid taskId, TaskItemStatus status, CancellationToken ct = default);

    Task<ServiceResult> DeleteAsync(Guid userId, Guid taskId, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetCategoriesAsync(Guid userId, CancellationToken ct = default);

    Task<DashboardData> GetDashboardAsync(Guid userId, DateTimeOffset localDayStartUtc, DateTimeOffset localDayEndUtc, CancellationToken ct = default);
}

public sealed record DashboardData
{
    public IReadOnlyList<TaskItem> DueToday { get; init; } = [];
    public IReadOnlyList<TaskItem> Overdue { get; init; } = [];
    public IReadOnlyList<TaskItem> Upcoming { get; init; } = [];
    public int OpenCount { get; init; }
    public int CompletedCount { get; init; }
    public int OverdueCount { get; init; }
    public int DueTodayCount { get; init; }
}

public sealed class TaskService : ITaskService
{
    private readonly AppDbContext _db;
    private readonly IOrganizationAccessService _access;
    private readonly IReminderService _reminders;

    public TaskService(AppDbContext db, IOrganizationAccessService access, IReminderService reminders)
    {
        _db = db;
        _access = access;
        _reminders = reminders;
    }

    public IQueryable<TaskItem> BuildVisibleQuery(Guid userId, IReadOnlySet<Guid> visibleOrgIds, TaskFilter filter)
    {
        var orgIds = visibleOrgIds.ToList();

        var query = _db.Tasks
            .AsNoTracking()
            .Where(t =>
                (t.OrganizationId == null && t.OwnerUserId == userId) ||
                (t.OrganizationId != null && orgIds.Contains(t.OrganizationId.Value)));

        if (filter.OrganizationId.HasValue)
        {
            // Ausdrücklich angeforderter Bereich: Mitgliedschaft wurde bereits geprüft.
            query = query.Where(t => t.OrganizationId == filter.OrganizationId.Value);
        }

        if (filter.OnlyMine)
        {
            query = query.Where(t => t.OwnerUserId == userId);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(t => t.Status == filter.Status.Value);
        }

        if (filter.Priority.HasValue)
        {
            query = query.Where(t => t.Priority == filter.Priority.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            var category = filter.Category.Trim();
            query = query.Where(t => t.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(filter.Tag))
        {
            var tag = filter.Tag.Trim().ToLowerInvariant();
            var pattern = $"%{tag}%";
            query = query.Where(t => t.Tags != null && EF.Functions.Like(t.Tags, pattern));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            var pattern = $"%{term}%";
            query = query.Where(t =>
                EF.Functions.Like(t.Title, pattern) ||
                (t.Description != null && EF.Functions.Like(t.Description, pattern)) ||
                (t.Tags != null && EF.Functions.Like(t.Tags, pattern)));
        }

        if (filter.OnlyOpen)
        {
            query = query.Where(t => t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled);
        }

        if (filter.OnlyOverdue)
        {
            var now = DateTimeOffset.UtcNow;
            query = query.Where(t => t.DueAt != null && t.DueAt < now
                                     && t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled);
        }

        query = filter.Sort switch
        {
            TaskSort.DueDescending => query.OrderByDescending(t => t.DueAt == null).ThenByDescending(t => t.DueAt),
            TaskSort.CreatedDescending => query.OrderByDescending(t => t.CreatedAt),
            TaskSort.CreatedAscending => query.OrderBy(t => t.CreatedAt),
            TaskSort.PriorityDescending => query.OrderByDescending(t => t.Priority).ThenBy(t => t.DueAt == null).ThenBy(t => t.DueAt),
            TaskSort.TitleAscending => query.OrderBy(t => t.Title),
            _ => query.OrderBy(t => t.DueAt == null).ThenBy(t => t.DueAt).ThenByDescending(t => t.Priority),
        };

        return query;
    }

    public async Task<IReadOnlyList<TaskItem>> ListAsync(Guid userId, TaskFilter filter, CancellationToken ct = default)
    {
        var orgIds = await _access.GetVisibleOrganizationIdsAsync(userId, ct).ConfigureAwait(false);

        if (filter.OrganizationId.HasValue && !orgIds.Contains(filter.OrganizationId.Value))
        {
            // Keine Mitgliedschaft: kein Zugriff, keine Daten.
            return [];
        }

        return await BuildVisibleQuery(userId, orgIds, filter)
            .Include(t => t.Owner)
            .Include(t => t.AssignedTo)
            .Take(500)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<TaskItem?> GetAsync(Guid userId, Guid taskId, CancellationToken ct = default)
    {
        var orgIds = await _access.GetVisibleOrganizationIdsAsync(userId, ct).ConfigureAwait(false);
        var ids = orgIds.ToList();

        // Verknüpfte Notizen werden nur mitgeführt, wenn sie für den Benutzer
        // sichtbar sind (eigene Notizen oder mit einer eigenen Organisation geteilt) –
        // private Notizen anderer Benutzer bleiben auch über die Aufgabe verborgen.
        var task = await _db.Tasks
            .AsNoTracking()
            .Include(t => t.Owner)
            .Include(t => t.AssignedTo)
            .Include(t => t.Notes.Where(n =>
                n.OwnerUserId == userId ||
                (n.OrganizationId.HasValue && n.IsSharedWithOrganization && ids.Contains(n.OrganizationId.Value))))
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            .ConfigureAwait(false);

        if (task is null)
        {
            return null;
        }

        return VisibilityRules.CanView(orgIds, task.OrganizationId, task.OwnerUserId, userId) ? task : null;
    }

    public async Task<ServiceResult> CreateAsync(Guid userId, TaskInput input, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(input.Title))
        {
            errors["Title"] = "Bitte geben Sie einen Titel an.";
        }

        if (input.OrganizationId.HasValue && !await _access.IsMemberAsync(userId, input.OrganizationId.Value, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind kein Mitglied dieser Organisation.");
        }

        if (input.AssignedToUserId.HasValue && input.OrganizationId.HasValue)
        {
            var assignedOk = await _access.IsMemberAsync(input.AssignedToUserId.Value, input.OrganizationId.Value, ct).ConfigureAwait(false);
            if (!assignedOk)
            {
                errors["AssignedToUserId"] = "Die ausgewählte Person ist kein Mitglied dieser Organisation.";
            }
        }

        if (errors.Count > 0)
        {
            return ServiceResult.Fail("Bitte prüfen Sie die Eingaben.", errors);
        }

        var task = new TaskItem
        {
            OwnerUserId = userId,
            OrganizationId = input.OrganizationId,
            AssignedToUserId = input.OrganizationId.HasValue ? input.AssignedToUserId : null,
            Title = input.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            Status = input.Status,
            Priority = input.Priority,
            Category = string.IsNullOrWhiteSpace(input.Category) ? null : input.Category.Trim(),
            Tags = TimeUtil.NormalizeTags(input.Tags),
            DueAt = TimeUtil.ToUtc(input.DueAt),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        if (task.Status == TaskItemStatus.Done)
        {
            task.CompletedAt = task.CreatedAt;
        }

        _db.Tasks.Add(task);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _reminders.SyncForTaskAsync(userId, task, input.ReminderLeadMinutes, input.CreateReminder, ct).ConfigureAwait(false);

        return ServiceResult.Ok(task.Id);
    }

    public async Task<ServiceResult> UpdateAsync(Guid userId, Guid taskId, TaskInput input, CancellationToken ct = default)
    {
        var task = await LoadForWriteAsync(userId, taskId, ct).ConfigureAwait(false);
        if (task.Result is not null)
        {
            return task.Result;
        }

        var item = task.Task!;

        if (!await CanEditAsync(userId, item, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diese Aufgabe zu bearbeiten.");
        }

        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(input.Title))
        {
            errors["Title"] = "Bitte geben Sie einen Titel an.";
        }

        if (input.OrganizationId != item.OrganizationId)
        {
            if (input.OrganizationId.HasValue &&
                !await _access.IsMemberAsync(userId, input.OrganizationId.Value, ct).ConfigureAwait(false))
            {
                return ServiceResult.Fail("Sie sind kein Mitglied dieser Organisation.");
            }
        }

        if (errors.Count > 0)
        {
            return ServiceResult.Fail("Bitte prüfen Sie die Eingaben.", errors);
        }

        item.Title = input.Title.Trim();
        item.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        item.Priority = input.Priority;
        item.Category = string.IsNullOrWhiteSpace(input.Category) ? null : input.Category.Trim();
        item.Tags = TimeUtil.NormalizeTags(input.Tags);
        item.DueAt = TimeUtil.ToUtc(input.DueAt);
        item.OrganizationId = input.OrganizationId;
        item.AssignedToUserId = input.OrganizationId.HasValue ? input.AssignedToUserId : null;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        ApplyStatus(item, input.Status);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _reminders.SyncForTaskAsync(userId, item, input.ReminderLeadMinutes, input.CreateReminder, ct).ConfigureAwait(false);

        return ServiceResult.Ok(item.Id);
    }

    public async Task<ServiceResult> ChangeStatusAsync(Guid userId, Guid taskId, TaskItemStatus status, CancellationToken ct = default)
    {
        var task = await LoadForWriteAsync(userId, taskId, ct).ConfigureAwait(false);
        if (task.Result is not null)
        {
            return task.Result;
        }

        var item = task.Task!;
        var canEdit = await CanEditAsync(userId, item, ct).ConfigureAwait(false);
        var isAssignee = item.AssignedToUserId == userId;

        if (!canEdit && !isAssignee)
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diese Aufgabe zu ändern.");
        }

        ApplyStatus(item, status);
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(item.Id);
    }

    public async Task<ServiceResult> DeleteAsync(Guid userId, Guid taskId, CancellationToken ct = default)
    {
        var task = await LoadForWriteAsync(userId, taskId, ct).ConfigureAwait(false);
        if (task.Result is not null)
        {
            return task.Result;
        }

        var item = task.Task!;
        if (!await CanEditAsync(userId, item, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diese Aufgabe zu löschen.");
        }

        _db.Tasks.Remove(item);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(taskId);
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(Guid userId, CancellationToken ct = default)
    {
        var orgIds = await _access.GetVisibleOrganizationIdsAsync(userId, ct).ConfigureAwait(false);

        return await BuildVisibleQuery(userId, orgIds, new TaskFilter())
            .Where(t => t.Category != null)
            .Select(t => t.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<DashboardData> GetDashboardAsync(
        Guid userId,
        DateTimeOffset localDayStartUtc,
        DateTimeOffset localDayEndUtc,
        CancellationToken ct = default)
    {
        var orgIds = await _access.GetVisibleOrganizationIdsAsync(userId, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var start = TimeUtil.ToUtc(localDayStartUtc);
        var end = TimeUtil.ToUtc(localDayEndUtc);

        var visible = BuildVisibleQuery(userId, orgIds, new TaskFilter());

        var openQuery = visible.Where(t => t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled);

        var dueToday = await openQuery
            .Where(t => t.DueAt != null && t.DueAt >= start && t.DueAt < end)
            .OrderBy(t => t.Priority)
            .Take(10)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var overdue = await openQuery
            .Where(t => t.DueAt != null && t.DueAt < now)
            .OrderBy(t => t.DueAt)
            .Take(10)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var upcoming = await openQuery
            .Where(t => t.DueAt != null && t.DueAt >= end)
            .OrderBy(t => t.DueAt)
            .Take(10)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var openCount = await openQuery.CountAsync(ct).ConfigureAwait(false);
        var completedCount = await visible.CountAsync(t => t.Status == TaskItemStatus.Done, ct).ConfigureAwait(false);
        var overdueCount = await openQuery.CountAsync(t => t.DueAt != null && t.DueAt < now, ct).ConfigureAwait(false);
        var dueTodayCount = await openQuery.CountAsync(t => t.DueAt != null && t.DueAt >= start && t.DueAt < end, ct).ConfigureAwait(false);

        return new DashboardData
        {
            DueToday = dueToday,
            Overdue = overdue,
            Upcoming = upcoming,
            OpenCount = openCount,
            CompletedCount = completedCount,
            OverdueCount = overdueCount,
            DueTodayCount = dueTodayCount,
        };
    }

    // ------------------------------------------------------------ intern

    private async Task<(TaskItem? Task, ServiceResult? Result)> LoadForWriteAsync(Guid userId, Guid taskId, CancellationToken ct)
    {
        var orgIds = await _access.GetVisibleOrganizationIdsAsync(userId, ct).ConfigureAwait(false);

        var item = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return (null, ServiceResult.Fail("Die Aufgabe wurde nicht gefunden."));
        }

        // Auch für Schreibzugriffe gilt die Sichtbarkeitsregel: fremde Daten sind
        // nicht erreichbar, selbst wenn die ID bekannt ist.
        if (!VisibilityRules.CanView(orgIds, item.OrganizationId, item.OwnerUserId, userId))
        {
            return (null, ServiceResult.Fail("Die Aufgabe wurde nicht gefunden."));
        }

        return (item, null);
    }

    private async Task<bool> CanEditAsync(Guid userId, TaskItem item, CancellationToken ct)
    {
        if (item.OwnerUserId == userId)
        {
            return true;
        }

        if (item.OrganizationId.HasValue)
        {
            return await _access.CanManageOrganizationAsync(userId, item.OrganizationId.Value, ct).ConfigureAwait(false);
        }

        return false;
    }

    private static void ApplyStatus(TaskItem item, TaskItemStatus status)
    {
        var wasDone = item.Status == TaskItemStatus.Done;
        item.Status = status;

        if (status == TaskItemStatus.Done && !wasDone)
        {
            item.CompletedAt = DateTimeOffset.UtcNow;
        }
        else if (status != TaskItemStatus.Done)
        {
            item.CompletedAt = null;
        }
    }
}
