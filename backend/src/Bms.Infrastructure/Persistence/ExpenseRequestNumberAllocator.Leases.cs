using Bms.Application.Abstractions;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed partial class ExpenseRequestNumberAllocator
{
    public static readonly TimeSpan LeaseLifetime = TimeSpan.FromMinutes(10);
    public async Task AcquireAsync(CancellationToken token)
    {
        EnsureSerializableTransaction();
        // First, before authority/CFO/counter/lease range reads. An empty unique index has
        // one shared gap across CFOs; anchor-only locks cannot prevent insert/insert deadlocks.
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=N'BMS:ExpenseRequest:NumberReservations',
                @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
            IF @result<0 THROW 65310,N'Expense-request numbering lock unavailable.',1;
            """, token);
    }

    private async Task<(string Prefix, ExpenseRequestCostCenterCounter? Counter)> LockAsync(Guid cfo, CancellationToken ct)
    {
        EnsureSerializableTransaction();
        await AcquireAsync(ct);
        if (cfo == Guid.Empty) throw new DomainRuleViolationException("A CFO is required.");
        var center = await db.CostCenters.FromSqlInterpolated($"""
            SELECT c.* FROM bms_web.CostCenters c WITH(UPDLOCK,HOLDLOCK) WHERE c.Id={cfo}
            """).AsNoTracking().SingleOrDefaultAsync(ct);
        if (center is null || !center.IsActive || center.IsGroup)
            throw new DomainRuleViolationException("The selected CFO is unavailable.");
        var prefix = ExpenseRequestDocumentNumber.CanonicalizePrefix(center.Code);
        var counter = await db.ExpenseRequestCostCenterCounters.SingleOrDefaultAsync(x => x.CostCenterId == cfo, ct);
        if (counter is not null) await db.Entry(counter).ReloadAsync(ct);
        if (counter is not null && counter.Prefix != prefix)
            throw new DomainRuleViolationException("The CFO number prefix changed.");
        return (prefix, counter);
    }

    public async Task<ExpenseRequestNumberLease> ReserveAsync(Guid costCenterId, Guid leaseId, string actor,
        DateTimeOffset now, CancellationToken token)
    {
        var (prefix, counter) = await LockAsync(costCenterId, token);
        if (leaseId == Guid.Empty || string.IsNullOrWhiteSpace(actor))
            throw new DomainRuleViolationException("A reservation identity is required.");
        var row = await db.ExpenseRequestNumberLeases.SingleOrDefaultAsync(x => x.LeaseId == leaseId, token);
        if (row is not null)
        {
            await db.Entry(row).ReloadAsync(token);
            if (row.CostCenterId != costCenterId || row.ActorExternalId != actor || row.Consumed
                || row.Number != ExpenseRequestDocumentNumber.Next(prefix, row.Ordinal - 1).Number
                || await db.ExpenseRequests.IgnoreQueryFilters().AnyAsync(x => x.RequestNumber == row.Number, token))
                throw new DomainRuleViolationException("The number reservation is unavailable.");
            row.ExpiresAtUtc = now + LeaseLifetime;
            return Present(row);
        }
        if (await db.ExpenseRequestNumberLeases.CountAsync(x => x.CostCenterId == costCenterId
            && x.ActorExternalId == actor && !x.Consumed && x.ExpiresAtUtc > now, token) >= 20)
            throw new DomainRuleViolationException("Too many simultaneously open request forms.");
        // Reuse only a previously reserved but never saved number. Saved/deleted historical documents
        // never make a consumed lease vacant; the permanent counter is never lowered.
        row = await db.ExpenseRequestNumberLeases.Where(x => x.CostCenterId == costCenterId
                && !x.Consumed && x.ExpiresAtUtc <= now
                && !db.ExpenseRequests.IgnoreQueryFilters().Any(r => r.RequestNumber == x.Number))
            .OrderBy(x => x.Ordinal).FirstOrDefaultAsync(token);
        if (row is null)
        {
            var next = ExpenseRequestDocumentNumber.Next(prefix, counter?.LastIssuedOrdinal ?? 0);
            while (await db.ExpenseRequestNumberLeases.AnyAsync(x => x.Number == next.Number, token)
                || await db.ExpenseRequests.IgnoreQueryFilters().AnyAsync(x => x.RequestNumber == next.Number, token))
                next = ExpenseRequestDocumentNumber.Next(prefix, next.Ordinal);
            row = new() { CostCenterId = costCenterId, Ordinal = next.Ordinal, Number = next.Number };
            db.ExpenseRequestNumberLeases.Add(row);
        }
        row.LeaseId = leaseId;
        row.Number = ExpenseRequestDocumentNumber.Next(prefix, row.Ordinal - 1).Number;
        row.ActorExternalId = actor;
        row.ExpiresAtUtc = now + LeaseLifetime;
        return Present(row);
    }

    public async Task ReleaseAsync(Guid costCenterId, Guid leaseId, string actor, DateTimeOffset now, CancellationToken token)
    {
        await LockAsync(costCenterId, token);
        var row = await db.ExpenseRequestNumberLeases.SingleOrDefaultAsync(x => x.LeaseId == leaseId, token);
        if (row is null) return; // Already recycled: never release a newer occupant.
        await db.Entry(row).ReloadAsync(token);
        if (row.CostCenterId != costCenterId || row.ActorExternalId != actor)
            throw new DomainRuleViolationException("The number reservation is unavailable.");
        if (!row.Consumed) row.ExpiresAtUtc = now;
    }

    public async Task<string> ConsumeAsync(Guid costCenterId, Guid leaseId, string actor, DateTimeOffset now, CancellationToken token)
    {
        var (prefix, counter) = await LockAsync(costCenterId, token);
        var row = await db.ExpenseRequestNumberLeases.SingleOrDefaultAsync(x => x.LeaseId == leaseId, token);
        if (row is not null) await db.Entry(row).ReloadAsync(token);
        if (row is null || row.CostCenterId != costCenterId || row.ActorExternalId != actor
            || row.Consumed || row.ExpiresAtUtc <= now
            || row.Number != ExpenseRequestDocumentNumber.Next(prefix, row.Ordinal - 1).Number
            || await db.ExpenseRequests.IgnoreQueryFilters().AnyAsync(x => x.RequestNumber == row.Number, token))
            throw new DomainRuleViolationException("The number reservation expired. Refresh it before saving.",
                DomainRuleViolationReason.ExpenseDraftNumberReservationExpired);
        if (counter is null)
        {
            counter = new() { CostCenterId = costCenterId, Prefix = prefix };
            db.ExpenseRequestCostCenterCounters.Add(counter);
        }
        counter.LastIssuedOrdinal = Math.Max(counter.LastIssuedOrdinal, row.Ordinal);
        row.Consumed = true;
        return row.Number;
    }

    private static ExpenseRequestNumberLease Present(ExpenseRequestNumberLeaseRow row) =>
        new(row.LeaseId, row.CostCenterId, row.Number, row.ExpiresAtUtc);
}
