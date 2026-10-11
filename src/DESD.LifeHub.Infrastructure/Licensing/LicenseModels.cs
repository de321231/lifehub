using DESD.LifeHub.Core.Enums;

namespace DESD.LifeHub.Infrastructure.Licensing;

/// <summary>Anzeige-Modell des Lizenzstatus – enthält niemals den Lizenzschlüssel.</summary>
public sealed record LicenseStatusView
{
    public bool HasLicense { get; init; }
    public LicenseStatus Status { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public string? MaskedKey { get; init; }
    public string? PolicyName { get; init; }
    public string? ProductCode { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool InGracePeriod { get; init; }
    public bool ServingFromLocalGrace { get; init; }
    public long? ActivationCount { get; init; }
    public long? MaxActivations { get; init; }
    public DateTimeOffset? LastValidatedAt { get; init; }
    public DateTimeOffset? LastAttemptAt { get; init; }
    public string? LastError { get; init; }
    public string? LastReason { get; init; }
    public Guid? OrganizationId { get; init; }
    public DateTimeOffset? AssignedAt { get; init; }
}

/// <summary>Ergebnis einer Aktivierung/Änderung des Lizenzschlüssels.</summary>
public sealed record LicenseActivationResult
{
    public required bool Succeeded { get; init; }

    /// <summary>Maschinenlesbarer Grund bei Fehlschlag (z. B. <c>duplicate_key</c>).</summary>
    public string? FailureCode { get; init; }

    public required string Message { get; init; }

    public LicenseStatus Status { get; init; }

    public LicenseStatusView? View { get; init; }

    public static LicenseActivationResult Fail(string code, string message) => new()
    {
        Succeeded = false,
        FailureCode = code,
        Message = message,
    };
}

/// <summary>Zeile der Administratorübersicht.</summary>
public sealed record LicenseAdminRow
{
    public required Guid UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public bool IsActive { get; init; }
    public bool HasLicense { get; init; }
    public LicenseStatus Status { get; init; }
    public string? MaskedKey { get; init; }
    public string? PolicyName { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? LastValidatedAt { get; init; }
    public bool InGracePeriod { get; init; }
    public int ConsecutiveTransientFailures { get; init; }
    public string? LastError { get; init; }
}
