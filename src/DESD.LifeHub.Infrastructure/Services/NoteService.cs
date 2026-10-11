using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>
/// Notizverwaltung. Notizen bleiben privat, solange sie nicht ausdrücklich mit einer
/// Organisation geteilt werden. Fremde Notizen sind ausschliesslich lesbar, wenn sie
/// geteilt wurden – und niemals bearbeitbar.
/// </summary>
public interface INoteService
{
    Task<IReadOnlyList<Note>> ListAsync(Guid userId, NoteFilter filter, CancellationToken ct = default);

    /// <summary>Zuletzt geänderte Notizen für das Dashboard.</summary>
    Task<IReadOnlyList<Note>> GetRecentAsync(Guid userId, int count, CancellationToken ct = default);

    Task<Note?> GetAsync(Guid userId, Guid noteId, CancellationToken ct = default);

    Task<ServiceResult> CreateAsync(Guid userId, NoteInput input, CancellationToken ct = default);

    Task<ServiceResult> UpdateAsync(Guid userId, Guid noteId, NoteInput input, CancellationToken ct = default);

    Task<ServiceResult> DeleteAsync(Guid userId, Guid noteId, CancellationToken ct = default);

    Task<ServiceResult> ToggleFavoriteAsync(Guid userId, Guid noteId, CancellationToken ct = default);

    Task<ServiceResult> ToggleArchiveAsync(Guid userId, Guid noteId, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetCategoriesAsync(Guid userId, CancellationToken ct = default);
}

public sealed class NoteService : INoteService
{
    private readonly AppDbContext _db;
    private readonly IOrganizationAccessService _access;

    public NoteService(AppDbContext db, IOrganizationAccessService access)
    {
        _db = db;
        _access = access;
    }

    private IQueryable<Note> BuildVisibleQuery(Guid userId, IReadOnlyList<Guid> orgIds)
    {
        var ids = orgIds.ToList();

        return _db.Notes
            .AsNoTracking()
            .Where(n =>
                n.OwnerUserId == userId ||
                (n.OrganizationId != null && n.IsSharedWithOrganization && ids.Contains(n.OrganizationId.Value)));
    }

