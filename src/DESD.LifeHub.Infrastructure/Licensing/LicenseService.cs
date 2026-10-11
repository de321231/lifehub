using DESD.LifeHub.Core.Abstractions;
using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Core.Licensing;
using DESD.LifeHub.Infrastructure.Data;
using DESD.LifeHub.Infrastructure.Options;
using DESD.LifeHub.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DESD.LifeHub.Infrastructure.Licensing;

/// <summary>
/// Zentraler Lizenzservice. Kapselt Speicherung, Prüfung und Zugriffsentscheidung
/// vollständig serverseitig. Der Lizenzschlüssel verlässt diese Klasse niemals im
/// Klartext – nach aussen werden ausschliesslich maskierte Anzeigewerte gegeben.
/// </summary>
public sealed class LicenseService : ILicenseAccessService
{
    private readonly AppDbContext _db;
    private readonly ILicenseValidator _validator;
    private readonly ILicenseKeyProtector _protector;
    private readonly IClock _clock;
    private readonly OpenLicensdOptions _options;
    private readonly IAuditService _audit;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LicenseService> _logger;

    /// <summary>Serialisiert Prüfungen pro Benutzer, um parallele Serveraufrufe zu vermeiden.</summary>
    private static readonly SemaphoreSlim ValidationLock = new(1, 1);

    public LicenseService(
        AppDbContext db,
        ILicenseValidator validator,
        ILicenseKeyProtector protector,
        IClock clock,
        IOptions<OpenLicensdOptions> options,
        IAuditService audit,
        IMemoryCache cache,
        ILogger<LicenseService> logger)
    {
        _db = db;
        _validator = validator;
        _protector = protector;
        _clock = clock;
        _options = options.Value;
        _audit = audit;
        _cache = cache;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Aktivierung

    /// <summary>
    /// Weist dem Benutzer einen Lizenzschlüssel zu (oder ersetzt den vorhandenen) und
    /// prüft ihn unmittelbar gegen OpenLicensd.
    ///
    /// Reihenfolge ist bewusst gewählt: Die Eindeutigkeitsprüfung erfolgt <em>vor</em>
    /// dem Serveraufruf, damit eine doppelte Zuweisung keine Aktivierung verbraucht.
    /// </summary>
    public async Task<LicenseActivationResult> AssignAsync(
        Guid userId,
        string plaintextKey,
        Guid? organizationId = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey))
        {
            return LicenseActivationResult.Fail("empty_key", "Bitte geben Sie einen Lizenzschlüssel ein.");
        }

