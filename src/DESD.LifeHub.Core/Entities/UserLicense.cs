using DESD.LifeHub.Core.Enums;

namespace DESD.LifeHub.Core.Entities;

/// <summary>
/// Zuordnung eines persönlichen OpenLicensd-Lizenzschlüssels zu einem Benutzer.
/// Pro Benutzer genau ein Datensatz; pro Schlüsselhash höchstens ein Datensatz
/// (per Unique-Index erzwungen).
/// </summary>
public class UserLicense
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    /// <summary>Optionale Zuordnung zu einer Organisation (Vorbereitung Firmenlizenzen).</summary>
    public Guid? OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    /// <summary>AES-GCM-verschlüsselter Lizenzschlüssel. Niemals Klartext.</summary>
    public string LicenseKeyCiphertext { get; set; } = string.Empty;

    /// <summary>SHA-256-Hash des normalisierten Schlüssels – dient der Eindeutigkeit.</summary>
    public string LicenseKeyHash { get; set; } = string.Empty;

    /// <summary>Anzeigepräfix für Administratoren, z. B. <c>X4F9K…YZ2CD</c>.</summary>
    public string LicenseKeyPrefix { get; set; } = string.Empty;

    /// <summary>Letzter serverseitig ermittelter Status.</summary>
    public LicenseStatus Status { get; set; } = LicenseStatus.None;

    /// <summary>Letzter <c>reason</c>-Code von OpenLicensd.</summary>
    public string? LastReason { get; set; }

    public string? ProductCode { get; set; }

    public string? PolicyName { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public bool InGracePeriod { get; set; }

    public long? ActivationCount { get; set; }

    public long? MaxActivations { get; set; }

    /// <summary>Stabile Aktivierungs-ID, die an OpenLicensd als Fingerprint gesendet wird.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>Letzter erfolgreicher Prüfzeitpunkt (Grundlage der Zwischenspeicherung).</summary>
    public DateTimeOffset? LastValidatedAt { get; set; }

    /// <summary>Letzter Prüfversuch, unabhängig vom Ergebnis.</summary>
    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>Kurzdiagnose des letzten Fehlers (ohne Geheimnisse).</summary>
    public string? LastError { get; set; }

    /// <summary>Aufeinanderfolgende transiente Fehler – Signal für Administratoren.</summary>
    public int ConsecutiveTransientFailures { get; set; }

    /// <summary>True, wenn der Zugriff aktuell über die lokale Kulanzfrist gewährt wird.</summary>
    public bool ServingFromGrace { get; set; }

    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? RevokedAt { get; set; }
}
