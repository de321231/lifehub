using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>Verwaltung von Workspaces (persönlich und Unternehmen).</summary>
public interface IWorkspaceService
{
    /// <summary>Alle Workspaces des Benutzers.</summary>
    Task<IReadOnlyList<Workspace>> GetForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Der persönliche Workspace (erstellt ihn bei Bedarf).</summary>
    Task<Workspace> GetOrCreatePersonalAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Workspace nach Company-ID oder Id.</summary>
    Task<Workspace?> GetByIdOrSlugAsync(Guid? workspaceId, string? companySlug, CancellationToken ct = default);

    /// <summary>Aktiviere einen Workspace für die aktuelle Sitzung des Benutzers.</summary>
    Task<ServiceResult> AddMembershipAsync(Guid userId, Guid workspaceId, CancellationToken ct = default);

    /// <summary>Erstelle einen neuen Firmen-Workspace.</summary>
    Task<ServiceResult> CreateCompanyAsync(Guid userId, string name, string companySlug, CancellationToken ct = default);

    /// <summary>Aktiviere/deaktiviere einen Workspace.</summary>
    Task<ServiceResult> SetStatusAsync(Guid actorUserId, Guid workspaceId, bool isActive, CancellationToken ct = default);
}

public sealed class WorkspaceService : IWorkspaceService
{
    private readonly AppDbContext _db;

    public WorkspaceService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Workspace>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        return await _db.WorkspaceMemberships
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.IsActive)
            .Select(m => m.Workspace!)
            .Where(w => w.IsActive)
            .OrderBy(w => w.Type)
            .ThenBy(w => w.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Workspace> GetOrCreatePersonalAsync(Guid userId, CancellationToken ct = default)
    {
        var workspace = await _db.Workspaces
            .FirstOrDefaultAsync(w => w.OwnerUserId == userId && w.Type == WorkspaceType.Personal && w.IsActive, ct)
            .ConfigureAwait(false);

        if (workspace is not null)
        {
            return workspace;
        }

        workspace = new Workspace
        {
            Name = "Persönlich",
            Type = WorkspaceType.Personal,
            OwnerUserId = userId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            WorkspaceId = workspace.Id,
            UserId = userId,
            IsActive = true,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return workspace;
    }

    public async Task<Workspace?> GetByIdOrSlugAsync(Guid? workspaceId, string? companySlug, CancellationToken ct = default)
    {
        if (workspaceId.HasValue)
        {
            return await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId.Value && w.IsActive, ct).ConfigureAwait(false);
        }
        if (!string.IsNullOrWhiteSpace(companySlug))
        {
            return await _db.Workspaces.FirstOrDefaultAsync(w => w.CompanyId == companySlug && w.IsActive, ct).ConfigureAwait(false);
        }
        return null;
    }

    public async Task<ServiceResult> AddMembershipAsync(Guid userId, Guid workspaceId, CancellationToken ct = default)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId && w.IsActive, ct).ConfigureAwait(false);
        if (workspace is null)
        {
            return ServiceResult.Fail("Workspace nicht gefunden.");
        }

        var exists = await _db.WorkspaceMemberships.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct).ConfigureAwait(false);
        if (exists)
        {
            return ServiceResult.Ok();
        }

        _db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            WorkspaceId = workspaceId,
            UserId = userId,
            IsActive = true,
            JoinedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> CreateCompanyAsync(Guid userId, string name, string companySlug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(companySlug))
        {
            return ServiceResult.Fail("Name und Firmen-ID sind erforderlich.");
        }

        var slugExists = await _db.Workspaces.AnyAsync(w => w.CompanyId == companySlug, ct).ConfigureAwait(false);
        if (slugExists)
        {
            return ServiceResult.Fail("Die Firmen-ID existiert bereits.");
        }

        var workspace = new Workspace
        {
            Name = name.Trim(),
            CompanyId = companySlug.Trim().ToLowerInvariant(),
            Type = WorkspaceType.Company,
            OwnerUserId = userId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            WorkspaceId = workspace.Id,
            UserId = userId,
            IsActive = true,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(workspace.Id);
    }

    public async Task<ServiceResult> SetStatusAsync(Guid actorUserId, Guid workspaceId, bool isActive, CancellationToken ct = default)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, ct).ConfigureAwait(false);
        if (workspace is null)
        {
            return ServiceResult.Fail("Workspace nicht gefunden.");
        }
        workspace.IsActive = isActive;
        workspace.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(workspaceId);
    }
}
