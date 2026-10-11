using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DESD.LifeHub.Infrastructure.Services;

public sealed record UserProfileView
{
    public required Guid Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public string PreferredLanguage { get; init; } = "de";
    public string TimeZoneId { get; init; } = "Europe/Berlin";
    public ThemePreference ThemePreference { get; init; }
    public bool InAppNotificationsEnabled { get; init; } = true;
    public bool EmailNotificationsEnabled { get; init; }
    public bool IsActive { get; init; } = true;
    public bool IsAdmin { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
    public Guid? PersonalOrganizationId { get; init; }
}

public sealed record AdminUserRow
{
    public required Guid UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
}

public sealed record InvitationResult
{
    public required bool Succeeded { get; init; }
    public string? Error { get; init; }
    public Guid? InvitationId { get; init; }

    /// <summary>Klartext-Token – wird genau einmal angezeigt und nie gespeichert.</summary>
    public string? Token { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>
/// Konto- und Profilverwaltung sowie administrative Benutzerverwaltung.
/// Passwörter werden ausschliesslich über ASP.NET Core Identity (PBKDF2) verarbeitet
/// und niemals im Klartext gespeichert oder protokolliert.
/// </summary>
public interface IUserAccountService
{
    Task<UserProfileView?> GetProfileAsync(Guid userId, CancellationToken ct = default);

    Task<ServiceResult> UpdateProfileAsync(
        Guid userId,
        string displayName,
        string timeZoneId,
        string preferredLanguage,
        ThemePreference theme,
        bool inAppNotifications,
        bool emailNotifications,
        CancellationToken ct = default);

    Task<ServiceResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default);

    Task<IReadOnlyList<AdminUserRow>> ListUsersAsync(string? search = null, CancellationToken ct = default);

    Task<ServiceResult> SetActiveAsync(Guid actorUserId, Guid targetUserId, bool isActive, CancellationToken ct = default);

    Task<ServiceResult> SetAdminRoleAsync(Guid actorUserId, Guid targetUserId, bool isAdmin, CancellationToken ct = default);

    /// <summary>Administrator erzeugt einen Link zum Zurücksetzen des Passworts.</summary>
    Task<ServiceResultWithValue<string>> GeneratePasswordResetLinkAsync(Guid targetUserId, string baseUrl, CancellationToken ct = default);

    Task<InvitationResult> CreateInvitationAsync(
        Guid actorUserId,
        string email,
        string displayName,
        Guid? organizationId,
        int validDays = 7,
        CancellationToken ct = default);

    Task<IReadOnlyList<Invitation>> ListOpenInvitationsAsync(CancellationToken ct = default);

    Task<ServiceResult> RevokeInvitationAsync(Guid invitationId, CancellationToken ct = default);

    Task<ServiceResult> AcceptInvitationAsync(string token, string password, CancellationToken ct = default);
}

public sealed record ServiceResultWithValue<T>
{
    public required bool Succeeded { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
}

public sealed class UserAccountService : IUserAccountService
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IOrganizationService _organizations;
    private readonly IAuditService _audit;

    public UserAccountService(
        AppDbContext db,
        UserManager<ApplicationUser> users,
        IOrganizationService organizations,
        IAuditService audit)
    {
        _db = db;
        _users = users;
        _organizations = organizations;
        _audit = audit;
    }

    public async Task<UserProfileView?> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return null;
        }

        var roles = await _users.GetRolesAsync(user).ConfigureAwait(false);

        return new UserProfileView
        {
            Id = user.Id,
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? (user.Email ?? "Benutzer") : user.DisplayName,
            Email = user.Email ?? string.Empty,
            PreferredLanguage = user.PreferredLanguage,
            TimeZoneId = user.TimeZoneId,
            ThemePreference = user.ThemePreference,
            InAppNotificationsEnabled = user.InAppNotificationsEnabled,
            EmailNotificationsEnabled = user.EmailNotificationsEnabled,
            IsActive = user.IsActive,
            IsAdmin = roles.Contains(IdentityRoles.Admin),
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            PersonalOrganizationId = user.PersonalOrganizationId,
        };
    }

