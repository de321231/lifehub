using DESD.LifeHub.Core.Enums;

namespace DESD.LifeHub.Core.Entities;

/// <summary>
/// Benutzerkonto. Erbt von IdentityUser mit Guid-Schlüssel.
/// </summary>
public class ApplicationUser : Microsoft.AspNetCore.Identity.IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Bevorzugte Sprache (im MVP: "de").</summary>
    public string PreferredLanguage { get; set; } = "de";

    /// <summary>IANA-Zeitzonenkennung, z. B. "Europe/Berlin".</summary>
    public string TimeZoneId { get; set; } = "Europe/Berlin";

    public ThemePreference ThemePreference { get; set; } = ThemePreference.System;

    /// <summary>In-App-Benachrichtigungen für Erinnerungen aktiviert.</summary>
    public bool InAppNotificationsEnabled { get; set; } = true;

    /// <summary>E-Mail-Erinnerungen gewünscht (wirkt erst mit konfiguriertem Mailversand).</summary>
    public bool EmailNotificationsEnabled { get; set; }

    /// <summary>Von einem Administrator deaktivierbare Konten dürfen sich nicht anmelden.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Persönlicher Bereich jedes Benutzers.</summary>
    public Guid? PersonalOrganizationId { get; set; }

    public Organization? PersonalOrganization { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }
}
