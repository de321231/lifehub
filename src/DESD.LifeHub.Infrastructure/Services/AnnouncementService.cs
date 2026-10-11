using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>Firmenankündigungen: Autoren sind Manager/Owner, sichtbar für alle Mitglieder.</summary>
public interface IAnnouncementService
{
    /// <summary>Veröffentlichte, nicht abgelaufene Ankündigungen aller Organisationen des Benutzers.</summary>
    Task<IReadOnlyList<CompanyAnnouncement>> GetActiveForUserAsync(Guid userId, int count, CancellationToken ct = default);

    /// <summary>Alle Ankündigungen einer Organisation (nur für Verwaltende).</summary>
    Task<IReadOnlyList<CompanyAnnouncement>> GetForOrganizationAsync(Guid userId, Guid organizationId, CancellationToken ct = default);

    Task<CompanyAnnouncement?> GetAsync(Guid userId, Guid announcementId, CancellationToken ct = default);

    Task<ServiceResult> CreateAsync(Guid userId, AnnouncementInput input, CancellationToken ct = default);

    Task<ServiceResult> UpdateAsync(Guid userId, Guid announcementId, AnnouncementInput input, CancellationToken ct = default);

    Task<ServiceResult> DeleteAsync(Guid userId, Guid announcementId, CancellationToken ct = default);
}

public sealed class AnnouncementService : IAnnouncementService
{
    private readonly AppDbContext _db;
    private readonly IOrganizationAccessService _access;
    private readonly IAuditService _audit;

    public AnnouncementService(AppDbContext db, IOrganizationAccessService access, IAuditService audit)
    {
        _db = db;
        _access = access;
        _audit = audit;
    }

    public async Task<IReadOnlyList<CompanyAnnouncement>> GetActiveForUserAsync(Guid userId, int count, CancellationToken ct = default)
    {
        var orgIds = await _access.GetOrganizationIdsAsync(userId, ct).ConfigureAwait(false);
        var ids = orgIds.ToList();
        var now = DateTimeOffset.UtcNow;

        return await _db.CompanyAnnouncements
            .AsNoTracking()
            .Include(a => a.Author)
            .Include(a => a.Organization)
            .Where(a => ids.Contains(a.OrganizationId)
                        && a.PublishAt <= now
                        && (a.ExpiresAt == null || a.ExpiresAt >= now))
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.Priority)
            .ThenByDescending(a => a.PublishAt)
            .Take(Math.Clamp(count, 1, 100))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CompanyAnnouncement>> GetForOrganizationAsync(Guid userId, Guid organizationId, CancellationToken ct = default)
    {
        if (!await _access.CanManageOrganizationAsync(userId, organizationId, ct).ConfigureAwait(false))
        {
            return [];
        }

        return await _db.CompanyAnnouncements
            .AsNoTracking()
            .Include(a => a.Author)
            .Where(a => a.OrganizationId == organizationId)
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.PublishAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<CompanyAnnouncement?> GetAsync(Guid userId, Guid announcementId, CancellationToken ct = default)
    {
        var item = await _db.CompanyAnnouncements
            .AsNoTracking()
            .Include(a => a.Author)
            .FirstOrDefaultAsync(a => a.Id == announcementId, ct)
            .ConfigureAwait(false);

        if (item is null)
        {
            return null;
        }

        // Verwaltende dürfen auch nicht veröffentlichte Ankündigungen sehen.
        if (await _access.CanManageOrganizationAsync(userId, item.OrganizationId, ct).ConfigureAwait(false))
        {
            return item;
        }

        var isMember = await _access.IsMemberAsync(userId, item.OrganizationId, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var visible = isMember && item.PublishAt <= now && (item.ExpiresAt == null || item.ExpiresAt >= now);
        return visible ? item : null;
    }

    public async Task<ServiceResult> CreateAsync(Guid userId, AnnouncementInput input, CancellationToken ct = default)
    {
        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return ServiceResult.Fail("Bitte prüfen Sie die Eingaben.", errors);
        }

        if (!await _access.CanManageOrganizationAsync(userId, input.OrganizationId, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, Ankündigungen dieser Organisation zu erstellen.");
        }

        var item = new CompanyAnnouncement
        {
            OrganizationId = input.OrganizationId,
            AuthorUserId = userId,
            Title = input.Title.Trim(),
            Body = input.Body.Trim(),
            Priority = input.Priority,
            IsPinned = input.IsPinned,
            PublishAt = TimeUtil.ToUtc(input.PublishAt),
            ExpiresAt = TimeUtil.ToUtc(input.ExpiresAt),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.CompanyAnnouncements.Add(item);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _audit.WriteAsync("announcement.create", userId, item.OrganizationId, "CompanyAnnouncement", item.Id.ToString(), item.Title, cancellationToken: ct).ConfigureAwait(false);

        return ServiceResult.Ok(item.Id);
    }

    public async Task<ServiceResult> UpdateAsync(Guid userId, Guid announcementId, AnnouncementInput input, CancellationToken ct = default)
    {
        var item = await _db.CompanyAnnouncements.FirstOrDefaultAsync(a => a.Id == announcementId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return ServiceResult.Fail("Die Ankündigung wurde nicht gefunden.");
        }

        if (!await _access.CanManageOrganizationAsync(userId, item.OrganizationId, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diese Ankündigung zu bearbeiten.");
        }

        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return ServiceResult.Fail("Bitte prüfen Sie die Eingaben.", errors);
        }

        item.Title = input.Title.Trim();
        item.Body = input.Body.Trim();
        item.Priority = input.Priority;
        item.IsPinned = input.IsPinned;
        item.PublishAt = TimeUtil.ToUtc(input.PublishAt);
        item.ExpiresAt = TimeUtil.ToUtc(input.ExpiresAt);
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(item.Id);
    }

    public async Task<ServiceResult> DeleteAsync(Guid userId, Guid announcementId, CancellationToken ct = default)
    {
        var item = await _db.CompanyAnnouncements.FirstOrDefaultAsync(a => a.Id == announcementId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return ServiceResult.Fail("Die Ankündigung wurde nicht gefunden.");
        }

        if (!await _access.CanManageOrganizationAsync(userId, item.OrganizationId, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diese Ankündigung zu löschen.");
        }

        _db.CompanyAnnouncements.Remove(item);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(announcementId);
    }

    private static Dictionary<string, string> Validate(AnnouncementInput input)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(input.Title))
        {
            errors["Title"] = "Bitte geben Sie einen Titel an.";
        }
        else if (input.Title.Length > 200)
        {
            errors["Title"] = "Der Titel darf höchstens 200 Zeichen enthalten.";
        }

        if (string.IsNullOrWhiteSpace(input.Body))
        {
            errors["Body"] = "Bitte geben Sie einen Text an.";
        }

        if (input.ExpiresAt.HasValue && TimeUtil.ToUtc(input.ExpiresAt.Value) < TimeUtil.ToUtc(input.PublishAt))
        {
            errors["ExpiresAt"] = "Der Ablauf darf nicht vor der Veröffentlichung liegen.";
        }

        return errors;
    }
}
