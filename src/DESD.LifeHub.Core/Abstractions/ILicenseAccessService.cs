using DESD.LifeHub.Core.Enums;

namespace DESD.LifeHub.Core.Abstractions;

/// <summary>
/// Zentrale, serverseitige Zugriffsprüfung anhand der Lizenz. Wird von der
/// Middleware und von API-Endpunkten verwendet, damit Lizenzprüfungen nicht
/// dupliziert werden.
/// </summary>
public interface ILicenseAccessService
{
    Task<LicenseAccessDecision> EvaluateAccessAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Ergebnis einer Zugriffsentscheidung (Spiegel von Infrastructure.Licensing.LicenseAccessDecision).</summary>
public sealed record LicenseAccessDecision
{
    public required bool Allowed { get; init; }

    public required LicenseStatus Status { get; init; }

    public bool ServingFromLocalGrace { get; init; }

    public bool InServerGracePeriod { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? LastValidatedAt { get; init; }

    public required string Message { get; init; }

    public static LicenseAccessDecision Deny(LicenseStatus status, string message) => new()
    {
        Allowed = false,
        Status = status,
        Message = message,
    };
}
