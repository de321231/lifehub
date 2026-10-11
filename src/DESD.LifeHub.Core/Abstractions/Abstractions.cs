using DESD.LifeHub.Core.Licensing;

namespace DESD.LifeHub.Core.Abstractions;

/// <summary>
/// Kapselt die Kommunikation mit dem OpenLicensd-Server vollständig.
/// Implementierungen dürfen den Lizenzschlüssel nur als Argument entgegennehmen und
/// ihn niemals protokollieren, zurückgeben oder in Fehlermeldungen einbetten.
/// </summary>
public interface ILicenseValidator
{
    /// <summary>
    /// Prüft einen Lizenzschlüssel gegen <c>POST {BaseUrl}/api/v1/validate</c>.
    /// Wirft keine Ausnahmen für Netzwerk- oder Serverfehler, sondern liefert
    /// <see cref="LicenseCheckOutcome.Transient"/> beziehungsweise
    /// <see cref="LicenseCheckOutcome.Malformed"/>.
    /// </summary>
    Task<LicenseCheckResult> ValidateAsync(LicenseValidationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Eingabedaten für eine Lizenzprüfung.</summary>
public sealed record LicenseValidationRequest
{
    /// <summary>Der vollständige Lizenzschlüssel im Klartext (nur im Speicher).</summary>
    public required string LicenseKey { get; init; }

    /// <summary>Produktcode, standardmässig <c>desd-lifehub</c>.</summary>
    public required string Product { get; init; }

    /// <summary>
    /// Stabile Aktivierungs-ID. Wird als <c>fingerprint</c> übertragen, weil
    /// OpenLicensd darüber Aktivierungen/Geräte zählt.
    /// </summary>
    public string? Fingerprint { get; init; }

    /// <summary>Optionaler Anzeigename des Hosts (untrusted, laut OpenLicensd).</summary>
    public string? Hostname { get; init; }

    /// <summary>Die <c>key</c>-Länge darf OpenLicensd zufolge begrenzt sein; hier nur informativ.</summary>
    public override string ToString() => $"LicenseValidationRequest {{ Product = {Product}, Fingerprint = {Fingerprint}, Hostname = {Hostname} }}";
}

/// <summary>
/// Verschlüsselt Lizenzschlüssel für die Speicherung. Der Schlüssel liegt
/// ausserhalb der Datenbank (Umgebungsvariable/Secret).
/// </summary>
public interface ILicenseKeyProtector
{
    /// <summary>Verschlüsselt einen Klartextschlüssel. Ergebnis ist base64.</summary>
    string Protect(string plaintext);

    /// <summary>Entschlüsselt einen zuvor mit <see cref="Protect"/> erzeugten Wert.</summary>
    string Unprotect(string protectedValue);

    /// <summary>Bildet einen stabilen, nicht umkehrbaren Hashwert (für Eindeutigkeit).</summary>
    string Hash(string plaintext);

    /// <summary>Erzeugt ein Anzeigepräfix wie <c>X4F9K…YZ2CD</c> ohne Geheimnisverlust.</summary>
    string Mask(string plaintext);
}

/// <summary>Abstraktion der Uhrzeit, damit Kulanzfristen testbar bleiben.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Versand von Benachrichtigungen. Eine echte E-Mail-Anbindung ist im MVP nicht
/// konfiguriert; die Standardimplementierung protokolliert nur.
/// </summary>
public interface IEmailSender
{
    bool IsConfigured { get; }

    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(string To, string Subject, string Body);
