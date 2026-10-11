using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Data;
using DESD.LifeHub.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>
/// Anhänge und Kommentare zu Aufgaben. Sämtliche Zugriffe werden gegen dieselben
/// Sichtbarkeitsregeln geprüft wie die Aufgabe selbst; Dateien liegen ausserhalb der
/// Datenbank und werden nur über geprüfte Endpunkte ausgeliefert.
/// </summary>
public interface ITaskCollaborationService
{
    Task<IReadOnlyList<TaskAttachment>> ListAttachmentsAsync(Guid userId, Guid taskId, CancellationToken ct = default);

    Task<ServiceResult> AddAttachmentAsync(Guid userId, Guid taskId, string fileName, string contentType, Stream content, long length, CancellationToken ct = default);

    Task<TaskAttachment?> GetAttachmentAsync(Guid userId, Guid attachmentId, CancellationToken ct = default);

    Task<ServiceResult> DeleteAttachmentAsync(Guid userId, Guid attachmentId, CancellationToken ct = default);

    Task<IReadOnlyList<TaskComment>> ListCommentsAsync(Guid userId, Guid taskId, CancellationToken ct = default);

    Task<ServiceResult> AddCommentAsync(Guid userId, Guid taskId, string body, CancellationToken ct = default);

    Task<ServiceResult> DeleteCommentAsync(Guid userId, Guid commentId, CancellationToken ct = default);
}

public sealed class TaskCollaborationService : ITaskCollaborationService
{
    private readonly AppDbContext _db;
    private readonly IOrganizationAccessService _access;
    private readonly IFileStorage _storage;
    private readonly StorageOptions _storageOptions;
    private readonly IAuditService _audit;

    public TaskCollaborationService(
        AppDbContext db,
        IOrganizationAccessService access,
        IFileStorage storage,
        IOptions<StorageOptions> storageOptions,
        IAuditService audit)
    {
        _db = db;
        _access = access;
        _storage = storage;
        _storageOptions = storageOptions.Value;
        _audit = audit;
    }

    public async Task<IReadOnlyList<TaskAttachment>> ListAttachmentsAsync(Guid userId, Guid taskId, CancellationToken ct = default)
    {
        var task = await LoadVisibleTaskAsync(userId, taskId, ct).ConfigureAwait(false);
        if (task is null)
        {
            return [];
        }

        return await _db.TaskAttachments
            .AsNoTracking()
            .Include(a => a.UploadedBy)
            .Where(a => a.TaskItemId == taskId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ServiceResult> AddAttachmentAsync(Guid userId, Guid taskId, string fileName, string contentType, Stream content, long length, CancellationToken ct = default)
    {
        var task = await LoadVisibleTaskAsync(userId, taskId, ct).ConfigureAwait(false);
        if (task is null)
        {
            return ServiceResult.Fail("Die Aufgabe wurde nicht gefunden.");
        }

        if (length <= 0)
        {
            return ServiceResult.Fail("Die Datei ist leer.");
        }

        if (length > _storageOptions.MaxAttachmentBytes)
        {
            var maxMb = _storageOptions.MaxAttachmentBytes / (1024 * 1024);
            return ServiceResult.Fail($"Die Datei ist zu gross (maximal {maxMb} MB).");
        }

        var safeName = SanitizeFileName(fileName);
        if (safeName is null)
        {
            return ServiceResult.Fail("Der Dateiname ist ungültig.");
        }

        var key = await _storage.SaveAsync(content, ct).ConfigureAwait(false);

        var attachment = new TaskAttachment
        {
            TaskItemId = taskId,
            FileName = safeName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType[..Math.Min(contentType.Length, 160)],
            SizeBytes = length,
            StorageKey = key,
            UploadedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.TaskAttachments.Add(attachment);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.WriteAsync("task.attachment.add", userId, task.OrganizationId, "TaskAttachment", attachment.Id.ToString(), safeName, cancellationToken: ct).ConfigureAwait(false);

        return ServiceResult.Ok(attachment.Id);
    }

    public async Task<TaskAttachment?> GetAttachmentAsync(Guid userId, Guid attachmentId, CancellationToken ct = default)
    {
        var attachment = await _db.TaskAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId, ct)
            .ConfigureAwait(false);

        if (attachment is null)
        {
            return null;
        }

        var task = await LoadVisibleTaskAsync(userId, attachment.TaskItemId, ct).ConfigureAwait(false);
        return task is null ? null : attachment;
    }

    public async Task<ServiceResult> DeleteAttachmentAsync(Guid userId, Guid attachmentId, CancellationToken ct = default)
    {
        var attachment = await _db.TaskAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId, ct).ConfigureAwait(false);
        if (attachment is null)
        {
            return ServiceResult.Fail("Der Anhang wurde nicht gefunden.");
        }

        var task = await LoadVisibleTaskAsync(userId, attachment.TaskItemId, ct).ConfigureAwait(false);
        if (task is null)
        {
            return ServiceResult.Fail("Der Anhang wurde nicht gefunden.");
        }

        var canDelete = task.OwnerUserId == userId
                        || attachment.UploadedByUserId == userId
                        || (task.OrganizationId.HasValue && await _access.CanManageOrganizationAsync(userId, task.OrganizationId.Value, ct).ConfigureAwait(false));

        if (!canDelete)
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diesen Anhang zu löschen.");
        }

        _db.TaskAttachments.Remove(attachment);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _storage.DeleteAsync(attachment.StorageKey, ct).ConfigureAwait(false);

        return ServiceResult.Ok(attachmentId);
    }

