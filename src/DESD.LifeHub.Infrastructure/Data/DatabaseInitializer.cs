using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Options;
using DESD.LifeHub.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DESD.LifeHub.Infrastructure.Data;

/// <summary>
/// Kontrollierte Datenbankinitialisierung: wendet Migrationen an (nur wenn
/// ausdrücklich konfiguriert) und legt Rollen sowie optional den ersten
/// Administrator an.
///
/// Es gibt bewusst keine hartcodierten Standardzugangsdaten. Der erste
/// Administrator wird ausschliesslich über Umgebungsvariablen angelegt.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("DESD.LifeHub.Database");
        var db = provider.GetRequiredService<AppDbContext>();
        var databaseOptions = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var bootstrap = provider.GetRequiredService<IOptions<BootstrapOptions>>().Value;

        if (databaseOptions.MigrateOnStartup)
        {
            await ApplyMigrationsWithRetryAsync(db, databaseOptions, logger, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            logger.LogInformation(
                "Database:MigrateOnStartup=false – es werden keine Migrationen angewendet. " +
                "Führen Sie die Migrationen kontrolliert aus (dotnet ef database update oder Migrationsprofil).");
        }

        await EnsureRolesAsync(provider, logger, cancellationToken).ConfigureAwait(false);
        await EnsureBootstrapAdminAsync(provider, bootstrap, logger, cancellationToken).ConfigureAwait(false);
        await EnsurePersonalOrganizationsAsync(provider, logger, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyMigrationsWithRetryAsync(
        AppDbContext db,
        DatabaseOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var attempts = options.RetryOnFailure ? Math.Max(1, options.MaxRetries) : 1;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();

                if (pending.Count == 0)
                {
                    logger.LogInformation("Datenbankschema ist aktuell (keine ausstehenden Migrationen).");
                    return;
                }

                logger.LogInformation("Wende {Count} ausstehende Migration(en) an.", pending.Count);
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Datenbankmigration abgeschlossen.");
                return;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                var delay = TimeSpan.FromSeconds(options.RetryDelaySeconds);
                logger.LogWarning(
                    ex,
                    "Datenbankmigration fehlgeschlagen (Versuch {Attempt}/{Attempts}). Neuer Versuch in {Delay}s.",
                    attempt, attempts, delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        // Letzter Versuch ohne Abfangen, damit der Fehler sichtbar wird.
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureRolesAsync(IServiceProvider provider, ILogger logger, CancellationToken cancellationToken)
    {
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var role in new[] { IdentityRoles.Admin, IdentityRoles.User })
        {
            if (await roleManager.RoleExistsAsync(role).ConfigureAwait(false))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role)).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                logger.LogError("Rolle {Role} konnte nicht angelegt werden: {Errors}",
                    role, string.Join(", ", result.Errors.Select(e => e.Code)));
            }
            else
            {
                logger.LogInformation("Rolle {Role} angelegt.", role);
            }
        }
    }

    private static async Task EnsureBootstrapAdminAsync(
        IServiceProvider provider,
        BootstrapOptions bootstrap,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bootstrap.AdminEmail) || string.IsNullOrWhiteSpace(bootstrap.AdminPassword))
        {
            logger.LogWarning(
                "Kein Erst-Administrator konfiguriert (Bootstrap:AdminEmail/Bootstrap:AdminPassword fehlen). " +
                "Es wurde kein Administratorkonto und kein Standardpasswort angelegt. " +
                "Setzen Sie BOOTSTRAP_ADMIN_EMAIL und BOOTSTRAP_ADMIN_PASSWORD und starten Sie erneut, " +
                "oder legen Sie Benutzer per Einladung an.");
            return;
        }

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = bootstrap.AdminEmail.Trim();
        var displayName = string.IsNullOrWhiteSpace(bootstrap.AdminDisplayName) ? email : bootstrap.AdminDisplayName.Trim();

        var existing = await userManager.FindByEmailAsync(email).ConfigureAwait(false);

        if (existing is not null)
        {
            if (!await userManager.IsInRoleAsync(existing, IdentityRoles.Admin).ConfigureAwait(false))
            {
                await userManager.AddToRoleAsync(existing, IdentityRoles.Admin).ConfigureAwait(false);
                logger.LogInformation("Vorhandenem Benutzer {Email} wurde die Administratorrolle zugewiesen.", email);
            }

            await EnsurePersonalOrganizationForAsync(provider, existing, logger, cancellationToken).ConfigureAwait(false);
            return;
        }

        var admin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var result = await userManager.CreateAsync(admin, bootstrap.AdminPassword).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            // Passwort wird niemals protokolliert – nur die Regelverstösse.
            logger.LogError(
                "Erst-Administrator konnte nicht angelegt werden. Bitte Passwortregeln prüfen (mind. 12 Zeichen, " +
                "Gross-/Kleinbuchstaben, Ziffer, Sonderzeichen). Fehler: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Code)));
            return;
        }

        await userManager.AddToRoleAsync(admin, IdentityRoles.Admin).ConfigureAwait(false);
        await userManager.AddToRoleAsync(admin, IdentityRoles.User).ConfigureAwait(false);

        logger.LogInformation("Erst-Administrator {Email} wurde angelegt.", email);

        await EnsurePersonalOrganizationForAsync(provider, admin, logger, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsurePersonalOrganizationsAsync(IServiceProvider provider, ILogger logger, CancellationToken cancellationToken)
    {
        var db = provider.GetRequiredService<AppDbContext>();

        var users = await db.Users
            .Where(u => u.PersonalOrganizationId == null)
            .Take(500)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var user in users)
        {
            await EnsurePersonalOrganizationForAsync(provider, user, logger, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsurePersonalOrganizationForAsync(
        IServiceProvider provider,
        ApplicationUser user,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var organizations = provider.GetRequiredService<IOrganizationService>();

        try
        {
            var orgId = await organizations
                .EnsurePersonalOrganizationAsync(user.Id, user.DisplayName, user.Email, cancellationToken)
                .ConfigureAwait(false);

            logger.LogDebug("Persönlicher Bereich für {Email} vorhanden ({OrgId}).", user.Email, orgId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Persönlicher Bereich für Benutzer {UserId} konnte nicht angelegt werden.", user.Id);
        }
    }
}
