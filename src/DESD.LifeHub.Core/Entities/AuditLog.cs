namespace DESD.LifeHub.Core.Entities;

/// <summary>
/// Revisionsprotokoll für sicherheitsrelevante Vorgänge (Anmeldung, Lizenz,
/// Rollen- und Berechtigungsänderungen). Enthält keine Geheimnisse.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    public Guid? UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public Guid? OrganizationId { get; set; }

    /// <summary>Kurzcode der Aktion, z. B. <c>license.activate</c>.</summary>
    public string Action { get; set; } = string.Empty;

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    /// <summary>Kurze, bereits anonymisierte Zusatzinformationen.</summary>
    public string? Details { get; set; }

    /// <summary>Erfolg oder Fehlschlag des Vorgangs.</summary>
    public bool Succeeded { get; set; } = true;

    public string? IpAddress { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
