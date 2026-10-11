namespace DESD.LifeHub.Infrastructure.Options;

/// <summary>
/// Bindung an den Abschnitt <c>OpenLicensd</c> in appsettings/Umgebungsvariablen.
/// </summary>
public sealed class OpenLicensdOptions
{
    public const string SectionName = "OpenLicensd";

    /// <summary>Basisadresse des Lizenzservers, z. B. https://license.desd.uk</summary>
    public string BaseUrl { get; set; } = "https://license.desd.uk";

    /// <summary>Produktcode laut OpenLicensd (hier: desd-lifehub).</summary>
    public string ProductCode { get; set; } = "desd-lifehub";

    /// <summary>Pfad des öffentlichen Validierungsendpunkts.</summary>
    public string ValidatePath { get; set; } = "/api/v1/validate";

    /// <summary>Anzeigename des Hosts, wird als <c>hostname</c> mitsendet.</summary>
    public string Hostname { get; set; } = "lifehub-server";

    /// <summary>HTTP-Timeout in Sekunden. Verhindert hängende Anfragen.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Wie lange ein gültiges Prüfergebnis zwischengespeichert wird, bevor erneut
    /// gegen den Lizenzserver geprüft wird.
    /// </summary>
    public int ValidationCacheMinutes { get; set; } = 60;

    /// <summary>
    /// Wie lange ein ablehnendes Ergebnis zwischengespeichert wird, bevor erneut
    /// geprüft wird. Schützt den Lizenzserver vor Anfragenflut.
    /// </summary>
    public int NegativeCacheMinutes { get; set; } = 5;

    /// <summary>
    /// Kurze Zwischenspeicherung der Zugriffsentscheidung im Speicher (Sekunden).
    /// 0 deaktiviert die Zwischenspeicherung vollständig.
    /// </summary>
    public int DecisionCacheSeconds { get; set; } = 10;

    /// <summary>
    /// Lokale Kulanzfrist in Stunden für den Fall, dass der Lizenzserver
    /// vorübergehend nicht erreichbar ist. Greift nur, wenn zuletzt ein gültiges
    /// Ergebnis vorlag.
    /// </summary>
    public int GracePeriodHours { get; set; } = 72;

    /// <summary>Bei falsch/leer wird die Lizenzprüfung als nicht konfiguriert gewertet.</summary>
    public bool Enabled { get; set; } = true;

    // ---------------------------------------------------------------- Admin-API
    // Die folgenden Werte werden ausschliesslich serverseitig verwendet, um über die
    // OpenLicensd-Admin-API Firmen-Kontingente zu erzeugen. Das API-Token wird niemals
    // an den Browser ausgeliefert, nie protokolliert und nie in eine URL geschrieben.

    /// <summary>
    /// Bearer-Token eines OpenLicensd-API-Tokens mit Admin-Rechten. Leer bedeutet,
    /// dass keine Kontingente erzeugt werden können ("nur Prüfung").
    /// </summary>
    public string? ApiToken { get; set; }

    /// <summary>Pfad zum Anlegen/Auflisten von Lizenzen.</summary>
    public string LicensesPath { get; set; } = "/api/v1/licenses";

    /// <summary>Pfad zur Produktliste (Auflösung der Produkt-ID über den Code).</summary>
    public string ProductsPath { get; set; } = "/api/v1/products";

    /// <summary>Pfad zur Richtlinienliste (Auflösung der Richtlinien-ID).</summary>
    public string PoliciesPath { get; set; } = "/api/v1/policies";

    /// <summary>
    /// Optionale, fest konfigurierte OpenLicensd-Produkt-ID. Ist sie gesetzt, wird
    /// die Produktliste nicht abgefragt.
    /// </summary>
    public string? ProductId { get; set; }

    /// <summary>
    /// Optionale, fest konfigurierte Richtlinien-ID. Ist sie gesetzt, wird die
    /// Richtlinienliste nicht abgefragt.
    /// </summary>
    public string? PolicyId { get; set; }

    /// <summary>
    /// Standard-Aktivierungslimit für neu erzeugte Firmenlizenzen. 1 bindet einen
    /// Schlüssel an genau eine Installation (ein Mitarbeiterkonto).
    /// </summary>
    public int DefaultMaxActivations { get; set; } = 1;

    /// <summary>Ob die Admin-API für Kontingente genutzt werden kann (Token vorhanden).</summary>
    public bool AdminApiConfigured => !string.IsNullOrWhiteSpace(ApiToken);
}

/// <summary>Abschnitt <c>LicenseKeyProtection</c> – Schlüssel liegt ausserhalb der Datenbank.</summary>
public sealed class LicenseKeyProtectionOptions
{
    public const string SectionName = "LicenseKeyProtection";

