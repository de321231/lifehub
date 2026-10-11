using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>
/// Zentrale Berechtigungsprüfung für die Mandantenstruktur. Jede Abfrage nach
/// gemeinsamen Inhalten läuft über diese Klasse, damit organisationsübergreifender
/// Zugriff nicht versehentlich möglich wird.
/// </summary>
public interface IOrganizationAccessService
{
    /// <summary>Alle Organisationen (inkl. persönlicher), in denen der Benutzer aktives Mitglied ist.</summary>
    Task<IReadOnlyList<Guid>> GetOrganizationIdsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Organisationen für die Bereichsauswahl (ohne persönliche Bereiche).</summary>
    Task<IReadOnlyList<Organization>> GetSelectableOrganizationsAsync(Guid userId, CancellationToken ct = default);

    Task<bool> IsMemberAsync(Guid userId, Guid organizationId, CancellationToken ct = default);

    Task<OrganizationRole?> GetRoleAsync(Guid userId, Guid organizationId, CancellationToken ct = default);

    /// <summary>Darf der Benutzer gemeinsame Inhalte dieser Organisation verwalten (Manager/Owner)?</summary>
    Task<bool> CanManageOrganizationAsync(Guid userId, Guid organizationId, CancellationToken ct = default);

    /// <summary>Liste der Organisationen, deren Inhalte für den Benutzer sichtbar sind.</summary>
    Task<HashSet<Guid>> GetVisibleOrganizationIdsAsync(Guid userId, CancellationToken ct = default);
}

public sealed class OrganizationAccessService : IOrganizationAccessService
{
    private readonly AppDbContext _db;

    public OrganizationAccessService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> GetOrganizationIdsAsync(Guid userId, CancellationToken ct = default)
        => await _db.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Organization>> GetSelectableOrganizationsAsync(Guid userId, CancellationToken ct = default)
        => await _db.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.IsActive && !m.Organization!.IsPersonal)
            .OrderBy(m => m.Organization!.Name)
            .Select(m => m.Organization!)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<bool> IsMemberAsync(Guid userId, Guid organizationId, CancellationToken ct = default)
        => await _db.OrganizationMembers
            .AsNoTracking()
            .AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId && m.IsActive, ct)
            .ConfigureAwait(false);

    public async Task<OrganizationRole?> GetRoleAsync(Guid userId, Guid organizationId, CancellationToken ct = default)
    {
        var role = await _db.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.OrganizationId == organizationId)
            .Select(m => (OrganizationRole?)m.Role)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return role;
    }

    public async Task<bool> CanManageOrganizationAsync(Guid userId, Guid organizationId, CancellationToken ct = default)
    {
        var role = await GetRoleAsync(userId, organizationId, ct).ConfigureAwait(false);
        return role is OrganizationRole.Manager or OrganizationRole.Owner;
    }

    public async Task<HashSet<Guid>> GetVisibleOrganizationIdsAsync(Guid userId, CancellationToken ct = default)
    {
        var ids = await GetOrganizationIdsAsync(userId, ct).ConfigureAwait(false);
        return new HashSet<Guid>(ids);
    }
}

/// <summary>Hilfsmittel für die Sichtbarkeitsregeln gemeinsamer Inhalte.</summary>
public static class VisibilityRules
{
    /// <summary>Privat: keine Organisation, Eigentümer.</summary>
    public static bool IsPrivate(IEnumerable<Guid> visibleOrgIds, Guid? organizationId, Guid ownerUserId, Guid currentUserId)
        => organizationId is null && ownerUserId == currentUserId;

    /// <summary>Sichtbar, wenn privat gehörig oder Mitglied der zugeordneten Organisation.</summary>
    public static bool CanView(IReadOnlySet<Guid> visibleOrgIds, Guid? organizationId, Guid ownerUserId, Guid currentUserId)
        => (organizationId is null && ownerUserId == currentUserId)
           || (organizationId is not null && visibleOrgIds.Contains(organizationId.Value));
}

/// <summary>Zeit-Hilfsfunktionen; alle Speicherwerte sind UTC.</summary>
public static class TimeUtil
{
    public static DateTimeOffset ToUtc(DateTimeOffset value) => value.ToUniversalTime();

    public static DateTimeOffset? ToUtc(DateTimeOffset? value) => value?.ToUniversalTime();

    /// <summary>Normalisiert eine komma-getrennte Tag-Liste (Kleinschreibung, dedupliziert, sortiert).</summary>
    public static string? NormalizeTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return null;
        }

        var parts = tags
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .Take(20)
            .ToList();

        return parts.Count == 0 ? null : string.Join(',', parts);
    }
}
