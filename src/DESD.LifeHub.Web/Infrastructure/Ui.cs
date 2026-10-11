using DESD.LifeHub.Core.Enums;

namespace DESD.LifeHub.Web.Infrastructure;

/// <summary>
/// Zentrale deutsche Bezeichnungen und CSS-Klassen für Enums, damit Seiten und
/// Layout dieselben Begriffe verwenden.
/// </summary>
public static class Ui
{
    public static string StatusText(TaskItemStatus status) => status switch
    {
        TaskItemStatus.Open => "Offen",
        TaskItemStatus.InProgress => "In Arbeit",
        TaskItemStatus.Blocked => "Blockiert",
        TaskItemStatus.Done => "Erledigt",
        TaskItemStatus.Cancelled => "Abgebrochen",
        _ => status.ToString(),
    };

    public static string StatusBadge(TaskItemStatus status) => status switch
    {
        TaskItemStatus.Open => "badge-info",
        TaskItemStatus.InProgress => "badge-brand",
        TaskItemStatus.Blocked => "badge-warning",
        TaskItemStatus.Done => "badge-success",
        TaskItemStatus.Cancelled => "badge-neutral",
        _ => "badge-neutral",
    };

    public static string PriorityText(TaskPriority priority) => priority switch
    {
        TaskPriority.Low => "Niedrig",
        TaskPriority.Normal => "Normal",
        TaskPriority.High => "Hoch",
        TaskPriority.Urgent => "Dringend",
        _ => priority.ToString(),
    };

    public static string PriorityDotClass(TaskPriority priority) => priority switch
    {
        TaskPriority.Low => "priority-low",
        TaskPriority.Normal => "priority-normal",
        TaskPriority.High => "priority-high",
        TaskPriority.Urgent => "priority-urgent",
        _ => "priority-normal",
    };

    public static string PriorityBadge(TaskPriority priority) => priority switch
    {
        TaskPriority.Low => "badge-neutral",
        TaskPriority.Normal => "badge-info",
        TaskPriority.High => "badge-warning",
        TaskPriority.Urgent => "badge-danger",
        _ => "badge-neutral",
    };

    public static string RoleText(OrganizationRole role) => role switch
    {
        OrganizationRole.Member => "Mitglied",
        OrganizationRole.Manager => "Manager",
        OrganizationRole.Owner => "Eigentümer",
        _ => role.ToString(),
    };

    public static string ReminderStatusText(ReminderStatus status) => status switch
    {
        ReminderStatus.Pending => "Ausstehend",
        ReminderStatus.Triggered => "Ausgelöst",
        ReminderStatus.Dismissed => "Erledigt",
        ReminderStatus.Cancelled => "Abgebrochen",
        ReminderStatus.Failed => "Fehlgeschlagen",
        _ => status.ToString(),
    };

    public static string ReminderStatusBadge(ReminderStatus status) => status switch
    {
        ReminderStatus.Pending => "badge-info",
        ReminderStatus.Triggered => "badge-brand",
        ReminderStatus.Dismissed => "badge-success",
        ReminderStatus.Cancelled => "badge-neutral",
        ReminderStatus.Failed => "badge-danger",
        _ => "badge-neutral",
    };

    public static string ThemeText(ThemePreference theme) => theme switch
    {
        ThemePreference.Light => "Hell",
        ThemePreference.Dark => "Dunkel",
        _ => "System",
    };

    public static string LicenseBadge(LicenseStatus status) => status switch
    {
        LicenseStatus.Valid => "badge-success",
        LicenseStatus.None => "badge-neutral",
        LicenseStatus.Unreachable or LicenseStatus.Malformed => "badge-warning",
        _ => "badge-danger",
    };

    public static string LicenseStatusText(LicenseStatus status) => status switch
    {
        LicenseStatus.None => "Keine Lizenz",
        LicenseStatus.Valid => "Gültig",
        LicenseStatus.NotFound => "Unbekannt",
        LicenseStatus.Expired => "Abgelaufen",
        LicenseStatus.Revoked => "Widerrufen",
        LicenseStatus.ProductMismatch => "Falsches Produkt",
        LicenseStatus.ActivationLimitReached => "Limit erreicht",
        LicenseStatus.FingerprintRequired => "Fingerprint erforderlich",
        LicenseStatus.Unreachable => "Server nicht erreichbar",
        LicenseStatus.Malformed => "Unlesbare Antwort",
        LicenseStatus.Invalid => "Ungültig",
        _ => status.ToString(),
    };

