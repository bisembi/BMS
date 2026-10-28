using Bms.Domain.Common;

namespace Bms.Domain.Budgeting;

public sealed record BudgetExecutionOpening343(BudgetKey Key, decimal LimitAmountKzt, decimal PaidAmountKzt);
public sealed record BudgetExecutionFrozen343(decimal PositiveDeficitKzt, string FinancialFingerprint, DateTimeOffset ApprovedAtUtc);
public sealed record BudgetExecutionRequest343(Guid Id, DateTimeOffset CreatedAtUtc, bool Submitted, bool Active,
    IReadOnlyDictionary<BudgetKey, decimal> RemainingReservations, BudgetExecutionFrozen343? Frozen)
{
    // Proven payments stay at the request's queue position, replacing rather
    // than duplicating consumption already present in opening cash.
    public IReadOnlyDictionary<BudgetKey, decimal> PaidQueueAmounts { get; init; } = new Dictionary<BudgetKey,decimal>();
}
public sealed record BudgetExecutionLine343(BudgetKey Key, decimal AmountKzt, decimal AvailableBeforeKzt, decimal AvailableAfterKzt)
{
    public decimal PaidBeforeKzt { get; init; }
    public decimal ReservedBeforeKzt { get; init; }
}
public sealed record BudgetExecutionDecision343(Guid RequestId, bool Frozen, decimal PositiveDeficitKzt,
    IReadOnlyList<BudgetExecutionLine343> Lines)
{
    public bool IsOverBudget => PositiveDeficitKzt > 0m;
}
public sealed record BudgetExecutionEvaluation343(IReadOnlyDictionary<Guid, BudgetExecutionDecision343> Decisions,
    IReadOnlyDictionary<BudgetKey, decimal> AvailableBalances);

