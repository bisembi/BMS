using Bms.Domain.Common;
using System.Text.Json.Serialization;

namespace Bms.Domain.ExpenseRequests;

public sealed class ExpenseRequestLine
{
    private ExpenseRequestLine()
    {
    }

    internal ExpenseRequestLine(
        Guid id,
        Guid expenseRequestId,
        Guid? cashFlowItemId,
        Guid? costCenterId,
        DateOnly? accrualPeriod,
        decimal amount,
        int sortOrder,
        ExpenseVatRate? vatRateCode,
        Guid? accrualCostCenterId = null)
    {
        if (id == Guid.Empty)
        {
            throw new DomainRuleViolationException("Line id is required.");
        }

        if (expenseRequestId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Expense request id is required.");
        }

        MoneyAmountPolicy.EnsurePositive(amount, "Line amount");
        ValidateSortOrder(sortOrder);

        Id = id;
        ExpenseRequestId = expenseRequestId;
        CashFlowItemId = cashFlowItemId;
        CostCenterId = costCenterId;
        AccrualCostCenterId = NormalizeAccrualCostCenter(accrualCostCenterId);
        AccrualPeriod = accrualPeriod;
        Amount = amount;
        SortOrder = sortOrder;
        ApplyCalculatedAmounts(amount, vatRateCode);
    }

    internal void Update(
        Guid? cashFlowItemId,
        Guid? costCenterId,
        DateOnly? accrualPeriod,
        decimal amount,
        ExpenseVatRate? vatRateCode,
        Guid? accrualCostCenterId = null)
    {
        MoneyAmountPolicy.EnsurePositive(amount, "Line amount");
        CashFlowItemId = cashFlowItemId;
        CostCenterId = costCenterId;
        AccrualCostCenterId = NormalizeAccrualCostCenter(accrualCostCenterId);
        AccrualPeriod = accrualPeriod;
        Amount = amount;
        ApplyCalculatedAmounts(amount, vatRateCode);
    }

    public Guid Id { get; private set; }

    public Guid ExpenseRequestId { get; private set; }

    public Guid? CashFlowItemId { get; private set; }

    public Guid? CostCenterId { get; private set; }

    // Separate P&L analytics. Never a budget key or a document access grant.
    // Null retains the historical analytical department for unmigrated lines.
    public Guid? AccrualCostCenterId { get; private set; }

    private static Guid? NormalizeAccrualCostCenter(Guid? id) => id == Guid.Empty
        ? throw new DomainRuleViolationException("An accrual department must be a non-empty reference.") : id;

    internal void UpdateApprovalAccrual(Guid costCenterId, DateOnly period, ExpenseVatRate vatRate,
        decimal vatAmountKzt, decimal netAmountKzt)
    {
        if ((AccrualSnapshotFormsManagerialAccrual == true && AccrualSnapshotExpenseItemId is null)
            || period.Day != 1 || costCenterId == Guid.Empty || AmountKzt is null
            || ConfirmedClosedAmount != 0 || ConfirmedClosedAmountKzt != 0)
            throw new DomainRuleViolationException("Only an unpaid accrual line can be edited.");
        AccrualCostCenterId = costCenterId; AccrualPeriod = period;
        ApplyCalculatedAmounts(Amount, vatRate);
        ApplyKztCalculation(AmountKzt.Value, vatAmountKzt, netAmountKzt);
    }

    public DateOnly? AccrualPeriod { get; private set; }

    public decimal Amount { get; private set; }

    public ExpenseVatRate? VatRateCode { get; private set; }

    public decimal? VatAmount { get; private set; }

    public decimal? AmountExcludingVat { get; private set; }

    public decimal? AmountKzt { get; private set; }

    public decimal? VatAmountKzt { get; private set; }

    public decimal? AmountExcludingVatKzt { get; private set; }

    [JsonIgnore]
    public bool? AccrualSnapshotFormsManagerialAccrual { get; private set; }

    [JsonIgnore]
    public Guid? AccrualSnapshotExpenseItemId { get; private set; }

    /// <summary>
    /// Server-owned cumulative document closure in request currency for the
    /// current accrual cycle. Direct draft editing never writes this value.
    /// </summary>
    [JsonIgnore]
    public decimal ConfirmedClosedAmount { get; private set; }