    /// <summary>
    /// Base64-kodierter 32-Byte-Schlüssel für AES-256-GCM. Muss gesetzt sein, wenn
    /// Lizenzschlüssel gespeichert werden.
    /// </summary>
    public string? EncryptionKey { get; set; }

    /// <summary>
    /// Zusätzlicher Pepper für den Eindeutigkeitshash. Änderung invalidiert alle
    /// bestehenden Hashwerte.
    /// </summary>
    public string? HashPepper { get; set; }
}

/// <summary>Abschnitt <c>Audit</c>.</summary>
public sealed class AuditOptions
{
    public const string SectionName = "Audit";
    public bool Enabled { get; set; } = true;
}

/// <summary>Abschnitt <c>Security</c>.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Im Produktivbetrieb true: erzwingt HTTPS-Weiterleitung und Secure-Cookies.</summary>
    public bool RequireHttps { get; set; }

    /// <summary>Sekunden, die eine Anmeldung gültig bleibt, ohne dass die Sitzung neu ausgestellt wird.</summary>
    public int SessionTimeoutMinutes { get; set; } = 480;

    /// <summary>Aktiviert das Rate-Limiting für sensible Endpunkte.</summary>
    public bool EnableRateLimiting { get; set; } = true;
}

/// <summary>Abschnitt <c>Database</c>.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Beim Start ausstehende Migrationen anwenden. Bewusst konfigurierbar, damit
    /// Schemaänderungen kontrolliert und nicht "zufällig" erfolgen.
    /// </summary>
    public bool MigrateOnStartup { get; set; } = true;

    /// <summary>Argumentationsloses Protokollieren der SQL-Befehle.</summary>
    public bool LogSql { get; set; }

    /// <summary>Startet die Anwendung, wenn die Datenbank nicht erreichbar ist.</summary>
    public bool RetryOnFailure { get; set; } = true;

    public int MaxRetries { get; set; } = 10;

    public int RetryDelaySeconds { get; set; } = 6;
}

/// <summary>Abschnitt <c>Bootstrap</c>: erstmalige Einrichtung ohne Standardkonto im Code.</summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    /// <summary>
    /// Optionale E-Mail des ersten Administrators. Ohne diesen Wert wird kein
    /// Administratorkonto angelegt; es gibt keine hartcodierten Zugangsdaten.
    /// </summary>
    public string? AdminEmail { get; set; }

    /// <summary>Passwort des ersten Administrators. Muss den Passwortregeln entsprechen.</summary>
    public string? AdminPassword { get; set; }

    public string? AdminDisplayName { get; set; }

    /// <summary>Selbstregistrierung erlauben (im Firmenbetrieb häufig deaktiviert).</summary>
    public bool AllowSelfRegistration { get; set; } = true;

    /// <summary>Beim Start Datenbankmigrationen anwenden (siehe Database:MigrateOnStartup).</summary>
    public bool CreatePersonalOrganizationForNewUsers { get; set; } = true;
}

/// <summary>Abschnitt <c>Reminders</c>.</summary>
public sealed class ReminderOptions
{
    public const string SectionName = "Reminders";

    /// <summary>Hintergrunddienst aktiv.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Prüfintervall in Sekunden.</summary>
    public int PollSeconds { get; set; } = 30;

    /// <summary>
    /// Maximale Anzahl Erinnerungen pro Durchlauf; begrenzt Lastspitzen.
    /// </summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>
    /// Standardvorlaufzeit in Minuten, wenn eine Aufgabe/ein Termin ohne eigene
    /// Vorlaufzeit gespeichert wird.
    /// </summary>
    public int DefaultLeadMinutes { get; set; } = 15;

    /// <summary>Erinnerungen zusätzlich an den E-Mail-Versand übergeben (sofern konfiguriert).</summary>
    public bool EmailChannelEnabled { get; set; }
}

/// <summary>Abschnitt <c>Storage</c>: Ablage hochgeladener Dateien (Anhänge).</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Verzeichnis für Aufgabeanhänge. Relativ zum Inhaltsverzeichnis oder absolut.
    /// In Docker wird hierfür ein persistentes Volume eingebunden.
    /// </summary>
    public string AttachmentsPath { get; set; } = "data/attachments";

    /// <summary>Maximale Dateigrösse in Bytes (Standard: 10 MiB).</summary>
    public long MaxAttachmentBytes { get; set; } = 10 * 1024 * 1024;
}

/// <summary>Abschnitt <c>Mail</c>: im MVP bewusst leer – es wird kein Versand vorgetäuscht.</summary>
public sealed class MailOptions
{
    public const string SectionName = "Mail";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "DESD LifeHub";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}
