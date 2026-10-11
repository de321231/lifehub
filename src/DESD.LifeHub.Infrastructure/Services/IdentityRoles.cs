namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>Rollennamen der Anwendungsebene (zusätzlich zu den Organisationsrollen).</summary>
public static class IdentityRoles
{
    public const string Admin = "Admin";
    public const string User = "User";

    public const string AdminDisplay = "Administrator";
    public const string UserDisplay = "Benutzer";

    public static string? Normalize(string? role) => role switch
    {
        null => null,
        var r when r.Equals(Admin, StringComparison.OrdinalIgnoreCase) => Admin,
        var r when r.Equals(User, StringComparison.OrdinalIgnoreCase) => User,
        _ => null,
    };

    public static string Display(string role) => role == Admin ? AdminDisplay : UserDisplay;
}

/// <summary>
/// Katalog gültiger Zeitzonen. Nutzt die Plattformdatenbank und bietet zusätzlich
/// eine kleine, geprüfte Auswahlliste für die Oberfläche.
/// </summary>
public static class TimeZoneCatalog
{
    /// <summary>Häufige Zeitzonen für die Auswahl – wird bei Bedarf aufgefüllt.</summary>
    public static readonly IReadOnlyList<string> Suggested =
    [
        "Europe/Berlin",
        "Europe/Vienna",
        "Europe/Zurich",
        "Europe/Amsterdam",
        "Europe/London",
        "Europe/Paris",
        "Europe/Madrid",
        "Europe/Rome",
        "Europe/Warsaw",
        "Europe/Istanbul",
        "UTC",
        "America/New_York",
        "America/Chicago",
        "America/Los_Angeles",
        "America/Sao_Paulo",
        "Asia/Dubai",
        "Asia/Kolkata",
        "Asia/Shanghai",
        "Asia/Tokyo",
        "Australia/Sydney",
    ];

    public static bool IsKnown(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (IsKnown(timeZoneId))
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId!);
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>Wandelt eine lokale Uhrzeit in der angegebenen Zone in UTC um.</summary>
    public static DateTimeOffset ToUtc(DateTime local, string? timeZoneId)
    {
        var tz = Resolve(timeZoneId);
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var offset = tz.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }

    /// <summary>Wandelt UTC in die lokale Zeit der Zone um.</summary>
    public static DateTimeOffset ToLocal(DateTimeOffset utc, string? timeZoneId)
        => TimeZoneInfo.ConvertTime(TimeUtil.ToUtc(utc), Resolve(timeZoneId));

    /// <summary>Beginn des Kalendertags in lokaler Zeit, als UTC-Wert.</summary>
    public static DateTimeOffset LocalDayStartUtc(DateTimeOffset nowUtc, string? timeZoneId)
    {
        var local = ToLocal(nowUtc, timeZoneId);
        var startLocal = local.Date;
        return ToUtc(startLocal, timeZoneId);
    }
}
