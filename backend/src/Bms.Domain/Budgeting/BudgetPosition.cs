using Bms.Domain.Common;

namespace Bms.Domain.Budgeting;

public sealed class BudgetPosition
{
    private BudgetPosition()
    {
    }

    private BudgetPosition(BudgetKey key, decimal? limitAmountKzt)
    {
        CostCenterId = key.CostCenterId;
        CashFlowItemId = key.CashFlowItemId;
        BudgetMonth = key.BudgetMonth;
        SetLimitAmount(limitAmountKzt);
        ConcurrencyStamp = Guid.NewGuid();
    }

    public Guid CostCenterId { get; private set; }

    public Guid CashFlowItemId { get; private set; }

    public DateOnly BudgetMonth { get; private set; }

    public decimal? LimitAmountKzt { get; private set; }

    public decimal ReservedAmountKzt { get; private set; }

    public decimal PaidAmountKzt { get; private set; }

    public Guid ConcurrencyStamp { get; private set; }

    public BudgetKey Key => BudgetKey.Create(CostCenterId, CashFlowItemId, BudgetMonth);

    public decimal? AvailableAmountKzt =>
        CalculateAvailableAmountKzt(LimitAmountKzt, ReservedAmountKzt, PaidAmountKzt);

    /// <summary>
    /// The single definition of what is available on a budget position. A
    /// position without a configured limit has no available amount at all: it is
    /// unknown, never zero. Read projections that do not materialise the entity
    /// use this same method so the rule cannot drift between them.
    /// </summary>
    public static decimal? CalculateAvailableAmountKzt(
        decimal? limitAmountKzt,
        decimal reservedAmountKzt,
        decimal paidAmountKzt) =>
        limitAmountKzt.HasValue
            ? limitAmountKzt.Value - reservedAmountKzt - paidAmountKzt
            : null;

    public bool IsOverLimit => AvailableAmountKzt is < 0m;

    public static BudgetPosition Create(BudgetKey key, decimal? limitAmountKzt = null) =>
        new(key, limitAmountKzt);

    public void SetLimitAmount(decimal? amountKzt)
    {
        MoneyAmountPolicy.EnsureNonNegative(amountKzt, "Budget limit");
        LimitAmountKzt = amountKzt;
        Touch();
    }

    public void SetPaidAmount(decimal amountKzt)
    {
        MoneyAmountPolicy.EnsureNonNegative(amountKzt, "Paid amount");
        PaidAmountKzt = amountKzt;
        Touch();
    }

    public void SetReservedAmount(decimal amountKzt)
    {
        MoneyAmountPolicy.EnsureNonNegative(amountKzt, "Reserved amount");
        ReservedAmountKzt = amountKzt;
        Touch();
    }

    public void ApplyReservationDelta(decimal deltaAmountKzt)
    {
        MoneyAmountPolicy.EnsureRepresentable(deltaAmountKzt, "Budget reservation delta");
        SetReservedAmount(ReservedAmountKzt + deltaAmountKzt);
    }

    private void Touch()
    {
        if (ConcurrencyStamp != Guid.Empty)
        {
            ConcurrencyStamp = Guid.NewGuid();
        }
    }
}