        var normalizedInput = AesLicenseKeyProtector.Normalize(plaintextKey);
        if (normalizedInput.Length < 10)
        {
            return LicenseActivationResult.Fail("key_too_short", "Der Lizenzschlüssel ist unvollständig.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return LicenseActivationResult.Fail("user_not_found", "Benutzerkonto nicht gefunden.");
        }

        if (!user.IsActive)
        {
            return LicenseActivationResult.Fail("user_inactive", "Ihr Konto ist deaktiviert. Bitte wenden Sie sich an einen Administrator.");
        }

        var hash = _protector.Hash(plaintextKey);

        // Ein Schlüssel darf nicht zwei Konten zugewiesen sein.
        var takenByOther = await _db.UserLicenses
            .AsNoTracking()
            .Where(l => l.LicenseKeyHash == hash && l.UserId != userId)
            .Select(l => (Guid?)l.UserId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (takenByOther.HasValue)
        {
            await _audit.WriteAsync("license.assign.rejected", userId, organizationId,
                entityType: nameof(UserLicense), details: "duplicate_key_detected", succeeded: false,
                ipAddress: ipAddress, cancellationToken: cancellationToken).ConfigureAwait(false);

            return LicenseActivationResult.Fail(
                "duplicate_key",
                "Dieser Lizenzschlüssel ist bereits einem anderen Benutzerkonto zugeordnet. " +
                "Eine persönliche Lizenz kann nur ein Konto nutzen.");
        }

        var license = await _db.UserLicenses
            .FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        var isNew = license is null;
        license ??= new UserLicense { UserId = userId };

        license.LicenseKeyCiphertext = _protector.Protect(plaintextKey);
        license.LicenseKeyHash = hash;
        license.LicenseKeyPrefix = _protector.Mask(plaintextKey);
        license.Fingerprint = LicenseFingerprint.ForUser(userId, _options.ProductCode);
        license.OrganizationId = organizationId;
        license.LastAttemptAt = _clock.UtcNow;
        license.ServingFromGrace = false;

        if (isNew)
        {
            license.AssignedAt = _clock.UtcNow;
            _db.UserLicenses.Add(license);
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // Wettlauf zweier paralleler Aktivierungen desselben Schlüssels.
            _logger.LogWarning(ex, "Parallele Lizenzaktivierung erkannt; Eindeutigkeitsindex hat den Konflikt abgefangen.");
            return LicenseActivationResult.Fail(
                "duplicate_key",
                "Dieser Lizenzschlüssel wurde soeben bereits zugeordnet. Bitte prüfen Sie den Lizenzstatus.");
        }

        // Nun serverseitig prüfen. Der Schlüssel liegt nur kurzzeitig im Speicher vor.
        var check = await RunCheckAsync(
            userId,
            plaintextKey,
            license.Fingerprint,
            isActivation: true,
            ipAddress,
            cancellationToken).ConfigureAwait(false);

        ApplyCheckResult(license, check);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync("license.assign", userId, organizationId,
            entityType: nameof(UserLicense), entityId: license.Id.ToString(),
            details: $"status={license.Status};reason={license.LastReason ?? "-"}",
            succeeded: check.Outcome == LicenseCheckOutcome.Valid,
            ipAddress: ipAddress, cancellationToken: cancellationToken).ConfigureAwait(false);

        InvalidateDecisionCache(userId);

        var view = await GetStatusAsync(userId, cancellationToken).ConfigureAwait(false);

        if (check.Outcome == LicenseCheckOutcome.Valid)
        {
            return new LicenseActivationResult
            {
                Succeeded = true,
                Message = "Lizenzschlüssel wurde bestätigt und Ihrem Konto zugeordnet.",
                Status = license.Status,
                View = view,
            };
        }

        return new LicenseActivationResult
        {
            Succeeded = false,
            FailureCode = license.LastReason ?? "not_verified",
            Message = DescribeCheck(check),
            Status = license.Status,
            View = view,
        };
    }

    /// <summary>Prüft die hinterlegte Lizenz erneut (z. B. nach Ablauf der Zwischenspeicherung).</summary>
    public async Task<LicenseActivationResult> RevalidateAsync(
        Guid userId,
        string? ipAddress = null,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var license = await _db.UserLicenses
            .FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (license is null)
        {
            return LicenseActivationResult.Fail("no_license", "Für dieses Konto ist kein Lizenzschlüssel hinterlegt.");
        }

        if (!force && !IsDue(license, _clock.UtcNow))
        {
            var current = await GetStatusAsync(userId, cancellationToken).ConfigureAwait(false);
            return new LicenseActivationResult
            {
                Succeeded = license.Status == LicenseStatus.Valid,
                Message = "Es liegt noch ein aktuelles Prüfergebnis vor (Zwischenspeicherung).",
                Status = license.Status,
                View = current,
            };
        }

        string plaintext;
        try
        {
            plaintext = _protector.Unprotect(license.LicenseKeyCiphertext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gespeicherter Lizenzschlüssel konnte nicht entschlüsselt werden (Benutzer {UserId}).", userId);
            license.Status = LicenseStatus.Malformed;
            license.LastError = "Gespeicherter Lizenzschlüssel ist nicht lesbar.";
            license.LastAttemptAt = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            InvalidateDecisionCache(userId);

            var broken = await GetStatusAsync(userId, cancellationToken).ConfigureAwait(false);
            return new LicenseActivationResult
            {
                Succeeded = false,
                FailureCode = "decrypt_failed",
                Message = "Der hinterlegte Lizenzschlüssel konnte nicht gelesen werden. Bitte erneut eingeben.",
                Status = license.Status,
                View = broken,
            };
        }

        var check = await RunCheckAsync(userId, plaintext, license.Fingerprint, isActivation: false, ipAddress, cancellationToken)
            .ConfigureAwait(false);

        ApplyCheckResult(license, check);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        InvalidateDecisionCache(userId);

        var view = await GetStatusAsync(userId, cancellationToken).ConfigureAwait(false);

        return new LicenseActivationResult
        {
            Succeeded = check.Outcome == LicenseCheckOutcome.Valid,
            FailureCode = check.Outcome == LicenseCheckOutcome.Valid ? null : (license.LastReason ?? check.Outcome.ToString().ToLowerInvariant()),
            Message = DescribeCheck(check),
            Status = license.Status,
            View = view,
        };
    }

    /// <summary>Entfernt die Lizenzzuordnung (Administrator oder Benutzer selbst).</summary>
    public async Task RemoveAsync(Guid userId, Guid? actorUserId, bool asAdmin, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        var license = await _db.UserLicenses
            .FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (license is null)
        {
            return;
        }

        _db.UserLicenses.Remove(license);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        InvalidateDecisionCache(userId);

        await _audit.WriteAsync("license.revoke", actorUserId ?? userId, null,
            entityType: nameof(UserLicense), entityId: license.Id.ToString(),
            details: asAdmin ? "durch Administrator entfernt" : "durch Benutzer entfernt",
            ipAddress: ipAddress, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- Zugriff

    /// <inheritdoc />
    public async Task<LicenseAccessDecision> EvaluateAccessAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var cacheKey = DecisionCacheKey(userId);
        if (_options.DecisionCacheSeconds > 0
            && _cache.TryGetValue(cacheKey, out LicenseAccessDecision? cached)
            && cached is not null)
        {
            return cached;
        }

        var decision = await EvaluateAccessCoreAsync(userId, cancellationToken).ConfigureAwait(false);

        if (_options.DecisionCacheSeconds > 0)
        {
            _cache.Set(cacheKey, decision, TimeSpan.FromSeconds(_options.DecisionCacheSeconds));
        }

        return decision;
    }

    private async Task<LicenseAccessDecision> EvaluateAccessCoreAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.IsActive })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Core.Abstractions.LicenseAccessDecision.Deny(LicenseStatus.None, "Benutzerkonto nicht gefunden.");
        }

