using System.Security.Cryptography;
using System.Text;

namespace DESD.LifeHub.Infrastructure.Licensing;

/// <summary>
/// Erzeugt die stabile Aktivierungs-ID (OpenLicensd-Feld <c>fingerprint</c>).
///
/// Der Wert ist deterministisch aus Produktcode und Benutzerkennung abgeleitet:
/// dadurch bleibt er über Neustarts und Containerwechsel stabil, ohne dass ein
/// zusätzliches Geheimnis benötigt oder der Lizenzschlüssel verwendet wird.
/// </summary>
public static class LicenseFingerprint
{
    public static string ForUser(Guid userId, string productCode)
    {
        var input = $"{productCode}|{userId:N}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return "lifehub-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
