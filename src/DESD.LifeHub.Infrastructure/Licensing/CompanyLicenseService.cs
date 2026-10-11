using DESD.LifeHub.Core.Abstractions;
using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Data;
using DESD.LifeHub.Infrastructure.Options;
using DESD.LifeHub.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DESD.LifeHub.Infrastructure.Licensing;

// ---------------------------------------------------------------- Ergebnis-/Anzeigetypen

/// <summary>Ergebnis der Erzeugung eines Firmen-Kontingents.</summary>
public sealed record CompanyLicenseGenerationResult
{
    public required int Requested { get; init; }
    public required int Created { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Gesetzt, wenn das Kontingent gar nicht erzeugt werden konnte (z. B. Admin-API aus).</summary>
    public string? FatalError { get; init; }

    public bool Succeeded => FatalError is null && Created > 0;
}

/// <summary>Zeile des Firmen-Lizenzpools. Enthält niemals den Rohschlüssel.</summary>
public sealed record CompanyLicenseView
{
    public required Guid Id { get; init; }
    public required string Label { get; init; }
    public string? MaskedKey { get; init; }
    public string? PolicyName { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public int? MaxActivations { get; init; }
    public bool Revoked { get; init; }
    public Guid? AssignedToUserId { get; init; }
    public string? AssignedToName { get; init; }
    public string? AssignedToEmail { get; init; }
    public DateTimeOffset? AssignedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Zählwerte des Firmenpools.</summary>
public sealed record CompanyPoolSummary
{
    public required Guid OrganizationId { get; init; }
    public required int Total { get; init; }
    public required int Assigned { get; init; }
    public required int Free { get; init; }
    public required int Revoked { get; init; }
    public bool AdminApiConfigured { get; init; }
}

/// <summary>Mitarbeiterzeile der Firmenverwaltung inklusive Lizenzstatus.</summary>
public sealed record CompanyEmployeeRow
{
    public required Guid UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public OrganizationRole Role { get; init; }
    public bool IsActive { get; init; }
    public bool HasValidLicense { get; init; }
    public LicenseStatus LicenseStatus { get; init; }
    public string? MaskedKey { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool FromCompanyPool { get; init; }
}

/// <summary>Übersichtszeile für die Systemverwaltung (Firma + Lizenzkennzahlen).</summary>
public sealed record CompanyOverviewRow
{
    public required Guid OrganizationId { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public Guid OwnerUserId { get; init; }
    public int MemberCount { get; init; }
    public int LicensedUsers { get; init; }
    public int PoolTotal { get; init; }
    public int PoolAssigned { get; init; }
    public int PoolFree { get; init; }
}

/// <summary>
/// Verwaltung der Firmen-Kontingente. Erzeugt Schlüssel über die OpenLicensd-Admin-API,
/// legt sie verschlüsselt ab und führt die Zuordnung zu Mitarbeiterkonten über den
/// zentralen <see cref="LicenseService"/> durch (dortige Prüf- und Kulanzlogik).
/// </summary>
public interface ICompanyLicenseService
{
    Task<CompanyLicenseGenerationResult> GenerateAsync(
        Guid organizationId,
        int count,
        Guid actorUserId,
        string? labelPrefix = null,
        DateTimeOffset? expiresAt = null,
        int? maxActivations = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanyLicenseView>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task<CompanyPoolSummary> GetPoolSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanyEmployeeRow>> GetEmployeesAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanyOverviewRow>> GetOperatorOverviewAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult> AssignAsync(
        Guid organizationId,
        Guid companyLicenseId,
        Guid userId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> ReleaseAsync(
        Guid organizationId,
        Guid companyLicenseId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> RevokeAsync(
        Guid organizationId,
        Guid companyLicenseId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}

public sealed class CompanyLicenseService : ICompanyLicenseService
{
    private const int MaxBatch = 500;

    private readonly AppDbContext _db;
    private readonly LicenseService _licenses;
    private readonly ILicenseKeyProtector _protector;
    private readonly IOpenLicensdAdmin _admin;
    private readonly IClock _clock;
    private readonly OpenLicensdOptions _options;
    private readonly IAuditService _audit;
    private readonly ILogger<CompanyLicenseService> _logger;

    public CompanyLicenseService(
        AppDbContext db,
        LicenseService licenses,
        ILicenseKeyProtector protector,
        IOpenLicensdAdmin admin,
        IClock clock,
        IOptions<OpenLicensdOptions> options,
        IAuditService audit,
        ILogger<CompanyLicenseService> logger)
    {
        _db = db;
        _licenses = licenses;
        _protector = protector;
        _admin = admin;
        _clock = clock;
        _options = options.Value;
        _audit = audit;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Erzeugung

    public async Task<CompanyLicenseGenerationResult> GenerateAsync(
        Guid organizationId,
        int count,
        Guid actorUserId,
        string? labelPrefix = null,
        DateTimeOffset? expiresAt = null,
        int? maxActivations = null,
        CancellationToken cancellationToken = default)
    {
        if (count < 1 || count > MaxBatch)
        {
            return new CompanyLicenseGenerationResult
            {
                Requested = count,
                Created = 0,
                FatalError = $"Bitte zwischen 1 und {MaxBatch} Lizenzen erzeugen.",
            };
        }

        if (!_admin.IsConfigured)
        {
            return new CompanyLicenseGenerationResult
            {
                Requested = count,
                Created = 0,
                FatalError = "Die OpenLicensd-Admin-API ist nicht konfiguriert. Ohne API-Token können keine Firmenlizenzen erzeugt werden.",
            };
        }

        var organization = await _db.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (organization is null)
        {
            return new CompanyLicenseGenerationResult
            {
                Requested = count,
                Created = 0,
                FatalError = "Organisation nicht gefunden.",
            };
        }

        // Produkt-ID auflösen (konfiguriert oder per API).
        var productId = _options.ProductId;
        if (string.IsNullOrWhiteSpace(productId))
        {
            var productResult = await _admin.FindProductByCodeAsync(_options.ProductCode, cancellationToken).ConfigureAwait(false);
            if (!productResult.Succeeded || productResult.Value is null)
            {
                return new CompanyLicenseGenerationResult
                {
                    Requested = count,
                    Created = 0,
                    FatalError = productResult.Error ?? $"Produkt '{_options.ProductCode}' konnte nicht aufgelöst werden.",
                };
            }

            productId = productResult.Value.Id;
        }

        // Richtlinien-ID auflösen.
        var policyId = _options.PolicyId;
        OpenLicensdPolicy? policy = null;
        if (string.IsNullOrWhiteSpace(policyId))
        {
            var policyResult = await _admin.FindPolicyAsync(productId!, cancellationToken).ConfigureAwait(false);
            if (!policyResult.Succeeded || policyResult.Value is null)
            {
                return new CompanyLicenseGenerationResult
                {
                    Requested = count,
                    Created = 0,
                    FatalError = policyResult.Error ?? "Es konnte keine Richtlinie ermittelt werden.",
                };
            }

            policy = policyResult.Value;
            policyId = policy.Id;
        }

        var prefix = string.IsNullOrWhiteSpace(labelPrefix) ? organization.Name : labelPrefix.Trim();
        var limit = maxActivations ?? _options.DefaultMaxActivations;
        var errors = new List<string>();
        var created = 0;

        for (var i = 1; i <= count; i++)
        {
            var label = $"{prefix} #{i:000}";

            var result = await _admin.CreateLicenseAsync(
                new CreateCompanyLicenseInput(label, productId!, policyId!, expiresAt, limit),
                cancellationToken).ConfigureAwait(false);

            if (!result.Succeeded || result.Value is null)
            {
                errors.Add($"„{label}“: {result.Error}");
                if (result.Unreachable)
                {
                    // Bei einem Netzwerkausfall ist ein weiterer Versuch zwecklos.
                    break;
                }

                continue;
            }

            var key = result.Value.Key;
            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add($"„{label}“: OpenLicensd lieferte keinen Schlüssel zurück.");
                continue;
            }

            _db.CompanyLicenses.Add(new CompanyLicense
            {
                OrganizationId = organizationId,
                Label = label,
                OpenLicensdLicenseId = result.Value.Id,
                LicenseKeyCiphertext = _protector.Protect(key),
                LicenseKeyHash = _protector.Hash(key),
                LicenseKeyPrefix = _protector.Mask(key),
                ProductCode = result.Value.ProductCode ?? _options.ProductCode,
                PolicyName = result.Value.PolicyName ?? policy?.Name,
                ExpiresAt = result.Value.ExpiresAt ?? expiresAt,
                MaxActivations = (int?)(result.Value.MaxActivations ?? limit),
                CreatedByUserId = actorUserId,
                CreatedAt = _clock.UtcNow,
            });

            created++;
        }

        if (created > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await _audit.WriteAsync(
            "companylicense.generate",
            actorUserId,
            organizationId,
            entityType: nameof(CompanyLicense),
            details: $"requested={count};created={created};errors={errors.Count}",
            succeeded: created > 0,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Firmenlizenzen erzeugt: Firma {OrganizationId}, angefordert {Requested}, erstellt {Created}.",
            organizationId, count, created);

        return new CompanyLicenseGenerationResult
        {
            Requested = count,
            Created = created,
            Errors = errors,
        };
    }

    // ---------------------------------------------------------------- Abfragen

    public async Task<IReadOnlyList<CompanyLicenseView>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.CompanyLicenses
            .AsNoTracking()
            .Where(l => l.OrganizationId == organizationId)
            .Select(l => new
            {
                l.Id,
                l.Label,
                l.LicenseKeyPrefix,
                l.PolicyName,
                l.ExpiresAt,
                l.MaxActivations,
                l.Revoked,
                l.AssignedToUserId,
                l.AssignedAt,
                l.CreatedAt,
                AssignedName = l.AssignedToUser != null ? l.AssignedToUser.DisplayName : null,
                AssignedEmail = l.AssignedToUser != null ? l.AssignedToUser.Email : null,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Sortierung erst im Speicher: DateTimeOffset ist nicht bei allen Providern
        // (z. B. SQLite) in ORDER BY übersetzbar.
        return rows
            .OrderBy(l => l.CreatedAt)
            .Select(l => new CompanyLicenseView
            {
                Id = l.Id,
                Label = l.Label,
                MaskedKey = l.LicenseKeyPrefix,
                PolicyName = l.PolicyName,
                ExpiresAt = l.ExpiresAt,
                MaxActivations = l.MaxActivations,
                Revoked = l.Revoked,
                AssignedToUserId = l.AssignedToUserId,
                AssignedToName = l.AssignedName,
                AssignedToEmail = l.AssignedEmail,
                AssignedAt = l.AssignedAt,
                CreatedAt = l.CreatedAt,
            }).ToList();
    }

    public async Task<CompanyPoolSummary> GetPoolSummaryAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var counts = await _db.CompanyLicenses
            .AsNoTracking()
            .Where(l => l.OrganizationId == organizationId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Assigned = g.Count(l => l.AssignedToUserId != null),
                Revoked = g.Count(l => l.Revoked),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var total = counts?.Total ?? 0;
        var assigned = counts?.Assigned ?? 0;
        var revoked = counts?.Revoked ?? 0;

        return new CompanyPoolSummary
        {
            OrganizationId = organizationId,
            Total = total,
            Assigned = assigned,
            Free = Math.Max(0, total - assigned - revoked),
            Revoked = revoked,
            AdminApiConfigured = _admin.IsConfigured,
        };
    }

    public async Task<IReadOnlyList<CompanyEmployeeRow>> GetEmployeesAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var members = await _db.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
            .Join(
                _db.Users.AsNoTracking(),
                m => m.UserId,
                u => u.Id,
                (m, u) => new
                {
                    u.Id,
                    u.DisplayName,
                    u.Email,
                    u.IsActive,
                    m.Role,
                })
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var userIds = members.Select(m => m.Id).ToList();

        var licenses = await _db.UserLicenses
            .AsNoTracking()
            .Where(l => userIds.Contains(l.UserId))
            .Select(l => new { l.UserId, l.Status, l.LicenseKeyPrefix, l.ExpiresAt })
            .ToDictionaryAsync(l => l.UserId, cancellationToken)
            .ConfigureAwait(false);

        // Schlüssel aus dem Firmenpool, um die Herkunft anzuzeigen.
        var poolAssigned = await _db.CompanyLicenses
            .AsNoTracking()
            .Where(l => l.OrganizationId == organizationId && l.AssignedToUserId != null)
            .Select(l => l.AssignedToUserId!.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var poolSet = poolAssigned.ToHashSet();

        return members.Select(m =>
        {
            licenses.TryGetValue(m.Id, out var license);
            return new CompanyEmployeeRow
            {
                UserId = m.Id,
                DisplayName = string.IsNullOrWhiteSpace(m.DisplayName) ? (m.Email ?? "–") : m.DisplayName,
                Email = m.Email ?? "–",
                Role = m.Role,
                IsActive = m.IsActive,
                HasValidLicense = license?.Status == LicenseStatus.Valid,
                LicenseStatus = license?.Status ?? LicenseStatus.None,
                MaskedKey = license?.LicenseKeyPrefix,
                ExpiresAt = license?.ExpiresAt,
                FromCompanyPool = poolSet.Contains(m.Id),
            };
        }).ToList();
    }

    public async Task<IReadOnlyList<CompanyOverviewRow>> GetOperatorOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        var organizations = await _db.Organizations
            .AsNoTracking()
            .Where(o => !o.IsPersonal)
            .OrderBy(o => o.Name)
            .Select(o => new { o.Id, o.Name, o.Slug, o.OwnerUserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var orgIds = organizations.Select(o => o.Id).ToList();

        var memberRows = await _db.OrganizationMembers
            .AsNoTracking()
            .Where(m => orgIds.Contains(m.OrganizationId))
            .Select(m => new { m.OrganizationId, m.UserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var memberUserIds = memberRows.Select(m => m.UserId).Distinct().ToList();

        var validUserIds = await _db.UserLicenses
            .AsNoTracking()
            .Where(l => memberUserIds.Contains(l.UserId) && l.Status == LicenseStatus.Valid)
            .Select(l => l.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var validSet = validUserIds.ToHashSet();

        var poolRows = await _db.CompanyLicenses
            .AsNoTracking()
            .Where(l => orgIds.Contains(l.OrganizationId))
            .Select(l => new { l.OrganizationId, l.AssignedToUserId, l.Revoked })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return organizations.Select(o =>
        {
            var membersOfOrg = memberRows.Where(m => m.OrganizationId == o.Id).ToList();
            var pool = poolRows.Where(p => p.OrganizationId == o.Id).ToList();

            return new CompanyOverviewRow
            {
                OrganizationId = o.Id,
                Name = o.Name,
                Slug = o.Slug,
                OwnerUserId = o.OwnerUserId,
                MemberCount = membersOfOrg.Count,
                LicensedUsers = membersOfOrg.Count(m => validSet.Contains(m.UserId)),
                PoolTotal = pool.Count,
                PoolAssigned = pool.Count(p => p.AssignedToUserId != null),
                PoolFree = pool.Count(p => p.AssignedToUserId is null && !p.Revoked),
            };
        }).ToList();
    }

    // ---------------------------------------------------------------- Zuordnung

    public async Task<ServiceResult> AssignAsync(
        Guid organizationId,
        Guid companyLicenseId,
        Guid userId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var poolLicense = await _db.CompanyLicenses
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == companyLicenseId && l.OrganizationId == organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (poolLicense is null)
        {
            return ServiceResult.Fail("Lizenz nicht im Pool dieser Firma gefunden.");
        }

        if (poolLicense.Revoked)
        {
            return ServiceResult.Fail("Diese Lizenz ist gesperrt und kann nicht zugewiesen werden.");
        }

        if (poolLicense.AssignedToUserId.HasValue)
        {
            return ServiceResult.Fail("Diese Lizenz ist bereits einem Mitarbeiter zugewiesen.");
        }

        // Mitarbeiter muss Mitglied der Firma sein.
        var isMember = await _db.OrganizationMembers
            .AsNoTracking()
            .AnyAsync(m => m.OrganizationId == organizationId && m.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (!isMember)
        {
            return ServiceResult.Fail("Der ausgewählte Benutzer ist kein Mitarbeiter dieser Firma.");
        }

        string plaintext;
        try
        {
            plaintext = _protector.Unprotect(poolLicense.LicenseKeyCiphertext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firmenlizenz {LicenseId} konnte nicht entschlüsselt werden.", companyLicenseId);
            return ServiceResult.Fail("Der Lizenzschlüssel konnte nicht gelesen werden. Bitte neu erzeugen.");
        }

        var hash = _protector.Hash(plaintext);

        // Lizenz atomar beanspruchen, damit zwei Zuweisungen nicht kollidieren.
        var now = _clock.UtcNow;
        var claimed = await _db.CompanyLicenses
            .Where(l => l.Id == companyLicenseId && l.AssignedToUserId == null && !l.Revoked)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(l => l.AssignedToUserId, userId)
                    .SetProperty(l => l.AssignedAt, now),
                cancellationToken)
            .ConfigureAwait(false);

        if (claimed == 0)
        {
            return ServiceResult.Fail("Diese Lizenz wurde soeben bereits zugewiesen.");
        }

        // Über den zentralen Service zuweisen – dortige Prüf-, Eindeutigkeits- und Kulanzlogik.
        var activation = await _licenses
            .AssignAsync(userId, plaintext, organizationId, ipAddress: null, cancellationToken)
            .ConfigureAwait(false);

        var userHasKey = await _db.UserLicenses
            .AsNoTracking()
            .AnyAsync(l => l.UserId == userId && l.LicenseKeyHash == hash, cancellationToken)
            .ConfigureAwait(false);

        if (!userHasKey)
        {
            // beanspruchten Zustand zurücknehmen
            await _db.CompanyLicenses
                .Where(l => l.Id == companyLicenseId)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(l => l.AssignedToUserId, (Guid?)null)
                        .SetProperty(l => l.AssignedAt, (DateTimeOffset?)null),
                    cancellationToken)
                .ConfigureAwait(false);

            return ServiceResult.Fail(activation.Message);
        }

        await _audit.WriteAsync(
            "companylicense.assign",
            actorUserId,
            organizationId,
            entityType: nameof(CompanyLicense),
            entityId: companyLicenseId.ToString(),
            details: $"user={userId};status={activation.Status}",
            succeeded: activation.Succeeded,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return activation.Succeeded
            ? ServiceResult.Ok()
            : ServiceResult.Fail(activation.Message);
    }

    public async Task<ServiceResult> ReleaseAsync(
        Guid organizationId,
        Guid companyLicenseId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var poolLicense = await _db.CompanyLicenses
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == companyLicenseId && l.OrganizationId == organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (poolLicense is null)
        {
            return ServiceResult.Fail("Lizenz nicht im Pool dieser Firma gefunden.");
        }

        if (!poolLicense.AssignedToUserId.HasValue)
        {
            return ServiceResult.Fail("Diese Lizenz ist derzeit nicht zugewiesen.");
        }

        var assignedUserId = poolLicense.AssignedToUserId.Value;

        // Nur die aus dem Pool stammende Zuordnung entfernen.
        await _licenses
            .RemoveAsync(assignedUserId, actorUserId, asAdmin: true, ipAddress: null, cancellationToken)
            .ConfigureAwait(false);

        await _db.CompanyLicenses
            .Where(l => l.Id == companyLicenseId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(l => l.AssignedToUserId, (Guid?)null)
                    .SetProperty(l => l.AssignedAt, (DateTimeOffset?)null),
                cancellationToken)
            .ConfigureAwait(false);

        await _audit.WriteAsync(
            "companylicense.release",
            actorUserId,
            organizationId,
            entityType: nameof(CompanyLicense),
            entityId: companyLicenseId.ToString(),
            details: $"freigegeben von user={assignedUserId}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RevokeAsync(
        Guid organizationId,
        Guid companyLicenseId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var poolLicense = await _db.CompanyLicenses
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == companyLicenseId && l.OrganizationId == organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (poolLicense is null)
        {
            return ServiceResult.Fail("Lizenz nicht im Pool dieser Firma gefunden.");
        }

        if (poolLicense.Revoked)
        {
            return ServiceResult.Fail("Diese Lizenz ist bereits gesperrt.");
        }

        // Bei zugewiesener Lizenz: Zuordnung beim Mitarbeiter entfernen.
        if (poolLicense.AssignedToUserId.HasValue)
        {
            await _licenses
                .RemoveAsync(poolLicense.AssignedToUserId.Value, actorUserId, asAdmin: true, ipAddress: null, cancellationToken)
                .ConfigureAwait(false);
        }

        var remoteError = (string?)null;
        if (!string.IsNullOrWhiteSpace(poolLicense.OpenLicensdLicenseId))
        {
            var remote = await _admin
                .RevokeLicenseAsync(poolLicense.OpenLicensdLicenseId!, cancellationToken)
                .ConfigureAwait(false);

            if (!remote.Succeeded)
            {
                remoteError = remote.Error;
            }
        }

        await _db.CompanyLicenses
            .Where(l => l.Id == companyLicenseId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(l => l.Revoked, true)
                    .SetProperty(l => l.AssignedToUserId, (Guid?)null)
                    .SetProperty(l => l.AssignedAt, (DateTimeOffset?)null),
                cancellationToken)
            .ConfigureAwait(false);

        await _audit.WriteAsync(
            "companylicense.revoke",
            actorUserId,
            organizationId,
            entityType: nameof(CompanyLicense),
            entityId: companyLicenseId.ToString(),
            details: remoteError is null ? "gesperrt" : $"lokal gesperrt; remote_fehler={remoteError}",
            succeeded: remoteError is null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return remoteError is null
            ? ServiceResult.Ok()
            : ServiceResult.Fail($"Lizenz wurde lokal gesperrt, konnte in OpenLicensd aber nicht gesperrt werden: {remoteError}");
    }
}
