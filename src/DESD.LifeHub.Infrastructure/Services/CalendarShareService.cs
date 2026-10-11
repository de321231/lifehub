using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>Kalenderfreigaben: Einblick in den persönlichen Kalender anderer Benutzer.</summary>
public interface ICalendarShareService
{
    /// <summary>Freigaben, die der Benutzer erteilt hat (eigener Kalender).</summary>
    Task<IReadOnlyList<CalendarShare>> GetOwnedSharesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Freigaben, die der Benutzer erhalten hat (fremde Kalender).</summary>
    Task<IReadOnlyList<CalendarShare>> GetReceivedSharesAsync(Guid userId, CancellationToken ct = default);

    Task<ServiceResult> CreateAsync(Guid userId, Guid sharedWithUserId, bool canEdit, CancellationToken ct = default);

    Task<ServiceResult> DeleteAsync(Guid userId, Guid shareId, CancellationToken ct = default);
}

public sealed class CalendarShareService : ICalendarShareService
{
    private readonly AppDbContext _db;
    private readonly IOrganizationAccessService _access;

    public CalendarShareService(AppDbContext db, IOrganizationAccessService access)
    {
        _db = db;
        _access = access;
    }

    public async Task<IReadOnlyList<CalendarShare>> GetOwnedSharesAsync(Guid userId, CancellationToken ct = default)
        => await _db.CalendarShares
            .AsNoTracking()
            .Include(s => s.SharedWith)
            .Where(s => s.OwnerUserId == userId)
            .OrderBy(s => s.SharedWith!.DisplayName)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<CalendarShare>> GetReceivedSharesAsync(Guid userId, CancellationToken ct = default)
        => await _db.CalendarShares
            .AsNoTracking()
            .Include(s => s.Owner)
            .Where(s => s.SharedWithUserId == userId)
            .OrderBy(s => s.Owner!.DisplayName)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<ServiceResult> CreateAsync(Guid userId, Guid sharedWithUserId, bool canEdit, CancellationToken ct = default)
    {
        if (sharedWithUserId == Guid.Empty || sharedWithUserId == userId)
        {
            return ServiceResult.Fail("Bitte wählen Sie eine andere Person aus.");
        }

        // Freigaben sind auf Personen beschränkt, mit denen eine gemeinsame Organisation besteht.
        var myOrgs = await _access.GetVisibleOrganizationIdsAsync(userId, ct).ConfigureAwait(false);
        var theirOrgs = await _access.GetVisibleOrganizationIdsAsync(sharedWithUserId, ct).ConfigureAwait(false);
        if (!myOrgs.Overlaps(theirOrgs))
        {
            return ServiceResult.Fail("Kalender können nur mit Personen aus Ihrer Organisation geteilt werden.");
        }

        var exists = await _db.CalendarShares
            .AnyAsync(s => s.OwnerUserId == userId && s.SharedWithUserId == sharedWithUserId, ct)
            .ConfigureAwait(false);

        if (exists)
        {
            return ServiceResult.Fail("Der Kalender ist für diese Person bereits freigegeben.");
        }

        _db.CalendarShares.Add(new CalendarShare
        {
            OwnerUserId = userId,
            SharedWithUserId = sharedWithUserId,
            CanEdit = canEdit,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteAsync(Guid userId, Guid shareId, CancellationToken ct = default)
    {
        var share = await _db.CalendarShares.FirstOrDefaultAsync(s => s.Id == shareId, ct).ConfigureAwait(false);
        if (share is null)
        {
            return ServiceResult.Fail("Die Freigabe wurde nicht gefunden.");
        }

        if (share.OwnerUserId != userId)
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diese Freigabe zu entfernen.");
        }

        _db.CalendarShares.Remove(share);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(shareId);
    }
}

/// <summary>
/// Gesetzliche Feiertage in Deutschland. Liefert die bundesweit einheitlichen
/// Feiertage; regionale Zusatzfeiertage sind hier bewusst nicht enthalten, da
/// Organisationen keine Region hinterlegen.
/// </summary>
public static class GermanHolidays
{
    public static IReadOnlyList<(DateOnly Date, string Name)> ForYear(int year)
    {
        return
        [
            (new DateOnly(year, 1, 1), "Neujahr"),
            (EasterSunday(year).AddDays(-2), "Karfreitag"),
            (EasterSunday(year).AddDays(1), "Ostermontag"),
            (new DateOnly(year, 5, 1), "Tag der Arbeit"),
            (EasterSunday(year).AddDays(39), "Christi Himmelfahrt"),
            (EasterSunday(year).AddDays(50), "Pfingstmontag"),
            (new DateOnly(year, 10, 3), "Tag der Deutschen Einheit"),
            (new DateOnly(year, 12, 25), "1. Weihnachtstag"),
            (new DateOnly(year, 12, 26), "2. Weihnachtstag"),
        ];
    }

    /// <summary>Ostersonntag nach der gaußschen Osterformel (gregorianischer Kalender).</summary>
    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;

        return new DateOnly(year, month, day);
    }
}