/// <summary>
/// One calendar-month key remains CFO + DDS article + month. Organization and
/// accrual dimensions do not multiply the existing company budget limit.
/// Approved financial fingerprints retain their original decision. Current
/// submitted unfrozen requests consume remaining funds in creation FIFO order.
/// Draft previews see current submitted consumption but never consume funds.
/// </summary>
public static class BudgetExecutionFifo343
{
    public static BudgetExecutionEvaluation343 Evaluate(IReadOnlyCollection<BudgetExecutionOpening343> openings,
        IReadOnlyCollection<BudgetExecutionRequest343> requests, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(openings);ArgumentNullException.ThrowIfNull(requests);
        cancellationToken.ThrowIfCancellationRequested();
        if (openings.Select(x => x.Key).Distinct().Count() != openings.Count
            || requests.Any(x => x.Id == Guid.Empty || x.CreatedAtUtc == default)
            || requests.Select(x => x.Id).Distinct().Count() != requests.Count)
            throw new InvalidOperationException("Distinct budget keys and request identities with creation times required.");
        foreach (var x in openings) { Money(x.LimitAmountKzt);Money(x.PaidAmountKzt); }
        foreach (var r in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(r.RemainingReservations);
            foreach (var amount in r.RemainingReservations.Values) Money(amount);
            foreach (var amount in r.PaidQueueAmounts.Values) Money(amount);
            if (r.Frozen is { } frozen)
            {
                Money(frozen.PositiveDeficitKzt);
                if (frozen.ApprovedAtUtc == default || frozen.FinancialFingerprint.Length != 64
                    || frozen.FinancialFingerprint.Any(c => c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
                    throw new InvalidOperationException("Exact approved financial fingerprint required.");
            }
        }
        var available = openings.ToDictionary(x => x.Key, x => checked(x.LimitAmountKzt - x.PaidAmountKzt));
        var paidBefore=openings.ToDictionary(x=>x.Key,x=>x.PaidAmountKzt);
        var reservedBefore=openings.ToDictionary(x=>x.Key,_=>0m);
        foreach (var key in requests.SelectMany(r => (r.Active ? r.RemainingReservations.Keys : []).Concat(r.PaidQueueAmounts.Keys)))
            if (!available.ContainsKey(key)) throw new InvalidOperationException("Explicit budget opening required, including accepted zero limits.");
        foreach(var payment in requests.SelectMany(r=>r.PaidQueueAmounts).GroupBy(p=>p.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var amount=payment.Sum(p=>p.Value);
            // Each grouped key is visited once: its paid value is still the opening.
            if(amount>paidBefore[payment.Key])
                throw new InvalidOperationException("Queue payments exceed independently posted cash.");
            available[payment.Key]=checked(available[payment.Key]+amount);
            paidBefore[payment.Key]-=amount;
        }
        var result = new Dictionary<Guid, BudgetExecutionDecision343>();
        // Approval does not reorder FIFO. Only the approved flag is frozen.
        foreach (var r in requests.Where(r => (r.Active && r.Submitted) || r.PaidQueueAmounts.Count>0)
                     .OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.Id.ToString("N"), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var amounts=(r.Active&&r.Submitted?r.RemainingReservations: new Dictionary<BudgetKey,decimal>())
                .Concat(r.PaidQueueAmounts).GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Sum(p=>p.Value));
            var lines = Allocate(r with{RemainingReservations=amounts}, available,paidBefore,reservedBefore);
            result.Add(r.Id, r.Frozen is{} frozen ? new(r.Id,true,frozen.PositiveDeficitKzt,lines) : Decision(r,lines));
        }
        foreach(var r in requests.Where(r=>r.Frozen is not null&&!result.ContainsKey(r.Id)))
            result.Add(r.Id,new(r.Id,true,(r.Frozen??throw new InvalidOperationException("Frozen decision missing.")).PositiveDeficitKzt,[]));
        // Compute every draft against the same final submitted balance; one
        // draft preview must not reserve funds ahead of another draft.
        foreach (var r in requests.Where(r => r.Frozen is null && r.Active && !r.Submitted && !result.ContainsKey(r.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A preview has unique reservation keys and no paid queue. Read the
            // final submitted balances without copying every unrelated key.
            result.Add(r.Id, Decision(r, Allocate(r, available, paidBefore, reservedBefore, consume: false)));
        }
        return new(result, available);
    }

    private static BudgetExecutionDecision343 Decision(BudgetExecutionRequest343 request, IReadOnlyList<BudgetExecutionLine343> lines) =>
        new(request.Id, false, lines.Aggregate(0m, (total, line) => checked(total + Math.Max(0m, -line.AvailableAfterKzt))), lines);

    private static IReadOnlyList<BudgetExecutionLine343> Allocate(BudgetExecutionRequest343 request, Dictionary<BudgetKey, decimal> available,
        Dictionary<BudgetKey,decimal> paidBefore,Dictionary<BudgetKey,decimal> reservedBefore, bool consume = true)
    {
        var lines = new List<BudgetExecutionLine343>();
        foreach (var row in request.RemainingReservations.OrderBy(x => x.Key.BudgetMonth)
                     .ThenBy(x => x.Key.CostCenterId.ToString("N"), StringComparer.Ordinal)
                     .ThenBy(x => x.Key.CashFlowItemId.ToString("N"), StringComparer.Ordinal))
        {
            var before = available[row.Key];var after = checked(before - row.Value);
            lines.Add(new(row.Key, row.Value, before, after)
                {PaidBeforeKzt=paidBefore[row.Key],ReservedBeforeKzt=reservedBefore[row.Key]});
            var ownPaid=request.PaidQueueAmounts.GetValueOrDefault(row.Key);
            if(ownPaid>row.Value)throw new InvalidOperationException("Queue payment allocation drift.");
            var paidAfter=checked(paidBefore[row.Key]+ownPaid);
            var reservedAfter=checked(reservedBefore[row.Key]+row.Value-ownPaid);
            if (consume)
            {
                available[row.Key] = after;
                paidBefore[row.Key]=paidAfter;
                reservedBefore[row.Key]=reservedAfter;
            }
        }
        return lines;
    }

    private static void Money(decimal amount)
    {
        if (amount < 0m || amount > MoneyAmountPolicy.Maximum || decimal.Round(amount, MoneyAmountPolicy.Scale) != amount)
            throw new InvalidOperationException("Nonnegative exact KZT monetary values required.");
    }
}
