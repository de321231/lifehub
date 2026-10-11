using System.Security.Claims;
using DESD.LifeHub.Core.Entities;
using Microsoft.AspNetCore.Identity;

namespace DESD.LifeHub.Web.Infrastructure;

/// <summary>Zugriff auf den angemeldeten Benutzer.</summary>
public interface ICurrentUser
{
    /// <summary>Benutzerkennung oder null, wenn nicht angemeldet.</summary>
    Guid? UserId { get; }

    bool IsAuthenticated { get; }

    bool IsAdmin { get; }

    string? Email { get; }

    string DisplayName { get; }

    /// <summary>Benutzerkennung oder eine Ausnahme – für Bereiche mit Anmeldepflicht.</summary>
    Guid RequireUserId();
}

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var value = Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public bool IsAdmin => Principal?.IsInRole("Admin") == true;

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public string DisplayName
    {
        get
        {
            var name = Principal?.FindFirstValue("display_name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return Principal?.FindFirstValue(ClaimTypes.Name) ?? Email ?? "Benutzer";
        }
    }

    public Guid RequireUserId()
        => UserId ?? throw new InvalidOperationException("Es ist kein Benutzer angemeldet.");
}

/// <summary>
/// Zentrale, wiederverwendbare Daten der Seitenvorlage: Benutzer, Theme, Zeitzone
/// und die Anzahl ungelesener Benachrichtigungen/offener Erinnerungen.
/// </summary>
public sealed class AppShell
{
    private readonly ICurrentUser _currentUser;
    private readonly DESD.LifeHub.Infrastructure.Services.INotificationService _notifications;
    private readonly DESD.LifeHub.Infrastructure.Services.IReminderService _reminders;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DESD.LifeHub.Core.Abstractions.ILicenseAccessService _licenseAccess;

    private ApplicationUser? _user;
    private bool _loaded;

    public AppShell(
        ICurrentUser currentUser,
        DESD.LifeHub.Infrastructure.Services.INotificationService notifications,
        DESD.LifeHub.Infrastructure.Services.IReminderService reminders,
        UserManager<ApplicationUser> userManager,
        DESD.LifeHub.Core.Abstractions.ILicenseAccessService licenseAccess)
    {
        _currentUser = currentUser;
        _notifications = notifications;
        _reminders = reminders;
        _userManager = userManager;
        _licenseAccess = licenseAccess;
    }

    public async Task<ApplicationUser?> GetUserAsync(CancellationToken ct = default)
    {
        if (_loaded)
        {
            return _user;
        }

        _loaded = true;
        if (_currentUser.UserId is { } id)
        {
            _user = await _userManager.FindByIdAsync(id.ToString()).ConfigureAwait(false);
        }

        return _user;
    }

    public string DisplayName => _currentUser.DisplayName;

    public string TimeZoneId => _user?.TimeZoneId ?? "Europe/Berlin";

    public DESD.LifeHub.Core.Enums.ThemePreference Theme =>
        _user?.ThemePreference ?? DESD.LifeHub.Core.Enums.ThemePreference.System;

    public int UnreadNotificationCount { get; private set; }

    public int DueReminderCount { get; private set; }

    public bool HasValidLicense { get; private set; }

    public bool ServingFromGrace { get; private set; }

    public string? LicenseMessage { get; private set; }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await GetUserAsync(ct).ConfigureAwait(false);

        if (_currentUser.UserId is not { } userId)
        {
            return;
        }

        UnreadNotificationCount = await _notifications.CountUnreadAsync(userId, ct).ConfigureAwait(false);
        DueReminderCount = await _reminders.CountPendingAsync(userId, ct).ConfigureAwait(false);

        var decision = await _licenseAccess.EvaluateAccessAsync(userId, ct).ConfigureAwait(false);
        HasValidLicense = decision.Allowed;
        ServingFromGrace = decision.ServingFromLocalGrace;
        LicenseMessage = decision.Message;
    }

    /// <summary>Wandelt UTC in die Zeitzone des Benutzers um.</summary>
    public DateTimeOffset ToLocal(DateTimeOffset utc)
        => DESD.LifeHub.Infrastructure.Services.TimeZoneCatalog.ToLocal(utc, TimeZoneId);

    public string FormatLocal(DateTimeOffset utc, string format = "dd.MM.yyyy HH:mm")
        => ToLocal(utc).ToString(format, System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
}
