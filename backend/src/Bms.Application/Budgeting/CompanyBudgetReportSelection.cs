using Bms.Domain.Budgeting;

namespace Bms.Application.Budgeting;

public static class CompanyBudgetReportSelection
{
    public static BudgetInstance? Winner(IReadOnlyCollection<BudgetInstance> sources, Guid center, DateOnly month,
        IReadOnlyDictionary<Guid, DateTimeOffset?>? creationTimes = null)
    {
        var covered = sources.Where(x => x.CostCenterId == center && x.ValidFrom <= month && x.ValidTo >= month).ToArray();
        if (covered.Length == 0) return null;
        if (covered.Length == 1) return covered[0];
        if (covered.Any(x => x.Status != BudgetInstanceStatus.Approved))
        {
            // An unknown legacy date is not the migration time or an inferred
            // number/approval date. Only ambiguous covered mixed/Draft months
            // fail closed; a sole legacy source above remains displayable.
            if (creationTimes is null || covered.Any(x => !creationTimes.TryGetValue(x.Id, out var at) || !at.HasValue))
                throw new CompanyBudgetOrderUnresolvedException();
            return covered.OrderByDescending(x => creationTimes[x.Id])
                .ThenByDescending(x => x.Id, CompanyBudgetCreationGuidComparer.Instance).First();
        }
        if (covered.Any(x => !x.ApprovalOrdinal.HasValue || !x.ApprovedAtUtc.HasValue)) throw new CompanyBudgetPlanUnavailableException();
        return covered.OrderByDescending(x => x.ApprovalOrdinal).ThenByDescending(x => x.ApprovedAtUtc).ThenByDescending(x => x.Id).First();
    }
}

// SQL uniqueidentifier precedence expressed only as a pure 16-byte order.
// Guid.TryWriteBytes uses the same little-endian field representation as
// Guid.ToByteArray. Provider types belong to Infrastructure/tests, not here.
internal sealed class CompanyBudgetCreationGuidComparer : IComparer<Guid>
{
    internal static IComparer<Guid> Instance { get; } = new CompanyBudgetCreationGuidComparer();
    private static ReadOnlySpan<byte> ByteOrder => [10, 11, 12, 13, 14, 15, 8, 9, 6, 7, 4, 5, 0, 1, 2, 3];
    public int Compare(Guid left, Guid right)
    {
        Span<byte> leftBytes = stackalloc byte[16];
        Span<byte> rightBytes = stackalloc byte[16];
        left.TryWriteBytes(leftBytes); right.TryWriteBytes(rightBytes);
        foreach (var position in ByteOrder)
        {
            var difference = leftBytes[position].CompareTo(rightBytes[position]);
            if (difference != 0) return difference;
        }
        return 0;
    }
}