        if (!user.IsActive)
        {
            return Core.Abstractions.LicenseAccessDecision.Deny(
                LicenseStatus.None, "Ihr Konto wurde deaktiviert. Bitte wenden Sie sich an einen Administrator.");
        }

        var license = await _db.UserLicenses
            .FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (license is null)
        {
            return Core.Abstractions.LicenseAccessDecision.Deny(
                LicenseStatus.None, "Für dieses Konto ist kein Lizenzschlüssel hinterlegt.");
        }

        var now = _clock.UtcNow;

        // Aktuelles, gültiges Ergebnis verwenden (Zwischenspeicherung).
        if (license.Status == LicenseStatus.Valid && license.LastValidatedAt.HasValue)
        {
            var cacheExpiry = license.LastValidatedAt.Value.AddMinutes(Math.Max(0, _options.ValidationCacheMinutes));
            if (now < cacheExpiry)
            {
                return Allow(license, localGrace: false,
                    message: license.InGracePeriod
                        ? "Lizenz gültig (OpenLicensd-Kulanzfrist aktiv)."
                        : "Lizenz gültig.");
            }
        }

        // Ablehnende Ergebnisse nur nach Ablauf der Negativ-Zwischenspeicherung erneut prüfen.
        var isDefinitiveRejection = license.Status is
            LicenseStatus.NotFound or LicenseStatus.Expired or LicenseStatus.Revoked
            or LicenseStatus.ProductMismatch or LicenseStatus.FingerprintRequired
            or LicenseStatus.ActivationLimitReached or LicenseStatus.Invalid;

        if (isDefinitiveRejection && license.LastAttemptAt.HasValue)
        {
            var negativeExpiry = license.LastAttemptAt.Value.AddMinutes(Math.Max(0, _options.NegativeCacheMinutes));
            if (now < negativeExpiry)
            {
                return Core.Abstractions.LicenseAccessDecision.Deny(license.Status, DescribeStatus(license.Status, license.LastReason));
            }
        }