    /// <summary>Komma-getrennte Schlagwörter in einzelne Tags aufteilen.</summary>
    public static IEnumerable<string> SplitTags(string? tags)
        => string.IsNullOrWhiteSpace(tags)
            ? []
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ---------------------------------------------------------------- Kalender

    /// <summary>Erlaubte Farbkategorien der Terminpalette.</summary>
    public static readonly string[] EventColors =
        ["blue", "violet", "green", "teal", "amber", "rose", "slate"];

    public static string ColorLabel(string? key) => key switch
    {
        "violet" => "Violett",
        "green" => "Grün",
        "teal" => "Türkis",
        "amber" => "Gelb",
        "rose" => "Rosé",
        "slate" => "Grau",
        _ => "Blau",
    };

    /// <summary>Liefert die CSS-Klasse der Farbkategorie (Fallback: Blau).</summary>
    public static string ColorClass(string? key)
        => key is not null && EventColors.Contains(key) ? $"cat-{key}" : "cat-blue";

    /// <summary>Badge-Klasse passend zur Farbkategorie.</summary>
    public static string ColorBadge(string? key)
        => key is not null && EventColors.Contains(key) ? $"badge cat-{key}" : "badge cat-blue";

    public static string RecurrenceText(RecurrenceFrequency frequency, int interval) => frequency switch
    {
        RecurrenceFrequency.Daily => interval == 1 ? "Täglich" : $"Alle {interval} Tage",
        RecurrenceFrequency.Weekly => interval == 1 ? "Wöchentlich" : $"Alle {interval} Wochen",
        RecurrenceFrequency.Monthly => interval == 1 ? "Monatlich" : $"Alle {interval} Monate",
        RecurrenceFrequency.Yearly => interval == 1 ? "Jährlich" : $"Alle {interval} Jahre",
        _ => "Einmalig",
    };

    public static string ParticipantResponseText(ParticipantResponse response) => response switch
    {
        ParticipantResponse.Accepted => "Zugesagt",
        ParticipantResponse.Tentative => "Vielleicht",
        ParticipantResponse.Declined => "Abgesagt",
        _ => "Eingeladen",
    };

    public static string ParticipantResponseBadge(ParticipantResponse response) => response switch
    {
        ParticipantResponse.Accepted => "badge-success",
        ParticipantResponse.Tentative => "badge-warning",
        ParticipantResponse.Declined => "badge-danger",
        _ => "badge-neutral",
    };

    // ---------------------------------------------------------------- Abwesenheiten

    public static string AbsenceTypeText(AbsenceType type) => type switch
    {
        AbsenceType.Vacation => "Urlaub",
        AbsenceType.Sick => "Krankheit",
        AbsenceType.HomeOffice => "Homeoffice",
        AbsenceType.Unpaid => "Unbezahlt",
        _ => "Sonstiges",
    };

    public static string AbsenceStatusText(AbsenceStatus status) => status switch
    {
        AbsenceStatus.Approved => "Genehmigt",
        AbsenceStatus.Rejected => "Abgelehnt",
        AbsenceStatus.Cancelled => "Storniert",
        _ => "Beantragt",
    };

    public static string AbsenceStatusBadge(AbsenceStatus status) => status switch
    {
        AbsenceStatus.Approved => "badge-success",
        AbsenceStatus.Rejected => "badge-danger",
        AbsenceStatus.Cancelled => "badge-neutral",
        _ => "badge-warning",
    };

    // ---------------------------------------------------------------- Ankündigungen

    public static string AnnouncementPriorityText(AnnouncementPriority priority) => priority switch
    {
        AnnouncementPriority.Important => "Wichtig",
        AnnouncementPriority.Critical => "Dringend",
        _ => "Info",
    };

    public static string AnnouncementPriorityBadge(AnnouncementPriority priority) => priority switch
    {
        AnnouncementPriority.Important => "badge-warning",
        AnnouncementPriority.Critical => "badge-danger",
        _ => "badge-info",
    };

    /// <summary>Komma-getrennte Teilnehmer-IDs für die Anzeige zusammenfassen.</summary>
    public static string JoinNames(IEnumerable<string> names)
        => string.Join(", ", names);
}