    public async Task<ServiceResult> UpdateProfileAsync(
        Guid userId,
        string displayName,
        string timeZoneId,
        string preferredLanguage,
        ThemePreference theme,
        bool inAppNotifications,
        bool emailNotifications,
        CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return ServiceResult.Fail("Benutzerkonto nicht gefunden.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return ServiceResult.Fail("Bitte geben Sie einen Namen an.", new Dictionary<string, string>
            {
                ["DisplayName"] = "Bitte geben Sie einen Namen an.",
            });
        }

        if (displayName.Length > 120)
        {
            return ServiceResult.Fail("Der Name ist zu lang.", new Dictionary<string, string>
            {
                ["DisplayName"] = "Der Name darf höchstens 120 Zeichen enthalten.",
            });
        }

        if (!TimeZoneCatalog.IsKnown(timeZoneId))
        {
            return ServiceResult.Fail("Die gewählte Zeitzone ist nicht bekannt.", new Dictionary<string, string>
            {
                ["TimeZoneId"] = "Bitte wählen Sie eine gültige Zeitzone.",
            });
        }

        user.DisplayName = displayName.Trim();
        user.TimeZoneId = timeZoneId;
        user.PreferredLanguage = string.IsNullOrWhiteSpace(preferredLanguage) ? "de" : preferredLanguage;
        user.ThemePreference = theme;
        user.InAppNotificationsEnabled = inAppNotifications;
        user.EmailNotificationsEnabled = emailNotifications;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(user.Id);
    }

    public async Task<ServiceResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            return ServiceResult.Fail("Benutzerkonto nicht gefunden.");
        }

        var result = await _users.ChangePasswordAsync(user, currentPassword, newPassword).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            var errors = result.Errors.ToDictionary(e => e.Code, e => TranslateIdentityError(e));
            return ServiceResult.Fail(errors.Values.FirstOrDefault() ?? "Das Passwort konnte nicht geändert werden.", errors);
        }

        await _audit.WriteAsync("user.password.changed", userId, cancellationToken: ct).ConfigureAwait(false);
        return ServiceResult.Ok(userId);
    }

    public async Task<IReadOnlyList<AdminUserRow>> ListUsersAsync(string? search = null, CancellationToken ct = default)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var pattern = $"%{term}%";
            query = query.Where(u =>
                (u.Email != null && EF.Functions.Like(u.Email, pattern)) ||
                EF.Functions.Like(u.DisplayName, pattern));
        }

        var users = await query.OrderBy(u => u.DisplayName).Take(500).ToListAsync(ct).ConfigureAwait(false);
        var userIds = users.Select(u => u.Id).ToList();

        var roleMap = await (
            from ur in _db.UserRoles
            join r in _db.Roles on ur.RoleId equals r.Id
            where userIds.Contains(ur.UserId)
            select new { ur.UserId, r.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var grouped = roleMap
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name ?? string.Empty).ToList());

        return users.Select(u => new AdminUserRow
        {
            UserId = u.Id,
            DisplayName = string.IsNullOrWhiteSpace(u.DisplayName) ? (u.Email ?? "–") : u.DisplayName,
            Email = u.Email ?? "–",
            IsActive = u.IsActive,
            Roles = grouped.TryGetValue(u.Id, out var r) ? r : [],
            CreatedAt = u.CreatedAt,
            LastLoginAt = u.LastLoginAt,
        }).ToList();
    }

    public async Task<ServiceResult> SetActiveAsync(Guid actorUserId, Guid targetUserId, bool isActive, CancellationToken ct = default)
    {
        if (actorUserId == targetUserId)
        {
            return ServiceResult.Fail("Sie können Ihr eigenes Konto nicht deaktivieren.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return ServiceResult.Fail("Benutzerkonto nicht gefunden.");
        }

        user.IsActive = isActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        // Deaktivierte Konten dürfen sich nicht mehr anmelden.
        if (!isActive)
        {
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.MaxValue;
            user.SecurityStamp = Guid.NewGuid().ToString();
        }
        else
        {
            user.LockoutEnd = null;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.WriteAsync(
            isActive ? "user.activated" : "user.deactivated",
            actorUserId,
            entityType: nameof(ApplicationUser),
            entityId: targetUserId.ToString(),
            cancellationToken: ct).ConfigureAwait(false);

        return ServiceResult.Ok(targetUserId);
    }

    public async Task<ServiceResult> SetAdminRoleAsync(Guid actorUserId, Guid targetUserId, bool isAdmin, CancellationToken ct = default)
    {
        if (actorUserId == targetUserId && !isAdmin)
        {
            return ServiceResult.Fail("Sie können sich selbst nicht die Administratorrolle entziehen.");
        }

        var user = await _users.FindByIdAsync(targetUserId.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            return ServiceResult.Fail("Benutzerkonto nicht gefunden.");
        }

        var isCurrentlyAdmin = await _users.IsInRoleAsync(user, IdentityRoles.Admin).ConfigureAwait(false);

        if (isAdmin && !isCurrentlyAdmin)
        {
            await _users.AddToRoleAsync(user, IdentityRoles.Admin).ConfigureAwait(false);
        }
        else if (!isAdmin && isCurrentlyAdmin)
        {
            if (await IsLastActiveAdminAsync(targetUserId, ct).ConfigureAwait(false))
            {
                return ServiceResult.Fail("Der letzte Administrator kann die Rolle nicht verlieren.");
            }

            await _users.RemoveFromRoleAsync(user, IdentityRoles.Admin).ConfigureAwait(false);
        }

        await _audit.WriteAsync(
            isAdmin ? "user.admin.granted" : "user.admin.revoked",
            actorUserId,
            entityType: nameof(ApplicationUser),
            entityId: targetUserId.ToString(),
            cancellationToken: ct).ConfigureAwait(false);

        return ServiceResult.Ok(targetUserId);
    }

    public async Task<ServiceResultWithValue<string>> GeneratePasswordResetLinkAsync(
        Guid targetUserId,
        string baseUrl,
        CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(targetUserId.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            return new ServiceResultWithValue<string> { Succeeded = false, Error = "Benutzerkonto nicht gefunden." };
        }

        var token = await _users.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
        var link = $"{baseUrl.TrimEnd('/')}/Account/ResetPassword?userId={user.Id}&code={Uri.EscapeDataString(token)}";

        await _audit.WriteAsync("user.password.resetlink-created", targetUserId,
            entityType: nameof(ApplicationUser), entityId: targetUserId.ToString(), cancellationToken: ct).ConfigureAwait(false);

        return new ServiceResultWithValue<string> { Succeeded = true, Value = link };
    }

    public async Task<InvitationResult> CreateInvitationAsync(
        Guid actorUserId,
        string email,
        string displayName,
        Guid? organizationId,
        int validDays = 7,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            return new InvitationResult { Succeeded = false, Error = "Bitte geben Sie eine gültige E-Mail-Adresse an." };
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return new InvitationResult { Succeeded = false, Error = "Bitte geben Sie einen Namen an." };
        }

        var normalized = email.Trim().ToLowerInvariant();

        var existingUser = await _db.Users.AnyAsync(u => u.NormalizedEmail == normalized.ToUpperInvariant(), ct).ConfigureAwait(false);
        if (existingUser)
        {
            return new InvitationResult
            {
                Succeeded = false,
                Error = "Für diese E-Mail-Adresse existiert bereits ein Benutzerkonto.",
            };
        }

        if (organizationId.HasValue)
        {
            var org = await _db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == organizationId.Value, ct).ConfigureAwait(false);
            if (org is null)
            {
                return new InvitationResult { Succeeded = false, Error = "Die Organisation wurde nicht gefunden." };
            }

            if (org.IsPersonal)
            {
                return new InvitationResult { Succeeded = false, Error = "In persönliche Bereiche kann nicht eingeladen werden." };
            }
        }

        var rawToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var invitation = new Invitation
        {
            Email = normalized,
            DisplayName = displayName.Trim(),
            TokenHash = HashToken(rawToken),
            OrganizationId = organizationId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(Math.Clamp(validDays, 1, 60)),
            InvitedByUserId = actorUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Invitations.Add(invitation);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.WriteAsync("user.invitation.created", actorUserId,
            organizationId: organizationId,
            entityType: nameof(Invitation),
            entityId: invitation.Id.ToString(),
            details: $"gültig bis {invitation.ExpiresAt:yyyy-MM-dd HH:mm} UTC",
            cancellationToken: ct).ConfigureAwait(false);

        return new InvitationResult
        {
            Succeeded = true,
            InvitationId = invitation.Id,
            Token = rawToken,
            ExpiresAt = invitation.ExpiresAt,
        };
    }

    public async Task<IReadOnlyList<Invitation>> ListOpenInvitationsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        return await _db.Invitations
            .AsNoTracking()
            .Include(i => i.Organization)
            .Where(i => i.AcceptedAt == null && i.ExpiresAt > now)
            .OrderBy(i => i.ExpiresAt)
            .Take(200)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ServiceResult> RevokeInvitationAsync(Guid invitationId, CancellationToken ct = default)
    {
        var affected = await _db.Invitations
            .Where(i => i.Id == invitationId && i.AcceptedAt == null)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        return affected > 0 ? ServiceResult.Ok(invitationId) : ServiceResult.Fail("Die Einladung wurde nicht gefunden.");
    }

    public async Task<ServiceResult> AcceptInvitationAsync(string token, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return ServiceResult.Fail("Der Einladungslink ist unvollständig.");
        }

        var hash = HashToken(token);
        var invitation = await _db.Invitations
            .FirstOrDefaultAsync(i => i.TokenHash == hash, ct)
            .ConfigureAwait(false);

        if (invitation is null)
        {
            return ServiceResult.Fail("Die Einladung ist ungültig.");
        }

        if (invitation.AcceptedAt is not null)
        {
            return ServiceResult.Fail("Diese Einladung wurde bereits verwendet.");
        }

        if (invitation.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return ServiceResult.Fail("Diese Einladung ist abgelaufen. Bitte fordern Sie eine neue an.");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = invitation.Email,
            Email = invitation.Email,
            EmailConfirmed = true,
            DisplayName = invitation.DisplayName,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var createResult = await _users.CreateAsync(user, password).ConfigureAwait(false);
        if (!createResult.Succeeded)
        {
            var errors = createResult.Errors.ToDictionary(e => e.Code, TranslateIdentityError);
            return ServiceResult.Fail(errors.Values.FirstOrDefault() ?? "Das Konto konnte nicht erstellt werden.", errors);
        }

        await _users.AddToRoleAsync(user, IdentityRoles.User).ConfigureAwait(false);

        await _organizations.EnsurePersonalOrganizationAsync(user.Id, user.DisplayName, user.Email, ct).ConfigureAwait(false);

        if (invitation.OrganizationId.HasValue)
        {
            _db.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = invitation.OrganizationId.Value,
                UserId = user.Id,
                Role = OrganizationRole.Member,
                JoinedAt = DateTimeOffset.UtcNow,
                InvitedByUserId = invitation.InvitedByUserId,
            });
        }

        invitation.AcceptedAt = DateTimeOffset.UtcNow;
        invitation.AcceptedByUserId = user.Id;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.WriteAsync("user.invitation.accepted", user.Id,
            organizationId: invitation.OrganizationId,
            entityType: nameof(ApplicationUser),
            entityId: user.Id.ToString(),
            cancellationToken: ct).ConfigureAwait(false);

        return ServiceResult.Ok(user.Id);
    }

    private async Task<bool> IsLastActiveAdminAsync(Guid excludedUserId, CancellationToken ct)
    {
        var adminRoleId = await _db.Roles
            .Where(r => r.Name == IdentityRoles.Admin)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (adminRoleId == default)
        {
            return false;
        }

        var adminIds = await _db.UserRoles
            .Where(ur => ur.RoleId == adminRoleId && ur.UserId != excludedUserId)
            .Select(ur => ur.UserId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return !await _db.Users.AnyAsync(u => adminIds.Contains(u.Id) && u.IsActive, ct).ConfigureAwait(false);
    }

    internal static string HashToken(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string TranslateIdentityError(IdentityError error) => error.Code switch
    {
        "PasswordTooShort" => "Das Passwort ist zu kurz (mindestens 12 Zeichen).",
        "PasswordRequiresNonAlphanumeric" => "Das Passwort benötigt mindestens ein Sonderzeichen.",
        "PasswordRequiresDigit" => "Das Passwort benötigt mindestens eine Zahl.",
        "PasswordRequiresLower" => "Das Passwort benötigt mindestens einen Kleinbuchstaben.",
        "PasswordRequiresUpper" => "Das Passwort benötigt mindestens einen Grossbuchstaben.",
        "PasswordMismatch" => "Das aktuelle Passwort ist nicht korrekt.",
        "DuplicateUserName" or "DuplicateEmail" => "Diese E-Mail-Adresse wird bereits verwendet.",
        "InvalidEmail" => "Die E-Mail-Adresse ist ungültig.",
        _ => error.Description,
    };
}
