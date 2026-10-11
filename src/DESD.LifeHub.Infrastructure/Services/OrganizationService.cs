using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DESD.LifeHub.Infrastructure.Services;

public sealed record OrganizationSummary
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public bool IsPersonal { get; init; }
    public OrganizationRole Role { get; init; }
    public int MemberCount { get; init; }
    public bool CanManage { get; init; }
}

public sealed record OrganizationMemberView
{
    public required Guid UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public OrganizationRole Role { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset JoinedAt { get; init; }
    public bool IsSelf { get; init; }
}

public sealed record OrganizationOption
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
}

/// <summary>
/// Verwaltung von Organisationen und Mitgliedschaften. Jede Operation prüft die
/// Mitgliedschaft des Aufrufers; mandantenübergreifender Zugriff ist nicht möglich.
/// </summary>
public interface IOrganizationService
{
    Task<IReadOnlyList<OrganizationSummary>> GetForUserAsync(Guid userId, CancellationToken ct = default);

    Task<OrganizationSummary?> GetSummaryAsync(Guid userId, Guid organizationId, CancellationToken ct = default);

    Task<IReadOnlyList<OrganizationMemberView>> GetMembersAsync(Guid userId, Guid organizationId, CancellationToken ct = default);

    Task<ServiceResult> CreateAsync(Guid userId, string name, CancellationToken ct = default);

    Task<ServiceResult> RenameAsync(Guid userId, Guid organizationId, string name, CancellationToken ct = default);

    Task<ServiceResult> AddMemberByEmailAsync(Guid actorUserId, Guid organizationId, string email, OrganizationRole role, CancellationToken ct = default);

    Task<ServiceResult> ChangeRoleAsync(Guid actorUserId, Guid organizationId, Guid memberUserId, OrganizationRole role, CancellationToken ct = default);

    Task<ServiceResult> RemoveMemberAsync(Guid actorUserId, Guid organizationId, Guid memberUserId, CancellationToken ct = default);

    /// <summary>Legt den persönlichen Bereich an, falls er fehlt. Wird bei Registrierung/Bootstrap genutzt.</summary>
    Task<Guid> EnsurePersonalOrganizationAsync(Guid userId, string displayName, string? email, CancellationToken ct = default);

    /// <summary>
    /// Alle Nicht-Personal-Organisationen für die Einladungsauswahl (Administration).
    /// Enthält bewusst keine Rollen oder Mitgliederzahlen.
    /// </summary>
    Task<IReadOnlyList<OrganizationOption>> ListInvitationOptionsAsync(CancellationToken ct = default);
}

public sealed class OrganizationService : IOrganizationService
{
    private const string PersonalSuffix = "persoenlich";

    private readonly AppDbContext _db;
    private readonly IOrganizationAccessService _access;

    public OrganizationService(AppDbContext db, IOrganizationAccessService access)
    {
        _db = db;
        _access = access;
    }

