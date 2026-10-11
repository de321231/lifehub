namespace DESD.LifeHub.Core.Entities;

/// <summary>
/// Protokoll der an den Lizenzserver übermittelten Aktivierungen. Dient der
/// Nachvollziehbarkeit (welcher Fingerprint wurde wann gemeldet) und enthält
/// niemals den Lizenzschlüssel.
/// </summary>
public class LicenseValidationLog
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? UserLicenseId { get; set; }

    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>Ergebnisklassifizierung (z. B. "Valid", "Rejected", "Transient").</summary>
    public string Outcome { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? Diagnostic { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
