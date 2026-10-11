using DESD.LifeHub.Core.Abstractions;
using DESD.LifeHub.Infrastructure.Background;
using DESD.LifeHub.Infrastructure.Data;
using DESD.LifeHub.Infrastructure.Licensing;
using DESD.LifeHub.Infrastructure.Options;
using DESD.LifeHub.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DESD.LifeHub.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registriert Datenbank, Identity, Lizenzierung, Fachservices und den
    /// Erinnerungsdienst. Die Weboberfläche ergänzt nur noch UI-spezifische Dienste.
    /// </summary>
    public static IServiceCollection AddLifeHubInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OpenLicensdOptions>(configuration.GetSection(OpenLicensdOptions.SectionName));
        services.Configure<LicenseKeyProtectionOptions>(configuration.GetSection(LicenseKeyProtectionOptions.SectionName));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<BootstrapOptions>(configuration.GetSection(BootstrapOptions.SectionName));
        services.Configure<ReminderOptions>(configuration.GetSection(ReminderOptions.SectionName));
        services.Configure<MailOptions>(configuration.GetSection(MailOptions.SectionName));
        services.Configure<AuditOptions>(configuration.GetSection(AuditOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        var connectionString = BuildConnectionString(configuration);

        services.AddDbContext<AppDbContext>(options =>
        {
            var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();

            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);

                if (databaseOptions.RetryOnFailure)
                {
                    npgsql.EnableRetryOnFailure(
                        maxRetryCount: Math.Max(1, databaseOptions.MaxRetries),
                        maxRetryDelay: TimeSpan.FromSeconds(Math.Max(1, databaseOptions.RetryDelaySeconds)),
                        errorCodesToAdd: null);
                }
            });

            if (databaseOptions.LogSql)
            {
                options.EnableSensitiveDataLogging(false);
                options.EnableDetailedErrors(true);
            }
        });

        services.AddMemoryCache();

        services
            .AddIdentity<Core.Entities.ApplicationUser, IdentityRole<Guid>>(identity =>
            {
                identity.Password.RequiredLength = 12;
                identity.Password.RequireDigit = true;
                identity.Password.RequireLowercase = true;
                identity.Password.RequireUppercase = true;
                identity.Password.RequireNonAlphanumeric = true;
                identity.Password.RequiredUniqueChars = 5;

                identity.Lockout.AllowedForNewUsers = true;
                identity.Lockout.MaxFailedAccessAttempts = 5;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                identity.User.RequireUniqueEmail = true;
                identity.SignIn.RequireConfirmedAccount = false;
                identity.SignIn.RequireConfirmedEmail = false;

                // Benutzernamen/E-Mails werden nicht im Klartext verglichen, sondern
                // über die normalisierte Form von Identity geprüft.
                identity.Stores.MaxLengthForKeys = 128;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        var security = configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();

        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.Cookie.Name = "desd.lifehub.auth";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            cookie.Cookie.SecurePolicy = security.RequireHttps
                ? Microsoft.AspNetCore.Http.CookieSecurePolicy.Always
                : Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
            cookie.SlidingExpiration = true;
            cookie.ExpireTimeSpan = TimeSpan.FromMinutes(Math.Max(15, security.SessionTimeoutMinutes));
            cookie.LoginPath = "/Account/Login";
            cookie.LogoutPath = "/Account/Logout";
            cookie.AccessDeniedPath = "/Account/AccessDenied";
            cookie.ReturnUrlParameter = "returnUrl";
        });

        services.Configure<IdentityOptions>(options => options.Lockout.MaxFailedAccessAttempts = 5);

        // Lizenzierung
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ILicenseKeyProtector, AesLicenseKeyProtector>();

        services.AddHttpClient<ILicenseValidator, OpenLicensdClient>(client =>
        {
            var licenseOptions = configuration.GetSection(OpenLicensdOptions.SectionName).Get<OpenLicensdOptions>() ?? new OpenLicensdOptions();

            if (Uri.TryCreate(licenseOptions.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                client.BaseAddress = baseUri;
            }

            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(licenseOptions.TimeoutSeconds, 1, 120) + 5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DESD-LifeHub/1.0");
        });

        // Geschützte Admin-API (Bearer-Token). Nur serverseitig, Token verlässt den
        // Prozess nicht. Ohne konfiguriertes Token bleibt die Kontingent-Erzeugung aus.
        // Das Bearer-Header setzt der Client pro Anfrage aus den Optionen.
        services.AddHttpClient<IOpenLicensdAdmin, OpenLicensdAdminClient>((serviceProvider, client) =>
        {
            var licenseOptions = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenLicensdOptions>>().Value;

            if (Uri.TryCreate(licenseOptions.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                client.BaseAddress = baseUri;
            }

            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(licenseOptions.TimeoutSeconds, 1, 120) + 5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DESD-LifeHub/1.0");
        });

        services.AddScoped<LicenseService>();
        services.AddScoped<ILicenseAccessService>(sp => sp.GetRequiredService<LicenseService>());
        services.AddScoped<ICompanyLicenseService, CompanyLicenseService>();

        // Fachservices
        services.AddSingleton<IEmailSender, LoggingEmailSender>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<IOrganizationAccessService, OrganizationAccessService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IReminderService, ReminderService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<ITaskCollaborationService, TaskCollaborationService>();
        services.AddScoped<INoteService, NoteService>();
        services.AddScoped<ICalendarService, CalendarService>();
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<ICalendarShareService, CalendarShareService>();
        services.AddScoped<IAnnouncementService, AnnouncementService>();
        services.AddScoped<IAbsenceService, AbsenceService>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IUserAccountService, UserAccountService>();

        if (configuration.GetSection(ReminderOptions.SectionName).Get<ReminderOptions>()?.Enabled ?? true)
        {
            services.AddHostedService<ReminderBackgroundService>();
        }

        return services;
    }

    /// <summary>
    /// Baut die Verbindungszeichenfolge. Bevorzugt wird <c>ConnectionStrings:Postgres</c>;
    /// andernfalls werden Einzelwerte aus Abschnitt <c>Database</c> verwendet.
    /// </summary>
    public static string BuildConnectionString(IConfiguration configuration)
    {
        var explicitConnection = configuration.GetConnectionString("Postgres");
        if (!string.IsNullOrWhiteSpace(explicitConnection))
        {
            return explicitConnection;
        }

        var host = configuration["Database:Host"] ?? "localhost";
        var port = configuration["Database:Port"] ?? "5432";
        var name = configuration["Database:Name"] ?? "desd_lifehub";
        var user = configuration["Database:User"] ?? "desd_lifehub";
        var password = configuration["Database:Password"] ?? string.Empty;

        return $"Host={host};Port={port};Database={name};Username={user};Password={password};" +
               "Pooling=true;Minimum Pool Size=0;Maximum Pool Size=50;Include Error Detail=false";
    }
}