        if (IsDue(license, now))
        {
            var refreshed = await RevalidateAsync(userId, null, force: true, cancellationToken).ConfigureAwait(false);
            if (refreshed.View is not null)
            {
                license = await _db.UserLicenses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken)
                    .ConfigureAwait(false) ?? license;
            }
        }

        // Nach der Prüfung neu bewerten.
        if (license.Status == LicenseStatus.Valid)
        {
            return Allow(license, localGrace: false, message: license.InGracePeriod
                ? "Lizenz gültig (OpenLicensd-Kulanzfrist aktiv)."
                : "Lizenz gültig.");
        }

        var transient = license.Status is LicenseStatus.Unreachable or LicenseStatus.Malformed;

        if (transient)
        {
            // Lokale, zeitlich begrenzte Kulanzfrist nur bei zuvor bestätigter Lizenz.
            var graceEnd = license.LastValidatedAt?.AddHours(Math.Max(0, _options.GracePeriodHours));
            if (license.LastValidatedAt.HasValue && graceEnd.HasValue && now < graceEnd.Value)
            {
                await MarkServingFromGraceAsync(license.Id, cancellationToken).ConfigureAwait(false);
                return Allow(license, localGrace: true,
                    message: "Lizenzserver derzeit nicht erreichbar – Zugriff läuft über die Kulanzfrist. " +
                             $"Ende: {graceEnd.Value:dd.MM.yyyy HH:mm} UTC.");
            }

            return Core.Abstractions.LicenseAccessDecision.Deny(
                license.Status,
                "Die Lizenz konnte nicht bestätigt werden und die Kulanzfrist ist abgelaufen. " +
                "Bitte prüfen Sie die Verbindung zum Lizenzserver oder erneuern Sie die Lizenz.");
        }

        return Core.Abstractions.LicenseAccessDecision.Deny(license.Status, DescribeStatus(license.Status, license.LastReason));
    }

    private async Task MarkServingFromGraceAsync(Guid licenseId, CancellationToken cancellationToken)
    {
        await _db.UserLicenses
            .Where(l => l.Id == licenseId && !l.ServingFromGrace)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ServingFromGrace, true), cancellationToken)
            .ConfigureAwait(false);
    }

    private static LicenseAccessDecision Allow(UserLicense license, bool localGrace, string message) => new()
    {
        Allowed = true,
        Status = LicenseStatus.Valid,
        ServingFromLocalGrace = localGrace,
        InServerGracePeriod = license.InGracePeriod,
        ExpiresAt = license.ExpiresAt,
        LastValidatedAt = license.LastValidatedAt,
        Message = message,
    };

    // ---------------------------------------------------------------- Status/Abfragen

    /// <summary>Liefert den Anzeige-Status ohne Lizenzschlüssel.</summary>
    public async Task<LicenseStatusView> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.IsActive })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var license = await _db.UserLicenses
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (license is null)
        {
            return new LicenseStatusView
            {
                HasLicense = false,
                Status = LicenseStatus.None,
                StatusLabel = "Keine Lizenz hinterlegt",
            };
        }

        return new LicenseStatusView
        {
            HasLicense = true,
            Status = license.Status,
            StatusLabel = DescribeStatus(license.Status, license.LastReason),
            MaskedKey = license.LicenseKeyPrefix,
            PolicyName = license.PolicyName,
            ProductCode = license.ProductCode,
            ExpiresAt = license.ExpiresAt,
            InGracePeriod = license.InGracePeriod,
            ServingFromLocalGrace = license.ServingFromGrace,
            ActivationCount = license.ActivationCount,
            MaxActivations = license.MaxActivations,
            LastValidatedAt = license.LastValidatedAt,
            LastAttemptAt = license.LastAttemptAt,
            LastError = license.LastError,
            LastReason = license.LastReason,
            OrganizationId = license.OrganizationId,
            AssignedAt = license.AssignedAt,
        };
    }

    /// <summary>Administratorübersicht über Benutzer und Lizenzstatus.</summary>
    public async Task<IReadOnlyList<LicenseAdminRow>> GetAdminOverviewAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        // Zwei getrennte Abfragen (keine verschachtelten Projektionen), damit der
        // Provider die Abfrage zuverlässig übersetzt.
        var usersQuery = _db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            usersQuery = usersQuery.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(term)) ||
                u.DisplayName.ToLower().Contains(term));
        }

        var users = await usersQuery
            .OrderBy(u => u.DisplayName)
            .Take(500)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.IsActive })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var userIds = users.Select(u => u.Id).ToList();
        var licenses = await _db.UserLicenses
            .AsNoTracking()
            .Where(l => userIds.Contains(l.UserId))
            .Select(l => new
            {
                l.UserId,
                l.Status,
                l.LicenseKeyPrefix,
                l.PolicyName,
                l.ExpiresAt,
                l.LastValidatedAt,
                l.InGracePeriod,
                l.ConsecutiveTransientFailures,
                l.LastError,
            })
            .ToDictionaryAsync(l => l.UserId, cancellationToken)
            .ConfigureAwait(false);

        return users.Select(u =>
        {
            licenses.TryGetValue(u.Id, out var license);

            return new LicenseAdminRow
            {
                UserId = u.Id,
                DisplayName = string.IsNullOrWhiteSpace(u.DisplayName) ? (u.Email ?? "–") : u.DisplayName,
                Email = u.Email ?? "–",
                IsActive = u.IsActive,
                HasLicense = license is not null,
                Status = license?.Status ?? LicenseStatus.None,
                MaskedKey = license?.LicenseKeyPrefix,
                PolicyName = license?.PolicyName,
                ExpiresAt = license?.ExpiresAt,
                LastValidatedAt = license?.LastValidatedAt,
                InGracePeriod = license?.InGracePeriod ?? false,
                ConsecutiveTransientFailures = license?.ConsecutiveTransientFailures ?? 0,
                LastError = license?.LastError,
            };
        }).ToList();
    }

    /// <summary>Anzahl der Benutzer mit gültiger Lizenz (Administratorkennzahl).</summary>
    public Task<int> CountValidLicensesAsync(CancellationToken cancellationToken = default)
        => _db.UserLicenses.AsNoTracking().CountAsync(l => l.Status == LicenseStatus.Valid, cancellationToken);

    // ---------------------------------------------------------------- intern

    private async Task<LicenseCheckResult> RunCheckAsync(
        Guid userId,
        string plaintextKey,
        string fingerprint,
        bool isActivation,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // Global serialisiert: begrenzt gleichzeitige Aufrufe des Lizenzservers.
        await ValidationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var request = new LicenseValidationRequest
            {
                LicenseKey = plaintextKey,
                Product = _options.ProductCode,
                Fingerprint = fingerprint,
                Hostname = _options.Hostname,
            };

            var result = await _validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);

            _db.LicenseValidationLogs.Add(new LicenseValidationLog
            {
                UserId = userId,
                Fingerprint = fingerprint,
                Outcome = result.Outcome.ToString(),
                Reason = result.Reason,
                HttpStatusCode = result.HttpStatusCode,
                Diagnostic = Truncate(result.Diagnostic, 400),
                CreatedAt = _clock.UtcNow,
            });

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Lizenzprüfung ({Mode}) für Benutzer {UserId}: {Outcome}/{Reason}.",
                isActivation ? "Aktivierung" : "Erneuerung", userId, result.Outcome, result.Reason ?? "-");

            return result;
        }
        finally
        {
            ValidationLock.Release();
        }
    }

    private void ApplyCheckResult(UserLicense license, LicenseCheckResult check)
    {
        license.LastAttemptAt = _clock.UtcNow;

        switch (check.Outcome)
        {
            case LicenseCheckOutcome.Valid:
                license.Status = LicenseStatus.Valid;
                license.LastValidatedAt = _clock.UtcNow;
                license.InGracePeriod = check.InGracePeriod;
                license.ServingFromGrace = false;
                license.ConsecutiveTransientFailures = 0;
                license.LastError = null;
                license.LastReason = null;
                license.ProductCode = check.ProductCode;
                license.PolicyName = check.PolicyName;
                license.ExpiresAt = check.ExpiresAt;
                license.ActivationCount = check.ActivationCount;
                license.MaxActivations = check.MaxActivations;
                license.RevokedAt = null;
                break;

            case LicenseCheckOutcome.Rejected:
                license.Status = MapReason(check.Reason);
                license.LastReason = check.Reason;
                license.ProductCode = check.ProductCode ?? license.ProductCode;
                license.PolicyName = check.PolicyName ?? license.PolicyName;
                license.ExpiresAt = check.ExpiresAt ?? license.ExpiresAt;
                license.InGracePeriod = check.InGracePeriod;
                license.ActivationCount = check.ActivationCount ?? license.ActivationCount;
                license.MaxActivations = check.MaxActivations ?? license.MaxActivations;
                license.LastError = check.Diagnostic;
                license.ServingFromGrace = false;
                license.RevokedAt = license.Status == LicenseStatus.Revoked ? _clock.UtcNow : license.RevokedAt;
                break;

            case LicenseCheckOutcome.Transient:
            case LicenseCheckOutcome.Malformed:
            default:
                // Status bleibt unverändert; nur Diagnose und Fehlerzähler werden gepflegt.
                license.ConsecutiveTransientFailures++;
                license.LastError = check.Diagnostic;
                license.ServingFromGrace = false;
                if (license.Status is LicenseStatus.None or LicenseStatus.Valid)
                {
                    license.Status = check.Outcome == LicenseCheckOutcome.Transient
                        ? LicenseStatus.Unreachable
                        : LicenseStatus.Malformed;
                }
                break;
        }
    }

    private bool IsDue(UserLicense license, DateTimeOffset now)
    {
        if (license.LastAttemptAt is null)
        {
            return true;
        }

        var interval = license.Status == LicenseStatus.Valid
            ? Math.Max(0, _options.ValidationCacheMinutes) + 1
            : Math.Max(0, _options.NegativeCacheMinutes) + 1;

        return now >= license.LastAttemptAt.Value.AddMinutes(interval);
    }

    private static LicenseStatus MapReason(string? reason) => reason switch
    {
        LicenseReasons.NotFound => LicenseStatus.NotFound,
        LicenseReasons.Expired => LicenseStatus.Expired,
        LicenseReasons.Revoked => LicenseStatus.Revoked,
        LicenseReasons.ProductMismatch => LicenseStatus.ProductMismatch,
        LicenseReasons.FingerprintRequired => LicenseStatus.FingerprintRequired,
        LicenseReasons.ActivationLimit => LicenseStatus.ActivationLimitReached,
        _ => LicenseStatus.Invalid,
    };

    private static string DescribeCheck(LicenseCheckResult check) => check.Outcome switch
    {
        LicenseCheckOutcome.Valid => "Lizenz gültig.",
        LicenseCheckOutcome.Transient => "Der Lizenzserver ist derzeit nicht erreichbar. Bitte später erneut versuchen.",
        LicenseCheckOutcome.Malformed => "Der Lizenzserver hat unerwartet geantwortet. Bitte später erneut versuchen.",
        _ => DescribeStatus(MapReason(check.Reason), check.Reason),
    };

    private static string DescribeStatus(LicenseStatus status, string? reason) => status switch
    {
        LicenseStatus.Valid => "Lizenz gültig.",
        LicenseStatus.None => "Keine Lizenz hinterlegt.",
        LicenseStatus.NotFound => "Dieser Lizenzschlüssel ist nicht bekannt.",
        LicenseStatus.Expired => "Diese Lizenz ist abgelaufen.",
        LicenseStatus.Revoked => "Diese Lizenz wurde gesperrt.",
        LicenseStatus.ProductMismatch => "Dieser Lizenzschlüssel gilt nicht für DESD LifeHub.",
        LicenseStatus.ActivationLimitReached =>
            "Das Aktivierungslimit dieser Lizenz ist erreicht. Bitte eine andere Lizenz verwenden oder den Anbieter kontaktieren.",
        LicenseStatus.FingerprintRequired => "Diese Lizenz benötigt eine Aktivierungs-ID, die nicht übermittelt werden konnte.",
        LicenseStatus.Unreachable => "Der Lizenzserver ist derzeit nicht erreichbar.",
        LicenseStatus.Malformed => "Der Lizenzserver hat unerwartet geantwortet.",
        _ => $"Lizenz ungültig{(reason is null ? "." : $" ({reason}).")}",
    };

    private string DecisionCacheKey(Guid userId) => $"license-decision:{userId:N}";

    private void InvalidateDecisionCache(Guid userId) => _cache.Remove(DecisionCacheKey(userId));

    private static string? Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}
