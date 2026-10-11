using DESD.LifeHub.Core.Entities;
using DESD.LifeHub.Core.Enums;
using DESD.LifeHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>
/// Abwesenheitsplaner (Urlaub, Krankheit, Homeoffice). Mitarbeiter beantragen,
/// Manager genehmigen oder lehnen ab. Sichtbarkeit: eigene Anträge immer, fremde
/// Anträge einer Organisation nur für Verwaltende oder – in genehmigtem Zustand –
/// für alle Mitglieder (Teamübersicht).
/// </summary>
public interface IAbsenceService
{
    Task<IReadOnlyList<AbsenceRequest>> GetForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Anträge einer Organisation im Zeitraum (Teamübersicht).</summary>
    Task<IReadOnlyList<AbsenceRequest>> GetForOrganizationAsync(Guid userId, Guid organizationId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Offene, zu genehmigende Anträge (nur Verwaltende).</summary>
    Task<IReadOnlyList<AbsenceRequest>> GetPendingAsync(Guid userId, Guid organizationId, CancellationToken ct = default);

    Task<ServiceResult> RequestAsync(Guid userId, AbsenceInput input, CancellationToken ct = default);

    Task<ServiceResult> DecideAsync(Guid userId, Guid absenceId, bool approve, CancellationToken ct = default);

    Task<ServiceResult> CancelAsync(Guid userId, Guid absenceId, CancellationToken ct = default);
}

public sealed class AbsenceService : IAbsenceService
{
    private readonly AppDbContext _db;
    private readonly IOrganizationAccessService _access;
    private readonly IAuditService _audit;

    public AbsenceService(AppDbContext db, IOrganizationAccessService access, IAuditService audit)
    {
        _db = db;
        _access = access;
        _audit = audit;
    }

    public async Task<IReadOnlyList<AbsenceRequest>> GetForUserAsync(Guid userId, CancellationToken ct = default)
        => await _db.AbsenceRequests
            .AsNoTracking()
            .Include(a => a.Organization)
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.StartDate)
            .Take(200)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<AbsenceRequest>> GetForOrganizationAsync(Guid userId, Guid organizationId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!await _access.IsMemberAsync(userId, organizationId, ct).ConfigureAwait(false))
        {
            return [];
        }

        var canManage = await _access.CanManageOrganizationAsync(userId, organizationId, ct).ConfigureAwait(false);

        var query = _db.AbsenceRequests
            .AsNoTracking()
            .Include(a => a.User)
            .Where(a => a.OrganizationId == organizationId
                        && a.StartDate <= to
                        && a.EndDate >= from);

        if (!canManage)
        {
            // Normale Mitglieder sehen nur genehmigte Abwesenheiten (Teamübersicht).
            query = query.Where(a => a.Status == AbsenceStatus.Approved);
        }

        return await query
            .OrderBy(a => a.StartDate)
            .Take(500)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AbsenceRequest>> GetPendingAsync(Guid userId, Guid organizationId, CancellationToken ct = default)
    {
        if (!await _access.CanManageOrganizationAsync(userId, organizationId, ct).ConfigureAwait(false))
        {
            return [];
        }

        return await _db.AbsenceRequests
            .AsNoTracking()
            .Include(a => a.User)
            .Where(a => a.OrganizationId == organizationId && a.Status == AbsenceStatus.Requested)
            .OrderBy(a => a.StartDate)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ServiceResult> RequestAsync(Guid userId, AbsenceInput input, CancellationToken ct = default)
    {
        if (!await _access.IsMemberAsync(userId, input.OrganizationId, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind kein Mitglied dieser Organisation.");
        }

        if (input.EndDate < input.StartDate)
        {
            return ServiceResult.Fail("Das Enddatum darf nicht vor dem Startdatum liegen.");
        }

        if (input.EndDate.DayNumber - input.StartDate.DayNumber > 366)
        {
            return ServiceResult.Fail("Ein Abwesenheitsantrag darf höchstens ein Jahr umfassen.");
        }

        // Überschneidungen mit bestehenden offenen/genehmigten Anträgen verhindern.
        var overlap = await _db.AbsenceRequests
            .AsNoTracking()
            .AnyAsync(a => a.UserId == userId
                           && a.OrganizationId == input.OrganizationId
                           && a.Status != AbsenceStatus.Rejected
                           && a.Status != AbsenceStatus.Cancelled
                           && a.StartDate <= input.EndDate
                           && a.EndDate >= input.StartDate, ct)
            .ConfigureAwait(false);

        if (overlap)
        {
            return ServiceResult.Fail("Für diesen Zeitraum besteht bereits ein Antrag.");
        }

        var item = new AbsenceRequest
        {
            OrganizationId = input.OrganizationId,
            UserId = userId,
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            Type = input.Type,
            Status = AbsenceStatus.Requested,
            Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.AbsenceRequests.Add(item);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _audit.WriteAsync("absence.request", userId, item.OrganizationId, "AbsenceRequest", item.Id.ToString(), cancellationToken: ct).ConfigureAwait(false);

        return ServiceResult.Ok(item.Id);
    }

    public async Task<ServiceResult> DecideAsync(Guid userId, Guid absenceId, bool approve, CancellationToken ct = default)
    {
        var item = await _db.AbsenceRequests.FirstOrDefaultAsync(a => a.Id == absenceId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return ServiceResult.Fail("Der Antrag wurde nicht gefunden.");
        }

        if (!await _access.CanManageOrganizationAsync(userId, item.OrganizationId, ct).ConfigureAwait(false))
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diesen Antrag zu bearbeiten.");
        }

        if (item.Status != AbsenceStatus.Requested)
        {
            return ServiceResult.Fail("Der Antrag wurde bereits bearbeitet.");
        }

        item.Status = approve ? AbsenceStatus.Approved : AbsenceStatus.Rejected;
        item.DecidedByUserId = userId;
        item.DecidedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _audit.WriteAsync(approve ? "absence.approve" : "absence.reject", userId, item.OrganizationId, "AbsenceRequest", item.Id.ToString(), cancellationToken: ct).ConfigureAwait(false);

        return ServiceResult.Ok(item.Id);
    }

    public async Task<ServiceResult> CancelAsync(Guid userId, Guid absenceId, CancellationToken ct = default)
    {
        var item = await _db.AbsenceRequests.FirstOrDefaultAsync(a => a.Id == absenceId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return ServiceResult.Fail("Der Antrag wurde nicht gefunden.");
        }

        var isOwner = item.UserId == userId;
        var canManage = await _access.CanManageOrganizationAsync(userId, item.OrganizationId, ct).ConfigureAwait(false);
        if (!isOwner && !canManage)
        {
            return ServiceResult.Fail("Sie sind nicht berechtigt, diesen Antrag zu stornieren.");
        }

        item.Status = AbsenceStatus.Cancelled;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ServiceResult.Ok(absenceId);
    }
}
