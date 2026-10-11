using System.ComponentModel.DataAnnotations;
using DESD.LifeHub.Core.Enums;

namespace DESD.LifeHub.Web.Pages.Calendar;

/// <summary>Auswahloption für Teilnehmer inklusive zugehöriger Organisation.</summary>
public sealed record MemberOption(Guid UserId, string DisplayName, Guid OrganizationId);

/// <summary>
/// Gemeinsames Eingabemodell für das Anlegen und Bearbeiten von Terminen
/// (inklusive Farbe, Raum, Wiederholung und Teilnehmern).
/// </summary>
public sealed class EventFormModel
{
    [Required(ErrorMessage = "Bitte geben Sie einen Titel an.")]
    [StringLength(200, ErrorMessage = "Der Titel darf höchstens 200 Zeichen enthalten.")]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Location { get; set; }

    /// <summary>Beginn in lokaler Benutzerzeit.</summary>
    [Required(ErrorMessage = "Bitte geben Sie einen Beginn an.")]
    public DateTime? StartsAt { get; set; }

    /// <summary>Ende in lokaler Benutzerzeit.</summary>
    [Required(ErrorMessage = "Bitte geben Sie ein Ende an.")]
    public DateTime? EndsAt { get; set; }

    public bool IsAllDay { get; set; }

    /// <summary>Farbkategorie-Schlüssel der Palette.</summary>
    public string? ColorCategory { get; set; }

    public Guid? RoomId { get; set; }

    public RecurrenceFrequency RecurrenceFrequency { get; set; } = RecurrenceFrequency.None;

    [Range(1, 52, ErrorMessage = "Der Abstand muss zwischen 1 und 52 liegen.")]
    public int RecurrenceInterval { get; set; } = 1;

    /// <summary>Serienende als lokales Datum (interpretiert als Tagesende).</summary>
    public DateTime? RecurrenceUntilDate { get; set; }

    public Guid? OrganizationId { get; set; }

    public bool CreateReminder { get; set; }

    public int? ReminderLeadMinutes { get; set; }

    public List<Guid> ParticipantUserIds { get; set; } = [];
}
