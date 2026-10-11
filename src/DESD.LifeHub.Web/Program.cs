using System.Threading.RateLimiting;
using DESD.LifeHub.Infrastructure;
using DESD.LifeHub.Infrastructure.Data;
using DESD.LifeHub.Infrastructure.Options;
using DESD.LifeHub.Web.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- Konfiguration
// Umgebungsvariablen haben Vorrang vor appsettings-Dateien (Standardverhalten von
// ASP.NET Core). Damit lassen sich alle Geheimnisse aus Containern setzen.
builder.Configuration
    .AddEnvironmentVariables(prefix: "DESD_")
    .AddEnvironmentVariables();

// ---------------------------------------------------------------- Dienste
builder.Services.AddLifeHubInfrastructure(builder.Configuration);
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<AppShell>();

var securityOptions = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();

builder.Services.AddRazorPages(options =>
{
    // Alle Seiten ausser den öffentlichen erfordern eine Anmeldung.
    options.Conventions.AuthorizeFolder("/", "Authenticated");
    options.Conventions.AllowAnonymousToFolder("/Account");
    options.Conventions.AllowAnonymousToPage("/License/ActivateHelp");
    options.Conventions.AuthorizeFolder("/Admin", "AdminOnly");

    // Firmenverwaltung: Die Anmeldung ist öffentlich, die Verwaltung selbst prüft
    // zusätzlich die Rolle des Benutzers innerhalb der Firma (siehe PageModel).
    options.Conventions.AllowAnonymousToPage("/CompanyAdmin/Login");

    // Zusätzliche Route für das Systemverwaltungspanel.
    options.Conventions.AddPageRoute("/Admin/Index", "/adminpanel");
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser())
    .AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));

// Antiforgery: Token landen als HttpOnly-Cookie, zusätzlich führen Razor-Formulare
// den Verifizierungs-Token automatisch mit.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "desd.lifehub.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = securityOptions.RequireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("postgres", tags: ["db", "ready"]);

if (securityOptions.EnableRateLimiting)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

        options.AddPolicy("register", context => RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

        options.AddPolicy("license", context => RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

        options.OnRejected = async (context, token) =>
        {
            if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    """{"error":"rate_limited","message":"Zu viele Anfragen. Bitte versuchen Sie es später erneut."}""", token);
            }
        };
    });
}

// Betrieb hinter Reverse Proxy: X-Forwarded-* auswerten.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                               | ForwardedHeaders.XForwardedProto
                               | ForwardedHeaders.XForwardedHost;

    // In Container-/Proxy-Umgebungen sind die Proxyadressen nicht im Vorfeld
    // bekannt; die Konfiguration erfolgt bewusst über die Umgebungsvariable
    // ASPNETCORE_FORWARDEDHEADERS_ENABLED bzw. diesen Schalter in der Doku.
    var trustedProxies = builder.Configuration["Proxy:TrustedNetworks"];
    if (!string.IsNullOrWhiteSpace(trustedProxies))
    {
        foreach (var network in trustedProxies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Format "10.0.0.0/8" oder einzelne Adresse "192.168.1.10".
            var cidr = network.Contains('/') ? network : network + "/32";
            if (System.Net.IPNetwork.TryParse(cidr, out var parsed))
            {
                options.KnownIPNetworks.Add(parsed);
            }
        }
    }
    else
    {
        // Ohne explizite Konfiguration werden Proxy-Header nicht blind vertraut.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

var app = builder.Build();

// ---------------------------------------------------------------- Datenbank
try
{
    await DatabaseInitializer.InitializeAsync(app.Services);
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    logger.LogCritical(ex,
        "Die Datenbank konnte nicht initialisiert werden. Die Anwendung wird beendet, damit kein Betrieb " +
        "mit unbekanntem Schema stattfindet.");
    throw;
}

// ---------------------------------------------------------------- Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
}

app.UseForwardedHeaders();

if (securityOptions.RequireHttps)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseSecurityHeaders();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

if (securityOptions.EnableRateLimiting)
{
    app.UseRateLimiter();
}

// Lizenzprüfung: schützt alle geschützten Bereiche serverseitig.
app.UseMiddleware<LicenseGateMiddleware>();

app.MapRazorPages();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
}).AllowAnonymous();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
}).AllowAnonymous();

app.MapLifeHubApi();

app.Run();

static string PartitionKey(HttpContext context)
{
    // Rate-Limiting pro Anfragendem. Hinter einem Proxy greift UseForwardedHeaders.
    var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwarded))
    {
        return forwarded.Split(',')[0].Trim();
    }

    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

/// <summary>Kennzeichnet das Webassembly für Integrationstests.</summary>
public partial class Program;