    /// <summary>The paired KZT closure derived only from the submission snapshot.</summary>
    [JsonIgnore]
    public decimal ConfirmedClosedAmountKzt { get; private set; }

    [JsonIgnore]
    public int SortOrder { get; private set; }

    public bool HasValidCalculatedAmounts()
    {
        var expectedVat = CalculateVatAmount(Amount, VatRateCode);
        if (VatAmount != expectedVat
            || AmountExcludingVat != (expectedVat.HasValue ? Amount - expectedVat.Value : null))
        {
            return false;
        }

        return HasValidKztCalculatedAmounts();
    }

    public bool HasValidSourceCalculatedAmounts()
    {
        var expectedVat = CalculateVatAmount(Amount, VatRateCode);
        return VatAmount == expectedVat
            && AmountExcludingVat == (expectedVat.HasValue ? Amount - expectedVat.Value : null);
    }

    public bool HasValidKztCalculatedAmounts()
    {
        if (!AmountKzt.HasValue)
        {
            return !VatAmountKzt.HasValue && !AmountExcludingVatKzt.HasValue;
        }

        if (!MoneyAmountPolicy.IsRepresentable(AmountKzt.Value) || AmountKzt.Value < 0m)
        {
            return false;
        }

        if (!VatAmountKzt.HasValue || !AmountExcludingVatKzt.HasValue)
        {
            return VatRateCode is null
                && !VatAmountKzt.HasValue
                && !AmountExcludingVatKzt.HasValue;
        }

        return MoneyAmountPolicy.IsRepresentable(VatAmountKzt.Value)
            && MoneyAmountPolicy.IsRepresentable(AmountExcludingVatKzt.Value)
            && VatAmountKzt.Value >= 0m
            && AmountExcludingVatKzt.Value >= 0m
            && VatAmountKzt.Value + AmountExcludingVatKzt.Value == AmountKzt.Value;
    }

    internal void ApplyKztCalculation(
        decimal amountKzt,
        decimal? vatAmountKzt,
        decimal? amountExcludingVatKzt)
    {
        MoneyAmountPolicy.EnsureNonNegative(amountKzt, "Line KZT amount");
        if (vatAmountKzt.HasValue != amountExcludingVatKzt.HasValue)
        {
            throw new DomainRuleViolationException(
                "Line KZT VAT components must either both be present or both be absent.");
        }

        if (vatAmountKzt is { } vatKzt && amountExcludingVatKzt is { } excludingVatKzt)
        {
            MoneyAmountPolicy.EnsureNonNegative(vatKzt, "Line KZT VAT amount");
            MoneyAmountPolicy.EnsureNonNegative(excludingVatKzt, "Line KZT amount excluding VAT");
            if (vatKzt + excludingVatKzt != amountKzt)
            {
                throw new DomainRuleViolationException(
                    "Line KZT VAT components must add up to the line KZT amount.");
            }
        }
        else if (VatRateCode.HasValue)
        {
            throw new DomainRuleViolationException(
                "A line with a VAT rate requires both calculated KZT VAT components.");
        }

        AmountKzt = amountKzt;
        VatAmountKzt = vatAmountKzt;
        AmountExcludingVatKzt = amountExcludingVatKzt;
    }

    internal void ClearKztCalculation()
    {
        AmountKzt = null;
        VatAmountKzt = null;
        AmountExcludingVatKzt = null;
    }

    internal bool HasAbsentAccrualSnapshot() =>
        AccrualSnapshotFormsManagerialAccrual is null &&
        AccrualSnapshotExpenseItemId is null;

    internal bool HasValidOrAbsentAccrualSnapshot() => HasAbsentAccrualSnapshot()
        || (AccrualSnapshotFormsManagerialAccrual is false && AccrualSnapshotExpenseItemId is null)
        || (AccrualSnapshotFormsManagerialAccrual is true
            && AccrualSnapshotExpenseItemId is { } expenseId && expenseId != Guid.Empty);

    // Only the aggregate's RETURNED-only resubmission boundary calls this.
    internal void ApplyResubmittedAccrualSnapshot(bool formsManagerialAccrual, Guid? expenseItemId)
    {
        if (!HasValidOrAbsentAccrualSnapshot()
            || (!formsManagerialAccrual && expenseItemId is not null)
            || (formsManagerialAccrual && (expenseItemId is null || expenseItemId == Guid.Empty)))
            throw new InvalidOperationException("The resubmitted accrual snapshot line assignment violates the invariant.");
        AccrualSnapshotFormsManagerialAccrual = formsManagerialAccrual;
        AccrualSnapshotExpenseItemId = expenseItemId;
    }

