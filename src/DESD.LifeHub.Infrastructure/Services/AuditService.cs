using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DESD.LifeHub.Infrastructure.Options;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>Schreibt Revisionsdatensätze. Enthält niemals Geheimnisse.</summary>
public interface IAuditService
{
    Task WriteAsync(
        string action,
        Guid? userId = null,
        Guid? organizationId = null,
        string? entityType = null,
        string? entityId = null,
        string? details = null,
        bool succeeded = true,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);
}

public sealed class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private readonly AuditOptions _options;
    private readonly ILogger<AuditService> _logger;

    public AuditService(AppDbContext db, IOptions<AuditOptions> options, ILogger<AuditService> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task WriteAsync(
        string action,
        Guid? userId = null,
        Guid? organizationId = null,
        string? entityType = null,
        string? entityId = null,
        string? details = null,
        bool succeeded = true,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            _db.AuditLogs.Add(new AuditLog
            {
                Action = Truncate(action, 120)!,
                UserId = userId,
                OrganizationId = organizationId,
                EntityType = Truncate(entityType, 120),
                EntityId = Truncate(entityId, 120),
                Details = Truncate(Sanitize(details), 2000),
                Succeeded = succeeded,
                IpAddress = Truncate(ipAddress, 64),
                CreatedAt = DateTimeOffset.UtcNow,
            });

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Audit darf den Hauptvorgang nicht zum Absturz bringen.
            _logger.LogError(ex, "Audit-Eintrag für Aktion {Action} konnte nicht gespeichert werden.", action);
        }
    }

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        // Schutz vor versehentlichem Protokollieren von Schlüsseln oder Passwörtern.
        var lowered = value.ToLowerInvariant();
        foreach (var needle in new[] { "key=", "license_key", "password", "passwort", "token=", "secret" })
        {
            if (lowered.Contains(needle))
            {
                return "[redigiert]";
            }
        }

        return value;
    }

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];
}
