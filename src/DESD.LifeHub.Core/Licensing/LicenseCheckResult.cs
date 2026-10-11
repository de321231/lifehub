namespace DESD.LifeHub.Core.Licensing;

/// <summary>
/// Maschinenlesbare Ablehnungsgründe, exakt wie sie OpenLicensd im Feld
/// <c>reason</c> von <c>POST /api/v1/validate</c> zurückgibt.
/// Siehe docs/LICENSING.md – die Liste ist laut OpenLicensd-Spezifikation eingefroren.
/// </summary>
public static class LicenseReasons
{
    public const string NotFound = "not_found";
    public const string Expired = "expired";
    public const string Revoked = "revoked";
    public const string ProductMismatch = "product_mismatch";
    public const string FingerprintRequired = "fingerprint_required";
    public const string ActivationLimit = "activation_limit";
}

/// <summary>
/// Ergebnis einer Lizenzprüfung, normalisiert für die interne Verwendung.
/// Enthält bewusst niemals den Klartext-Lizenzschlüssel.
/// </summary>
public sealed record LicenseCheckResult
{
    public required LicenseCheckOutcome Outcome { get; init; }

    /// <summary>Rohwert aus OpenLicensd (<c>valid</c>).</summary>
    public bool Valid { get; init; }

    /// <summary>Rohwert aus OpenLicensd (<c>reason</c>), nur bei <c>valid=false</c> gesetzt.</summary>
    public string? Reason { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? ProductCode { get; init; }

    public string? PolicyName { get; init; }

    /// <summary>OpenLicensd meldet, dass die Lizenz die Kulanzfrist des Servers nutzt.</summary>
    public bool InGracePeriod { get; init; }

    public long? ActivationCount { get; init; }

    public long? MaxActivations { get; init; }

    /// <summary>HTTP-Statuscode, sofern überhaupt eine Antwort empfangen wurde.</summary>
    public int? HttpStatusCode { get; init; }

    /// <summary>Kurze, technische Beschreibung für Audit/Logs (ohne Geheimnisse).</summary>
    public string? Diagnostic { get; init; }

    public static LicenseCheckResult Success(
        string? productCode,
        string? policyName,
        DateTimeOffset? expiresAt,
        bool inGracePeriod,
        long? activationCount,
        long? maxActivations,
        int httpStatusCode) => new()
        {
            Outcome = LicenseCheckOutcome.Valid,
            Valid = true,
            ProductCode = productCode,
            PolicyName = policyName,
            ExpiresAt = expiresAt,
            InGracePeriod = inGracePeriod,
            ActivationCount = activationCount,
            MaxActivations = maxActivations,
            HttpStatusCode = httpStatusCode,
        };

    public static LicenseCheckResult Rejected(
        string reason,
        string? diagnostic = null,
        int? httpStatusCode = 200,
        string? productCode = null,
        string? policyName = null,
        DateTimeOffset? expiresAt = null,
        bool inGracePeriod = false,
        long? activationCount = null,
        long? maxActivations = null) => new()
        {
            Outcome = LicenseCheckOutcome.Rejected,
            Valid = false,
            Reason = reason,
            Diagnostic = diagnostic ?? reason,
            HttpStatusCode = httpStatusCode,
            ProductCode = productCode,
            PolicyName = policyName,
            ExpiresAt = expiresAt,
            InGracePeriod = inGracePeriod,
            ActivationCount = activationCount,
            MaxActivations = maxActivations,
        };

    public static LicenseCheckResult Transient(string diagnostic, int? httpStatusCode = null) => new()
    {
        Outcome = LicenseCheckOutcome.Transient,
        Valid = false,
        Diagnostic = diagnostic,
        HttpStatusCode = httpStatusCode,
    };

    public static LicenseCheckResult Malformed(string diagnostic, int? httpStatusCode = null) => new()
    {
        Outcome = LicenseCheckOutcome.Malformed,
        Valid = false,
        Diagnostic = diagnostic,
        HttpStatusCode = httpStatusCode,
    };
}

/// <summary>Grobe Klassifizierung einer Prüfantwort.</summary>
public enum LicenseCheckOutcome
{
    /// <summary>Lizenz gültig.</summary>
    Valid = 0,

    /// <summary>Der Lizenzserver hat die Lizenz eindeutig abgelehnt – kein Zugriff.</summary>
    Rejected = 1,

    /// <summary>Temporäres Problem (Timeout, 5xx, 429). Kulanzfrist kann greifen.</summary>
    Transient = 2,

    /// <summary>Antwort war 2xx, aber nicht interpretierbar. Wird wie transient behandelt.</summary>
    Malformed = 3,
}
