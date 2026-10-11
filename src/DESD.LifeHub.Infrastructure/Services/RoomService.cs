using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>Verwaltung der Besprechungsräume einer Organisation.</summary>
public interface IRoomService
{
    /// <summary>Aktive Räume der Organisation (nur für Mitglieder).</summary>
    Task<IReadOnlyList<Room>> GetRoomsAsync(Guid userId, Guid organizationId, CancellationToken ct = default);

    /// <summary>Räume aller Organisationen, die der Benutzer verwalten darf.</summary>
    Task<IReadOnlyList<Room>> GetManageableRoomsAsync(Guid userId, CancellationToken ct = default);

    Task<ServiceResult> CreateAsync(Guid userId, RoomInput input, CancellationToken ct = default);

    Task<ServiceResult> UpdateAsync(Guid userId, Guid roomId, RoomInput input, CancellationToken ct = default);

    Task<ServiceResult> DeleteAsync(Guid userId, Guid roomId, CancellationToken ct = default);
}

public sealed class RoomService : IRoomService
{
    private readonly AppDbContext _db;
    private readonly IOrganizationAccessService _access;

    public RoomService(AppDbContext db, IOrganizationAccessService access)
    {
        _db = db;
        _access = access;
    }

    public async Task<IReadOnlyList<Room>> GetRoomsAsync(Guid userId, Guid organizationId, CancellationToken ct = default)
    {
        if (!await _access.IsMemberAsync(userId, organizationId, ct).ConfigureAwait(false))
        {
            return [];
        }

        return await _db.Rooms
            .AsNoTracking()
            .Where(r => r.OrganizationId == organizationId && r.IsActive)
            .OrderBy(r => r.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Room>> GetManageableRoomsAsync(Guid userId, CancellationToken ct = default)
    {
        var orgs = await _access.GetSelectableOrganizationsAsync(userId, ct).ConfigureAwait(false);
        var manageable = new List<Room>();

        foreach (var org in orgs)
        {
            if (await _access.CanManageOrganizationAsync(userId, org.Id, ct).ConfigureAwait(false))
            {
                manageable.AddRange(await _db.Rooms
                    .AsNoTracking()
                    .Where(r => r.OrganizationId == org.Id)
                    .OrderBy(r => r.Name)
                    .ToListAsync(ct)
                    .ConfigureAwait(false));
            }
        }

        return manageable;
    }

    public async Task<ServiceResult> CreateAsync(Guid userId, RoomInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return ServiceResult.Fail("Bitte geben Sie einen Namen an.");
        }

        if (!await _access.CanManageOrganizationAsync(userId, input.OrganizationId, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, Räume dieser Organisation zu verwalten.");
        }

        var room = new Room
        {
            OrganizationId = input.OrganizationId,
            Name = input.Name.Trim(),
            Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim(),
            Capacity = input.Capacity,
            ColorCategory = string.IsNullOrWhiteSpace(input.ColorCategory) ? null : input.ColorCategory.Trim(),
            IsActive = input.IsActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Rooms.Add(room);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(room.Id);
    }

    public async Task<ServiceResult> UpdateAsync(Guid userId, Guid roomId, RoomInput input, CancellationToken ct = default)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct).ConfigureAwait(false);
        if (room is null)
        {
            return ServiceResult.Fail("Der Raum wurde nicht gefunden.");
        }

        if (!await _access.CanManageOrganizationAsync(userId, room.OrganizationId, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diesen Raum zu bearbeiten.");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return ServiceResult.Fail("Bitte geben Sie einen Namen an.");
        }

        room.Name = input.Name.Trim();
        room.Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim();
        room.Capacity = input.Capacity;
        room.ColorCategory = string.IsNullOrWhiteSpace(input.ColorCategory) ? null : input.ColorCategory.Trim();
        room.IsActive = input.IsActive;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(room.Id);
    }

    public async Task<ServiceResult> DeleteAsync(Guid userId, Guid roomId, CancellationToken ct = default)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct).ConfigureAwait(false);
        if (room is null)
        {
            return ServiceResult.Fail("Der Raum wurde nicht gefunden.");
        }

        if (!await _access.CanManageOrganizationAsync(userId, room.OrganizationId, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diesen Raum zu löschen.");
        }

        _db.Rooms.Remove(room);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(roomId);
    }
}
