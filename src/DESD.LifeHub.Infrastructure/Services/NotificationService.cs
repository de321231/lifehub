using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>In-App-Benachrichtigungen.</summary>
public interface INotificationService
{
    Task<IReadOnlyList<Notification>> ListAsync(Guid userId, int count = 20, CancellationToken ct = default);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default);

    Task<ServiceResult> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);

    Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default);

    Task<ServiceResult> DeleteAsync(Guid userId, Guid notificationId, CancellationToken ct = default);
}

public sealed class NotificationService : INotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Notification>> ListAsync(Guid userId, int count = 20, CancellationToken ct = default)
        => await _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(count, 1, 100))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default)
        => _db.Notifications.AsNoTracking().CountAsync(n => n.UserId == userId && !n.IsRead, ct);

    public async Task<ServiceResult> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        var affected = await _db.Notifications
            .Where(n => n.Id == notificationId && n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), ct)
            .ConfigureAwait(false);

        return affected > 0
            ? ServiceResult.Ok(notificationId)
            : ServiceResult.Fail("Die Benachrichtigung wurde nicht gefunden.");
    }

    public Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default)
        => _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), ct);

    public async Task<ServiceResult> DeleteAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        var affected = await _db.Notifications
            .Where(n => n.Id == notificationId && n.UserId == userId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        return affected > 0
            ? ServiceResult.Ok(notificationId)
            : ServiceResult.Fail("Die Benachrichtigung wurde nicht gefunden.");
    }
}