    public async Task<IReadOnlyList<OrganizationSummary>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var memberships = await _db.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.IsActive)
            .Select(m => new
            {
                m.OrganizationId,
                m.Role,
                m.Organization!.Name,
                m.Organization.Slug,
                m.Organization.IsPersonal,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var ids = memberships.Select(m => m.OrganizationId).ToList();

        var counts = await _db.OrganizationMembers
            .AsNoTracking()
            .Where(m => ids.Contains(m.OrganizationId) && m.IsActive)
            .GroupBy(m => m.OrganizationId)
            .Select(g => new { OrganizationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.OrganizationId, g => g.Count, ct)
            .ConfigureAwait(false);

        return memberships
            .Select(m => new OrganizationSummary
            {
                Id = m.OrganizationId,
                Name = m.Name,
                Slug = m.Slug,
                IsPersonal = m.IsPersonal,
                Role = m.Role,
                MemberCount = counts.TryGetValue(m.OrganizationId, out var c) ? c : 0,
                CanManage = m.Role is OrganizationRole.Manager or OrganizationRole.Owner,
            })
            .OrderByDescending(o => o.Role)
            .ThenBy(o => o.Name)
            .ToList();
    }

    public async Task<OrganizationSummary?> GetSummaryAsync(Guid userId, Guid organizationId, CancellationToken ct = default)
    {
        if (!await _access.IsMemberAsync(userId, organizationId, ct).ConfigureAwait(false))
        {
            return null;
        }

        var role = await _access.GetRoleAsync(userId, organizationId, ct).ConfigureAwait(false) ?? OrganizationRole.Member;

        var org = await _db.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct)
            .ConfigureAwait(false);

        if (org is null)
        {
            return null;
        }

        var memberCount = await _db.OrganizationMembers
            .AsNoTracking()
            .CountAsync(m => m.OrganizationId == organizationId && m.IsActive, ct)
            .ConfigureAwait(false);

        return new OrganizationSummary
        {
            Id = org.Id,
            Name = org.Name,
            Slug = org.Slug,
            IsPersonal = org.IsPersonal,
            Role = role,
            MemberCount = memberCount,
            CanManage = role is OrganizationRole.Manager or OrganizationRole.Owner,
        };
    }

    public async Task<IReadOnlyList<OrganizationMemberView>> GetMembersAsync(Guid userId, Guid organizationId, CancellationToken ct = default)
    {
        if (!await _access.IsMemberAsync(userId, organizationId, ct).ConfigureAwait(false))
        {
            return [];
        }

        return await _db.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
            .OrderByDescending(m => m.Role)
            .ThenBy(m => m.User!.DisplayName)
            .Select(m => new OrganizationMemberView
            {
                UserId = m.UserId,
                DisplayName = m.User!.DisplayName == "" ? m.User.Email! : m.User.DisplayName,
                Email = m.User.Email ?? "–",
                Role = m.Role,
                IsActive = m.IsActive && m.User.IsActive,
                JoinedAt = m.JoinedAt,
                IsSelf = m.UserId == userId,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ServiceResult> CreateAsync(Guid userId, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceResult.Fail("Bitte geben Sie einen Namen für die Organisation an.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length > 160)
        {
            return ServiceResult.Fail("Der Name ist zu lang (maximal 160 Zeichen).");
        }

        var org = new Organization
        {
            Name = trimmed,
            Slug = await GenerateUniqueSlugAsync(trimmed, ct).ConfigureAwait(false),
            IsPersonal = false,
            OwnerUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Organizations.Add(org);
        _db.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = org.Id,
            UserId = userId,
            Role = OrganizationRole.Owner,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(org.Id);
    }

    public async Task<ServiceResult> RenameAsync(Guid userId, Guid organizationId, string name, CancellationToken ct = default)
    {
        if (!await _access.CanManageOrganizationAsync(userId, organizationId, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diese Organisation zu ändern.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceResult.Fail("Bitte geben Sie einen Namen an.");
        }

        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId, ct).ConfigureAwait(false);
        if (org is null)
        {
            return ServiceResult.Fail("Die Organisation wurde nicht gefunden.");
        }

        org.Name = name.Trim();
        org.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(org.Id);
    }

    public async Task<ServiceResult> AddMemberByEmailAsync(
        Guid actorUserId,
        Guid organizationId,
        string email,
        OrganizationRole role,
        CancellationToken ct = default)
    {
        if (!await _access.CanManageOrganizationAsync(actorUserId, organizationId, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, Mitglieder hinzuzufügen.");
        }

        var org = await _db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == organizationId, ct).ConfigureAwait(false);
        if (org is null)
        {
            return ServiceResult.Fail("Die Organisation wurde nicht gefunden.");
        }

        if (org.IsPersonal)
        {
            return ServiceResult.Fail("Persönliche Bereiche können keine weiteren Mitglieder erhalten.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return ServiceResult.Fail("Bitte geben Sie eine E-Mail-Adresse an.");
        }

        var normalized = email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized.ToUpperInvariant(), ct).ConfigureAwait(false);

        if (user is null)
        {
            return ServiceResult.Fail(
                "Es existiert kein Benutzerkonto mit dieser E-Mail-Adresse. " +
                "Legen Sie zuerst eine Einladung an.");
        }

        var existing = await _db.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == user.Id, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            if (existing.IsActive)
            {
                return ServiceResult.Fail("Diese Person ist bereits Mitglied der Organisation.");
            }

            existing.IsActive = true;
            existing.Role = role;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return ServiceResult.Ok(existing.Id);
        }

        _db.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organizationId,
            UserId = user.Id,
            Role = role,
            JoinedAt = DateTimeOffset.UtcNow,
            InvitedByUserId = actorUserId,
        });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ChangeRoleAsync(
        Guid actorUserId,
        Guid organizationId,
        Guid memberUserId,
        OrganizationRole role,
        CancellationToken ct = default)
    {
        var actorRole = await _access.GetRoleAsync(actorUserId, organizationId, ct).ConfigureAwait(false);
        if (actorRole is not OrganizationRole.Owner)
        {
            return ServiceResult.Fail("Nur der Eigentümer der Organisation darf Rollen ändern.");
        }

        var membership = await _db.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == memberUserId, ct)
            .ConfigureAwait(false);

        if (membership is null)
        {
            return ServiceResult.Fail("Die Mitgliedschaft wurde nicht gefunden.");
        }

        if (membership.Role == OrganizationRole.Owner && role != OrganizationRole.Owner)
        {
            var ownerCount = await _db.OrganizationMembers
                .CountAsync(m => m.OrganizationId == organizationId && m.Role == OrganizationRole.Owner && m.IsActive, ct)
                .ConfigureAwait(false);

            if (ownerCount <= 1)
            {
                return ServiceResult.Fail("Die Organisation benötigt mindestens einen Eigentümer.");
            }
        }

        membership.Role = role;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(membership.Id);
    }

    public async Task<ServiceResult> RemoveMemberAsync(
        Guid actorUserId,
        Guid organizationId,
        Guid memberUserId,
        CancellationToken ct = default)
    {
        var actorRole = await _access.GetRoleAsync(actorUserId, organizationId, ct).ConfigureAwait(false);

        var isSelf = actorUserId == memberUserId;
        if (actorRole is not OrganizationRole.Owner && !isSelf)
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, dieses Mitglied zu entfernen.");
        }

        var membership = await _db.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == memberUserId, ct)
            .ConfigureAwait(false);

        if (membership is null)
        {
            return ServiceResult.Fail("Die Mitgliedschaft wurde nicht gefunden.");
        }

        if (membership.Role == OrganizationRole.Owner)
        {
            var ownerCount = await _db.OrganizationMembers
                .CountAsync(m => m.OrganizationId == organizationId && m.Role == OrganizationRole.Owner && m.IsActive, ct)
                .ConfigureAwait(false);

            if (ownerCount <= 1)
            {
                return ServiceResult.Fail("Der letzte Eigentümer kann die Organisation nicht verlassen.");
            }
        }

        membership.IsActive = false;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(membership.Id);
    }

    public async Task<Guid> EnsurePersonalOrganizationAsync(
        Guid userId,
        string displayName,
        string? email,
        CancellationToken ct = default)
    {
        var existing = await _db.Organizations
            .FirstOrDefaultAsync(o => o.OwnerUserId == userId && o.IsPersonal, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.Id;
        }

        var label = string.IsNullOrWhiteSpace(displayName) ? (email ?? "Benutzer") : displayName;
        var org = new Organization
        {
            Name = $"Persönlich – {label}",
            Slug = await GenerateUniqueSlugAsync($"{Slugify(label)}-{PersonalSuffix}", ct).ConfigureAwait(false),
            IsPersonal = true,
            OwnerUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Organizations.Add(org);
        _db.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = org.Id,
            UserId = userId,
            Role = OrganizationRole.Owner,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is not null)
        {
            user.PersonalOrganizationId = org.Id;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return org.Id;
    }

    public async Task<IReadOnlyList<OrganizationOption>> ListInvitationOptionsAsync(CancellationToken ct = default)
        => await _db.Organizations
            .AsNoTracking()
            .Where(o => !o.IsPersonal)
            .OrderBy(o => o.Name)
            .Select(o => new OrganizationOption { Id = o.Id, Name = o.Name })
            .Take(500)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    private static string Slugify(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-') is { Length: > 0 } trimmed ? trimmed[..Math.Min(trimmed.Length, 60)] : "organisation";
    }

    private async Task<string> GenerateUniqueSlugAsync(string name, CancellationToken ct)
    {
        var baseSlug = Slugify(name);
        var candidate = baseSlug;
        var counter = 1;

        while (await _db.Organizations.AnyAsync(o => o.Slug == candidate, ct).ConfigureAwait(false))
        {
            counter++;
            var suffix = $"-{counter}";
            candidate = baseSlug[..Math.Min(baseSlug.Length, 80 - suffix.Length)] + suffix;
        }

        return candidate;
    }
}
