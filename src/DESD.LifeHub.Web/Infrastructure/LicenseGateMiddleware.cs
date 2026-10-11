using DESD.LifeHub.Core.Abstractions;
using DESD.LifeHub.Core.Enums;

namespace DESD.LifeHub.Web.Infrastructure;

/// <summary>
/// Serverseitige Lizenzschranke. Jede Anfrage an einen geschützten Bereich wird
/// gegen den zentralen Lizenzservice geprüft. Nicht prüfbare oder ungültige
/// Lizenzen führen zu einer Weiterleitung auf die Lizenzseite.
///
/// Die Prüfung erfolgt damit auf dem Server und lässt sich vom Browser weder
/// umgangen noch manipulieren.
/// </summary>
public sealed class LicenseGateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<LicenseGateMiddleware> _logger;

    /// <summary>
    /// Pfade, die auch ohne gültige Lizenz erreichbar bleiben müssen – sonst wäre
    /// die Aktivierung unmöglich.
    /// </summary>
    private static readonly string[] PublicPrefixes =
    [
        "/Account",
        "/License",
        "/Error",
        "/health",
        "/css",
        "/js",
        "/images",
        "/favicon",
        "/lib",
        "/_framework",
        "/_blazor",
    ];

    private static readonly string[] PublicExactPaths =
    [
        "/",
    ];

    /// <summary>
    /// Erste Pfadsegmente, die niemals eine Firmenverwaltung sind. Verhindert, dass
    /// z. B. <c>/Admin/…</c> versehentlich als Firmenbereich gelten.
    /// </summary>
    private static readonly string[] ReservedFirstSegments =
    [
        "Admin", "Account", "License", "Error", "api", "health", "css", "js",
        "images", "favicon", "lib", "_framework", "_blazor",
    ];

    public LicenseGateMiddleware(RequestDelegate next, ILogger<LicenseGateMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, ILicenseAccessService licenseAccess)
    {
        if (!RequiresLicense(context) || !currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var decision = await licenseAccess.EvaluateAccessAsync(userId, context.RequestAborted).ConfigureAwait(false);

        if (decision.Allowed)
        {
            if (decision.ServingFromLocalGrace)
            {
                _logger.LogWarning(
                    "Zugriff für Benutzer {UserId} läuft über die Kulanzfrist ({Message}).", userId, decision.Message);
            }

            // Entscheidung für Layout-Kennzeichnung bereitstellen.
            context.Items[LicenseContextKeys.Decision] = decision;
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Bei API-/AJAX-Aufrufen keine HTML-Weiterleitung, sondern klarer Statuscode.
        if (IsApiRequest(context))
        {
            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "license_required",
                status = decision.Status.ToString(),
                message = decision.Message,
            }, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        if (decision.Status is LicenseStatus.None)
        {
            context.Response.Redirect("/License?reason=none");
            return;
        }

        context.Response.Redirect("/License?reason=invalid");
    }

    private static bool RequiresLicense(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        if (PublicExactPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var prefix in PublicPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Die Systemverwaltung (<c>/adminpanel</c>, <c>/Admin/…</c>) und die
        // Firmenverwaltung (<c>/{FIRMEN-ID}/admin…</c>) sind von der persönlichen
        // Lizenzschranke ausgenommen. Nur so kann ein (System- oder Firmen-)Admin
        // Lizenzen verwalten, auch wenn sein eigenes Konto (noch) keine gültige
        // Lizenz besitzt – andernfalls könnte sich das System selbst aussperren.
        // Der Zugriff bleibt vollständig über Anmeldung und Rollenprüfung geschützt.
        return !IsOperatorAdminPath(path) && !IsCompanyAdminPath(path);
    }

    /// <summary>
    /// Systemverwaltung. Bleibt ohne gültige Lizenz erreichbar, damit ein System-
    /// Administrator Lizenzen (auch die eigene) vergeben und erneuern kann.
    /// </summary>
    private static bool IsOperatorAdminPath(string path)
    {
        if (path.Equals("/adminpanel", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/adminpanel/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 1
               && segments[0].Equals("Admin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompanyAdminPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            return false;
        }

        if (ReservedFirstSegments.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return segments[1].Equals("adminlogin", StringComparison.OrdinalIgnoreCase)
               || segments[1].Equals("admin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsApiRequest(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            return true;
        }

        var accept = context.Request.Headers.Accept.ToString();
        return accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)
               && !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }
}

public static class LicenseContextKeys
{
    public const string Decision = "desd.lifehub.license.decision";
}

/// <summary>Setzt Sicherheits-Header für alle Antworten.</summary>
public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;

                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["X-Permitted-Cross-Domain-Policies"] = "none";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

                // Strikte, aber für die eigene Oberfläche ausreichende CSP.
                // Inline-Styles sind für die Kalenderpositionierung nötig.
                headers["Content-Security-Policy"] =
                    "default-src 'self'; " +
                    "img-src 'self' data:; " +
                    "style-src 'self' 'unsafe-inline'; " +
                    "script-src 'self'; " +
                    "font-src 'self' data:; " +
                    "connect-src 'self'; " +
                    "form-action 'self'; " +
                    "frame-ancestors 'none'; " +
                    "base-uri 'self'; " +
                    "object-src 'none'";

                return Task.CompletedTask;
            });

            await next().ConfigureAwait(false);
        });
    }
}
