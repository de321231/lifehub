using System.Security.Cryptography;
using System.Text;
using DESD.LifeHub.Core.Abstractions;
using DESD.LifeHub.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace DESD.LifeHub.Infrastructure.Licensing;

/// <summary>
/// Verschlüsselt Lizenzschlüssel mit AES-256-GCM. Der Schlüssel wird ausschliesslich
/// aus der Konfiguration (Umgebungsvariable/<c>LICENSE_ENCRYPTION_KEY</c>) gelesen und
/// liegt damit niemals in der Datenbank.
/// </summary>
public sealed class AesLicenseKeyProtector : ILicenseKeyProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;
    private readonly string _pepper;

    public AesLicenseKeyProtector(IOptions<LicenseKeyProtectionOptions> options)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.EncryptionKey))
        {
            throw new InvalidOperationException(
                "LicenseKeyProtection:EncryptionKey ist nicht konfiguriert. Ohne Schlüssel können " +
                "Lizenzschlüssel nicht sicher gespeichert werden. Bitte LICENSE_ENCRYPTION_KEY setzen " +
                "(32 Byte, base64).");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(o.EncryptionKey.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("LicenseKeyProtection:EncryptionKey ist kein gültiger base64-Wert.", ex);
        }

        if (key.Length != 32)
        {
            throw new InvalidOperationException(
                $"LicenseKeyProtection:EncryptionKey muss 32 Byte (256 Bit) lang sein, ist aber {key.Length} Byte.");
        }

        _key = key;
        _pepper = o.HashPepper ?? string.Empty;
    }

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var payload = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceSize + TagSize, ciphertext.Length);

        return Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedValue);

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(protectedValue);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Gespeicherter Lizenzschlüssel ist kein gültiger base64-Wert.", ex);
        }

        if (payload.Length <= NonceSize + TagSize)
        {
            throw new CryptographicException("Gespeicherter Lizenzschlüssel ist unvollständig.");
        }

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var ciphertext = payload.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    public string Hash(string plaintext)
    {
        var normalized = Normalize(plaintext);
        var bytes = Encoding.UTF8.GetBytes(_pepper + "|" + normalized);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public string Mask(string plaintext)
    {
        var normalized = Normalize(plaintext);
        if (normalized.Length <= 6)
        {
            return "••••";
        }

        return $"{normalized[..5]}…{normalized[^4..]}";
    }

    /// <summary>
    /// Normalisiert einen Schlüssel wie OpenLicensd: Leerraum entfernen, Grossbuchstaben,
    /// Bindestriche entfernen, mehrdeutige Crockford-Zeichen vereinheitlichen.
    /// </summary>
    public static string Normalize(string key)
    {
        var compact = new StringBuilder(key.Length);
        foreach (var c in key)
        {
            if (char.IsWhiteSpace(c) || c == '-')
            {
                continue;
            }

            var upper = char.ToUpperInvariant(c);
            compact.Append(upper switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                _ => upper,
            });
        }

        return compact.ToString();
    }
}