    internal void ApplyInitialAccrualSnapshot(
        bool formsManagerialAccrual,
        Guid? expenseItemId)
    {
        if (!HasAbsentAccrualSnapshot() ||
            (!formsManagerialAccrual && expenseItemId is not null) ||
            (formsManagerialAccrual &&
             (expenseItemId is null || expenseItemId == Guid.Empty)))
        {
            throw new InvalidOperationException(
                "The initial accrual snapshot line assignment violates the invariant.");
        }

        AccrualSnapshotFormsManagerialAccrual = formsManagerialAccrual;
        AccrualSnapshotExpenseItemId = expenseItemId;
    }

    internal void ApplyConfirmedClosingAllocation(decimal amount, decimal amountKzt)
    {
        if (AccrualSnapshotFormsManagerialAccrual != true ||
            AccrualSnapshotExpenseItemId is null ||
            AmountKzt is not { } originalAmountKzt)
        {
            throw new DomainRuleViolationException(
                "Only a captured managerial-accrual line can be closed.");
        }

        MoneyAmountPolicy.EnsurePositive(amount, "Line closing amount");
        MoneyAmountPolicy.EnsureNonNegative(amountKzt, "Line closing KZT amount");
        var remainingAmount = Amount - ConfirmedClosedAmount;
        var remainingAmountKzt = originalAmountKzt - ConfirmedClosedAmountKzt;
        if (remainingAmount <= 0m || remainingAmountKzt < 0m || amount > remainingAmount)
        {
            throw new DomainRuleViolationException(
                "Line closing allocation exceeds the active accrual balance.");
        }

        var expectedAmountKzt = PairedMoneyAllocationPolicy.Allocate(
            remainingAmountKzt,
            amount,
            remainingAmount);
        if (amountKzt != expectedAmountKzt)
        {
            throw new DomainRuleViolationException(
                "Line closing KZT allocation does not match the immutable snapshot ratio.");
        }

        try
        {
            ConfirmedClosedAmount = checked(ConfirmedClosedAmount + amount);
            ConfirmedClosedAmountKzt = checked(ConfirmedClosedAmountKzt + amountKzt);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException(
                "Line closing totals exceed decimal storage range.");
        }

        MoneyAmountPolicy.EnsureNonNegative(
            ConfirmedClosedAmount,
            "Confirmed closed amount");
        MoneyAmountPolicy.EnsureNonNegative(
            ConfirmedClosedAmountKzt,
            "Confirmed closed KZT amount");
        if (ConfirmedClosedAmount > Amount ||
            ConfirmedClosedAmountKzt > originalAmountKzt ||
            (ConfirmedClosedAmount == Amount &&
             ConfirmedClosedAmountKzt != originalAmountKzt))
        {
            throw new DomainRuleViolationException(
                "Line closing totals violate the immutable source balance.");
        }
    }

    internal void ResetConfirmedClosing()
    {
        ConfirmedClosedAmount = 0m;
        ConfirmedClosedAmountKzt = 0m;
    }

    private static void ValidateSortOrder(int sortOrder)
    {
        if (sortOrder <= 0)
        {
            throw new DomainRuleViolationException("Line sort order must be greater than zero.");
        }
    }

    private void ApplyCalculatedAmounts(decimal amount, ExpenseVatRate? vatRateCode)
    {
        if (vatRateCode.HasValue && !Enum.IsDefined(vatRateCode.Value))
        {
            throw new DomainRuleViolationException("Unsupported VAT rate.");
        }

        VatRateCode = vatRateCode;
        VatAmount = CalculateVatAmount(amount, vatRateCode);
        AmountExcludingVat = VatAmount.HasValue ? amount - VatAmount.Value : null;
    }

    private static decimal? CalculateVatAmount(decimal amount, ExpenseVatRate? vatRateCode) =>
        vatRateCode switch
        {
            ExpenseVatRate.WithoutVat => 0m,
            ExpenseVatRate.Vat16 => decimal.Round(
                amount * 16m / 116m,
                2,
                MidpointRounding.AwayFromZero),
            null => null,
            _ => throw new DomainRuleViolationException("Unsupported VAT rate.")
        };
}
