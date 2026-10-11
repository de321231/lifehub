namespace DESD.LifeHub.Core.Entities;

/// <summary>
/// Ein Lizenzschlüssel aus dem Firmen-Kontingent ("Lizenzpool"). Der Schlüssel wird
/// vom Systemadministrator über die OpenLicensd-Admin-API erzeugt und serverseitig
/// verschlüsselt abgelegt. Er wird erst bei der Zuweisung an ein Mitarbeiterkonto in
/// eine <see cref="UserLicense"/> überführt.
///
/// Mitarbeiter sehen den Schlüssel niemals; die Firmenverwaltung sieht ausschliesslich
/// Zählwerte (gesamt / zugewiesen / frei) und maskierte Präfixe.
/// </summary>
public class CompanyLicense
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    /// <summary>Bezeichnung des Schlüssels in OpenLicensd (z. B. "LifeHub – Acme #1").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>OpenLicensd-ID des Schlüssels (keine Geheiminformation).</summary>
    public string? OpenLicensdLicenseId { get; set; }

    /// <summary>AES-GCM-verschlüsselter Lizenzschlüssel. Niemals Klartext in der Datenbank.</summary>
    public string LicenseKeyCiphertext { get; set; } = string.Empty;

    /// <summary>SHA-256-Hash (mit Pepper) des normalisierten Schlüssels – dient der Eindeutigkeit.</summary>
    public string LicenseKeyHash { get; set; } = string.Empty;

    /// <summary>Maskiertes Präfix zur Anzeige in der Firmenverwaltung.</summary>
    public string LicenseKeyPrefix { get; set; } = string.Empty;

    public string? ProductCode { get; set; }

    public string? PolicyName { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public int? MaxActivations { get; set; }

    /// <summary>In OpenLicensd gesperrt (bleibt als Datensatz erhalten).</summary>
    public bool Revoked { get; set; }

    /// <summary>Zugewiesener Mitarbeiter; null bedeutet "frei".</summary>
    public Guid? AssignedToUserId { get; set; }

    public ApplicationUser? AssignedToUser { get; set; }

    public DateTimeOffset? AssignedAt { get; set; }

    /// <summary>Systemadministrator, der den Schlüssel erzeugt hat.</summary>
    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsAssigned => AssignedToUserId.HasValue;

    public bool IsFree => !AssignedToUserId.HasValue && !Revoked;
}