    public async Task<IReadOnlyList<Note>> ListAsync(Guid userId, NoteFilter filter, CancellationToken ct = default)
    {
        var orgIds = await _access.GetOrganizationIdsAsync(userId, ct).ConfigureAwait(false);
        var query = BuildVisibleQuery(userId, orgIds);

        if (filter.OnlyArchived)
        {
            query = query.Where(n => n.IsArchived);
        }
        else if (!filter.IncludeArchived)
        {
            query = query.Where(n => !n.IsArchived);
        }

        if (filter.OnlyFavorites)
        {
            query = query.Where(n => n.IsFavorite);
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            var category = filter.Category.Trim();
            query = query.Where(n => n.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(filter.Tag))
        {
            var pattern = $"%{filter.Tag.Trim().ToLowerInvariant()}%";
            query = query.Where(n => n.Tags != null && EF.Functions.Like(n.Tags, pattern));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search.Trim()}%";
            query = query.Where(n =>
                EF.Functions.Like(n.Title, pattern) ||
                EF.Functions.Like(n.Content, pattern) ||
                (n.Tags != null && EF.Functions.Like(n.Tags, pattern)));
        }

        return await query
            .OrderByDescending(n => n.IsFavorite)
            .ThenByDescending(n => n.UpdatedAt ?? n.CreatedAt)
            .Take(500)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Note>> GetRecentAsync(Guid userId, int count, CancellationToken ct = default)
    {
        var orgIds = await _access.GetOrganizationIdsAsync(userId, ct).ConfigureAwait(false);

        return await BuildVisibleQuery(userId, orgIds)
            .Where(n => !n.IsArchived)
            .OrderByDescending(n => n.UpdatedAt ?? n.CreatedAt)
            .Take(Math.Clamp(count, 1, 50))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Note?> GetAsync(Guid userId, Guid noteId, CancellationToken ct = default)
    {
        var orgIds = await _access.GetOrganizationIdsAsync(userId, ct).ConfigureAwait(false);

        var note = await _db.Notes
            .AsNoTracking()
            .Include(n => n.Owner)
            .Include(n => n.TaskItem)
            .FirstOrDefaultAsync(n => n.Id == noteId, ct)
            .ConfigureAwait(false);

        if (note is null)
        {
            return null;
        }

        var canView = note.OwnerUserId == userId
                      || (note.OrganizationId.HasValue && note.IsSharedWithOrganization && orgIds.Contains(note.OrganizationId.Value));

        return canView ? note : null;
    }

    public async Task<ServiceResult> CreateAsync(Guid userId, NoteInput input, CancellationToken ct = default)
    {
        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return ServiceResult.Fail("Bitte prüfen Sie die Eingaben.", errors);
        }

        if (input.IsSharedWithOrganization && input.OrganizationId.HasValue &&
            !await _access.IsMemberAsync(userId, input.OrganizationId.Value, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind kein Mitglied dieser Organisation.");
        }

        if (input.OrganizationId.HasValue)
        {
            var isMember = await _access.IsMemberAsync(userId, input.OrganizationId.Value, ct).ConfigureAwait(false);
            if (!isMember)
            {
                return ServiceResult.Fail("Sie sind kein Mitglied dieser Organisation.");
            }
        }

        var note = new Note
        {
            OwnerUserId = userId,
            OrganizationId = input.OrganizationId,
            IsSharedWithOrganization = input.IsSharedWithOrganization && input.OrganizationId.HasValue,
            Title = input.Title.Trim(),
            Content = input.Content.Trim(),
            Category = string.IsNullOrWhiteSpace(input.Category) ? null : input.Category.Trim(),
            Tags = TimeUtil.NormalizeTags(input.Tags),
            IsFavorite = input.IsFavorite,
            IsArchived = input.IsArchived,
            TaskItemId = input.TaskItemId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Notes.Add(note);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ServiceResult.Ok(note.Id);
    }

    public async Task<ServiceResult> UpdateAsync(Guid userId, Guid noteId, NoteInput input, CancellationToken ct = default)
    {
        var note = await _db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, ct).ConfigureAwait(false);

        if (note is null || note.OwnerUserId != userId)
        {
            // Geteilte Notizen anderer Benutzer sind absichtlich nicht bearbeitbar.
            return ServiceResult.Fail("Die Notiz wurde nicht gefunden oder Sie sind nicht berechtigt, sie zu ändern.");
        }

        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return ServiceResult.Fail("Bitte prüfen Sie die Eingaben.", errors);
        }

        if (input.OrganizationId.HasValue &&
            !await _access.IsMemberAsync(userId, input.OrganizationId.Value, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind kein Mitglied dieser Organisation.");
        }

        note.Title = input.Title.Trim();
        note.Content = input.Content.Trim();
        note.Category = string.IsNullOrWhiteSpace(input.Category) ? null : input.Category.Trim();
        note.Tags = TimeUtil.NormalizeTags(input.Tags);
        note.IsFavorite = input.IsFavorite;
        note.IsArchived = input.IsArchived;
        note.OrganizationId = input.OrganizationId;
        note.IsSharedWithOrganization = input.IsSharedWithOrganization && input.OrganizationId.HasValue;
        note.TaskItemId = input.TaskItemId;
        note.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(note.Id);
    }

    public async Task<ServiceResult> DeleteAsync(Guid userId, Guid noteId, CancellationToken ct = default)
    {
        var note = await _db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, ct).ConfigureAwait(false);

        if (note is null || note.OwnerUserId != userId)
        {
            return ServiceResult.Fail("Die Notiz wurde nicht gefunden.");
        }

        _db.Notes.Remove(note);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(noteId);
    }

    public async Task<ServiceResult> ToggleFavoriteAsync(Guid userId, Guid noteId, CancellationToken ct = default)
    {
        var note = await _db.Notes.FirstOrDefaultAsync(n => n.Id == noteId && n.OwnerUserId == userId, ct).ConfigureAwait(false);
        if (note is null)
        {
            return ServiceResult.Fail("Die Notiz wurde nicht gefunden.");
        }

        note.IsFavorite = !note.IsFavorite;
        note.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(noteId);
    }

    public async Task<ServiceResult> ToggleArchiveAsync(Guid userId, Guid noteId, CancellationToken ct = default)
    {
        var note = await _db.Notes.FirstOrDefaultAsync(n => n.Id == noteId && n.OwnerUserId == userId, ct).ConfigureAwait(false);
        if (note is null)
        {
            return ServiceResult.Fail("Die Notiz wurde nicht gefunden.");
        }

        note.IsArchived = !note.IsArchived;
        note.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(noteId);
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(Guid userId, CancellationToken ct = default)
    {
        var orgIds = await _access.GetOrganizationIdsAsync(userId, ct).ConfigureAwait(false);

        return await BuildVisibleQuery(userId, orgIds)
            .Where(n => n.Category != null)
            .Select(n => n.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    private static Dictionary<string, string> Validate(NoteInput input)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(input.Title))
        {
            errors["Title"] = "Bitte geben Sie einen Titel an.";
        }

        if (input.Title.Length > 200)
        {
            errors["Title"] = "Der Titel darf höchstens 200 Zeichen enthalten.";
        }

        return errors;
    }
}
