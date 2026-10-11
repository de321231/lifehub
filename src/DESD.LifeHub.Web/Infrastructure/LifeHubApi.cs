using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using DESD.LifeHub.Infrastructure.Data;
using DESD.LifeHub.Infrastructure.Licensing;
using DESD.LifeHub.Core.Abstractions;

namespace DESD.LifeHub.Web.Infrastructure;

/// <summary>
/// Schlanke JSON-Schnittstelle für die interaktiven Teile der Oberfläche
/// (Board-Verschieben, Schnellaktionen, Benachrichtigungen).
///
/// Alle Endpunkte erfordern eine angemeldete Sitzung, eine gültige Lizenz
/// (über die Lizenzschranke) und einen gültigen CSRF-Token.
/// </summary>
public static class LifeHubApi
{
    public static IEndpointRouteBuilder MapLifeHubApi(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api").RequireAuthorization("Authenticated");

        api.MapGet("/license/status", async (ICurrentUser user, LicenseService license) =>
        {
            var view = await license.GetStatusAsync(user.RequireUserId());
            return Results.Ok(new
            {
                hasLicense = view.HasLicense,
                status = view.Status.ToString(),
                statusLabel = view.StatusLabel,
                maskedKey = view.MaskedKey,
                expiresAt = view.ExpiresAt,
                inGracePeriod = view.InGracePeriod,
                servingFromLocalGrace = view.ServingFromLocalGrace,
                lastValidatedAt = view.LastValidatedAt,
            });
        });

        api.MapPost("/tasks/{id:guid}/status", async (
            Guid id,
            TaskStatusRequest request,
            HttpContext http,
            ICurrentUser user,
            ITaskService tasks,
            IAntiforgery antiforgery) =>
        {
            if (!await IsAntiforgeryValidAsync(antiforgery, http))
            {
                return Results.Json(new { error = "invalid_csrf_token" }, statusCode: StatusCodes.Status400BadRequest);
            }

            if (!Enum.TryParse<TaskItemStatus>(request.Status, ignoreCase: true, out var status))
            {
                return Results.Json(new { error = "invalid_status" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await tasks.ChangeStatusAsync(user.RequireUserId(), id, status);

            return result.Succeeded
                ? Results.Ok(new { id, status = status.ToString() })
                : Results.Json(new { error = "not_allowed", message = result.Error }, statusCode: StatusCodes.Status403Forbidden);
        });

        api.MapPost("/tasks/quick", async (
            QuickTaskRequest request,
            HttpContext http,
            ICurrentUser user,
            ITaskService tasks,
            IAntiforgery antiforgery) =>
        {
            if (!await IsAntiforgeryValidAsync(antiforgery, http))
            {
                return Results.Json(new { error = "invalid_csrf_token" }, statusCode: StatusCodes.Status400BadRequest);
            }

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.Json(new { error = "title_required" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await tasks.CreateAsync(user.RequireUserId(), new TaskInput
            {
                Title = request.Title,
                Priority = request.Priority ?? TaskPriority.Normal,
                DueAt = request.DueAt,
                CreateReminder = request.DueAt.HasValue,
            });

            return result.Succeeded
                ? Results.Ok(new { id = result.Id })
                : Results.Json(new { error = "invalid", message = result.Error }, statusCode: StatusCodes.Status400BadRequest);
        });

        api.MapPost("/notifications/{id:guid}/read", async (
            Guid id,
            HttpContext http,
            ICurrentUser user,
            INotificationService notifications,
            IAntiforgery antiforgery) =>
        {
            if (!await IsAntiforgeryValidAsync(antiforgery, http))
            {
                return Results.Json(new { error = "invalid_csrf_token" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await notifications.MarkReadAsync(user.RequireUserId(), id);
            return result.Succeeded
                ? Results.Ok(new { id })
                : Results.Json(new { error = "not_found" }, statusCode: StatusCodes.Status404NotFound);
        });

        api.MapPost("/reminders/{id:guid}/dismiss", async (
            Guid id,
            HttpContext http,
            ICurrentUser user,
            IReminderService reminders,
            IAntiforgery antiforgery) =>
        {
            if (!await IsAntiforgeryValidAsync(antiforgery, http))
            {
                return Results.Json(new { error = "invalid_csrf_token" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await reminders.DismissAsync(user.RequireUserId(), id);
            return result.Succeeded
                ? Results.Ok(new { id })
                : Results.Json(new { error = "not_found", message = result.Error }, statusCode: StatusCodes.Status404NotFound);
        });

        api.MapPost("/settings/theme", async (
            ThemeRequest request,
            HttpContext http,
            ICurrentUser user,
            AppDbContext db,
            IAntiforgery antiforgery) =>
        {
            if (!await IsAntiforgeryValidAsync(antiforgery, http))
            {
                return Results.Json(new { error = "invalid_csrf_token" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var parsed = request.Theme?.ToLowerInvariant() switch
            {
                "light" => ThemePreference.Light,
                "dark" => ThemePreference.Dark,
                "system" => ThemePreference.System,
                _ => (ThemePreference?)null,
            };

            if (parsed is null)
            {
                return Results.Json(new { error = "invalid_theme" }, statusCode: StatusCodes.Status400BadRequest);
            }

            await db.Users
                .Where(u => u.Id == user.RequireUserId())
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ThemePreference, parsed.Value));

            return Results.Ok(new { theme = parsed.Value.ToString() });
        });

        api.MapPost("/calendar/events/{id:guid}/move", async (
            Guid id,
            MoveEventRequest request,
            HttpContext http,
            ICurrentUser user,
            ICalendarService calendar,
            IAntiforgery antiforgery) =>
        {
            if (!await IsAntiforgeryValidAsync(antiforgery, http))
            {
                return Results.Json(new { error = "invalid_csrf_token" }, statusCode: StatusCodes.Status400BadRequest);
            }

            if (request.StartsAt is null)
            {
                return Results.Json(new { error = "invalid_start" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await calendar.MoveAsync(user.RequireUserId(), id, request.StartsAt.Value);

            return result.Succeeded
                ? Results.Ok(new { id })
                : Results.Json(new { error = "not_allowed", message = result.Error }, statusCode: StatusCodes.Status403Forbidden);
        });

        return routes;
    }

    private static async Task<bool> IsAntiforgeryValidAsync(IAntiforgery antiforgery, HttpContext context)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    public sealed record TaskStatusRequest(string Status);

    public sealed record QuickTaskRequest(string Title, TaskPriority? Priority, DateTimeOffset? DueAt);

    public sealed record ThemeRequest(string? Theme);

    public sealed record MoveEventRequest(DateTimeOffset? StartsAt);
}
