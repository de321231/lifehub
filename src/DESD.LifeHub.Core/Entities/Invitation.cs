namespace DESD.LifeHub.Core.Entities;

/// <summary>
/// Einladung durch einen Administrator. Der Token wird nur gehasht gespeichert und
/// einmalig eingelöst.
/// </summary>
public class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = string.Empty;

    /// <summary>Anzeigename, den der Administrator vorgegeben hat.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>SHA-256-Hash des Einladungstokens.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Organisation, der der neue Benutzer beitreten soll.</summary>
    public Guid? OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public Guid? AcceptedByUserId { get; set; }

    public Guid InvitedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsOpen(DateTimeOffset now) => AcceptedAt is null && ExpiresAt > now;
}
