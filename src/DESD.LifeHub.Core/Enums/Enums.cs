namespace DESD.LifeHub.Core.Enums;

/// <summary>Lebenszyklus einer Aufgabe.</summary>
public enum TaskItemStatus
{
    Open = 0,
    InProgress = 1,
    Blocked = 2,
    Done = 3,
    Cancelled = 4,
}

/// <summary>Priorität einer Aufgabe.</summary>
public enum TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3,
}

/// <summary>Rolle eines Benutzers innerhalb einer Organisation.</summary>
public enum OrganizationRole
{
    Member = 0,
    Manager = 1,
    Owner = 2,
}

/// <summary>
/// Serverseitig ermittelter Lizenzstatus. Wird ausschliesslich vom Backend gesetzt
/// und niemals aus Browser-Eingaben übernommen.
/// </summary>
public enum LicenseStatus
{
    /// <summary>Noch kein Schlüssel hinterlegt.</summary>
    None = 0,

    /// <summary>Server hat die Lizenz bestätigt.</summary>
    Valid = 1,

    /// <summary>Schlüssel unbekannt (not_found).</summary>
    NotFound = 2,

    /// <summary>Definitiv abgelehnt, Grund nicht im bekannten OpenLicensd-Enum.</summary>
    Invalid = 10,

    /// <summary>Abgelaufen (expired).</summary>
    Expired = 3,

    /// <summary>Gesperrt/widerrufen (revoked).</summary>
    Revoked = 4,

    /// <summary>Schlüssel gehört zu einem anderen Produkt (product_mismatch).</summary>
    ProductMismatch = 5,

    /// <summary>Aktivierungslimit erreicht (activation_limit).</summary>
    ActivationLimitReached = 6,

    /// <summary>Der Server verlangt einen Fingerprint (fingerprint_required).</summary>
    FingerprintRequired = 7,

    /// <summary>Server nicht erreichbar / unerwartete Antwort – vorübergehend unklar.</summary>
    Unreachable = 8,

    /// <summary>Antwort des Servers nicht interpretierbar.</summary>
    Malformed = 9,
}

/// <summary>Status einer Erinnerung.</summary>
public enum ReminderStatus
{
    Pending = 0,
    Triggered = 1,
    Dismissed = 2,
    Cancelled = 3,
    Failed = 4,
}

/// <summary>Auslieferungskanal einer Erinnerung.</summary>
public enum ReminderChannel
{
    InApp = 0,
    Email = 1,
}

/// <summary>Vom Benutzer gewähltes Erscheinungsbild.</summary>
public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>Wiederholungsrhythmus eines Termins.</summary>
public enum RecurrenceFrequency
{
    /// <summary>Keine Wiederholung (Einzeltermin).</summary>
    None = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Yearly = 4,
}

/// <summary>Antwortstatus eines Terminteilnehmers.</summary>
public enum ParticipantResponse
{
    Invited = 0,
    Accepted = 1,
    Tentative = 2,
    Declined = 3,
}

/// <summary>Dringlichkeit einer Firmenankündigung.</summary>
public enum AnnouncementPriority
{
    Normal = 0,
    Important = 1,
    Critical = 2,
}

/// <summary>Art eines Abwesenheitsantrags.</summary>
public enum AbsenceType
{
    Vacation = 0,
    Sick = 1,
    HomeOffice = 2,
    Unpaid = 3,
    Other = 4,
}

/// <summary>Bearbeitungsstatus eines Abwesenheitsantrags.</summary>
public enum AbsenceStatus
{
    Requested = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
}