    public async Task<IReadOnlyList<TaskComment>> ListCommentsAsync(Guid userId, Guid taskId, CancellationToken ct = default)
    {
        var task = await LoadVisibleTaskAsync(userId, taskId, ct).ConfigureAwait(false);
        if (task is null)
        {
            return [];
        }

        return await _db.TaskComments
            .AsNoTracking()
            .Include(c => c.Author)
            .Where(c => c.TaskItemId == taskId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ServiceResult> AddCommentAsync(Guid userId, Guid taskId, string body, CancellationToken ct = default)
    {
        var trimmed = body?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return ServiceResult.Fail("Bitte geben Sie einen Kommentar ein.");
        }

        if (trimmed.Length > 4000)
        {
            return ServiceResult.Fail("Der Kommentar darf höchstens 4000 Zeichen enthalten.");
        }

        var task = await LoadVisibleTaskAsync(userId, taskId, ct).ConfigureAwait(false);
        if (task is null)
        {
            return ServiceResult.Fail("Die Aufgabe wurde nicht gefunden.");
        }

        var comment = new TaskComment
        {
            TaskItemId = taskId,
            AuthorUserId = userId,
            Body = trimmed,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.TaskComments.Add(comment);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ServiceResult.Ok(comment.Id);
    }

    public async Task<ServiceResult> DeleteCommentAsync(Guid userId, Guid commentId, CancellationToken ct = default)
    {
        var comment = await _db.TaskComments.FirstOrDefaultAsync(c => c.Id == commentId, ct).ConfigureAwait(false);
        if (comment is null)
        {
            return ServiceResult.Fail("Der Kommentar wurde nicht gefunden.");
        }

        var task = await LoadVisibleTaskAsync(userId, comment.TaskItemId, ct).ConfigureAwait(false);
        if (task is null)
        {
            return ServiceResult.Fail("Der Kommentar wurde nicht gefunden.");
        }

        var canDelete = comment.AuthorUserId == userId
                        || task.OwnerUserId == userId
                        || (task.OrganizationId.HasValue && await _access.CanManageOrganizationAsync(userId, task.OrganizationId.Value, ct).ConfigureAwait(false));

        if (!canDelete)
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diesen Kommentar zu löschen.");
        }

        _db.TaskComments.Remove(comment);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(commentId);
    }

    // ------------------------------------------------------------------ intern

    private async Task<TaskItem?> LoadVisibleTaskAsync(Guid userId, Guid taskId, CancellationToken ct)
    {
        var task = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct).ConfigureAwait(false);
        if (task is null)
        {
            return null;
        }

        var orgIds = await _access.GetVisibleOrganizationIdsAsync(userId, ct).ConfigureAwait(false);
        return VisibilityRules.CanView(orgIds, task.OrganizationId, task.OwnerUserId, userId) ? task : null;
    }

    private static string? SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var name = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return name.Length > 260 ? name[..260] : name;
    }
}
