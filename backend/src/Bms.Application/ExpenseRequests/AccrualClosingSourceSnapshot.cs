using System.Collections.ObjectModel;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

internal enum AccrualClosingSourceState
{
    Ready = 1,
    RequestStatusIsNotPaid = 2,
    NoManagerialAccrualLines = 3
}

/// <summary>
/// Immutable facts for one submitted request line whose captured source
/// settings require a managerial accrual. Gross and VAT amounts are preserved
/// separately: this type does not choose the future accrual amount basis.
/// </summary>
internal sealed class AccrualClosingSourceLine
{
    internal AccrualClosingSourceLine(
        Guid lineId,
        int sortOrder,
        Guid cashFlowItemId,
        Guid costCenterId,
        Guid expenseItemId,
        DateOnly accrualPeriod,
        decimal grossAmount,
        decimal vatAmount,
        decimal amountExcludingVat,
        decimal grossAmountKzt,
        decimal vatAmountKzt,
        decimal amountExcludingVatKzt)
    {
        if (lineId == Guid.Empty ||
            sortOrder <= 0 ||
            cashFlowItemId == Guid.Empty ||
            costCenterId == Guid.Empty ||
            expenseItemId == Guid.Empty ||
            accrualPeriod.Day != 1 ||
            grossAmount < 0m ||
            vatAmount < 0m ||
            amountExcludingVat < 0m ||
            grossAmountKzt < 0m ||
            vatAmountKzt < 0m ||
            amountExcludingVatKzt < 0m ||
            !MoneyAmountPolicy.IsRepresentable(grossAmount) ||
            !MoneyAmountPolicy.IsRepresentable(vatAmount) ||
            !MoneyAmountPolicy.IsRepresentable(amountExcludingVat) ||
            !MoneyAmountPolicy.IsRepresentable(grossAmountKzt) ||
            !MoneyAmountPolicy.IsRepresentable(vatAmountKzt) ||
            !MoneyAmountPolicy.IsRepresentable(amountExcludingVatKzt) ||
            vatAmount + amountExcludingVat != grossAmount ||
            vatAmountKzt + amountExcludingVatKzt != grossAmountKzt)
        {
            throw InvariantViolation();
        }

        LineId = lineId;
        SortOrder = sortOrder;
        CashFlowItemId = cashFlowItemId;
        CostCenterId = costCenterId;
        ExpenseItemId = expenseItemId;
        AccrualPeriod = accrualPeriod;
        GrossAmount = grossAmount;
        VatAmount = vatAmount;
        AmountExcludingVat = amountExcludingVat;
        GrossAmountKzt = grossAmountKzt;
        VatAmountKzt = vatAmountKzt;
        AmountExcludingVatKzt = amountExcludingVatKzt;
    }

    internal Guid LineId { get; }

    internal int SortOrder { get; }

    internal Guid CashFlowItemId { get; }

    internal Guid CostCenterId { get; }

    internal Guid ExpenseItemId { get; }

    internal DateOnly AccrualPeriod { get; }

    internal decimal GrossAmount { get; }

    internal decimal VatAmount { get; }

    internal decimal AmountExcludingVat { get; }

    internal decimal GrossAmountKzt { get; }

    internal decimal VatAmountKzt { get; }

    internal decimal AmountExcludingVatKzt { get; }

    private static InvalidOperationException InvariantViolation() => new(
        "Accrual closing source snapshot violates the internal invariant.");
}

/// <summary>
/// A source-neutral snapshot from a request whose persisted status is PAID.
/// It is not close authorization and does not prove the payment ledger: a
/// future writer must reread both under its own Serializable transaction.
/// </summary>
internal sealed class AccrualClosingSourceSnapshot
{
    internal AccrualClosingSourceSnapshot(
        Guid expenseRequestId,
        Guid organizationId,
        Guid costCenterId,
        Guid requestVersion,
        DateTimeOffset sourceUpdatedAtUtc,
        IEnumerable<AccrualClosingSourceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var capturedLines = lines.ToArray();
        if (expenseRequestId == Guid.Empty ||
            organizationId == Guid.Empty ||
            costCenterId == Guid.Empty ||
            requestVersion == Guid.Empty ||
            sourceUpdatedAtUtc.Offset != TimeSpan.Zero ||
            capturedLines.Length == 0 ||
            capturedLines.Any(line => line is null) ||
            capturedLines.Select(line => line.LineId).Distinct().Count() !=
                capturedLines.Length ||
            capturedLines.Select(line => line.SortOrder).Distinct().Count() !=
                capturedLines.Length ||
            // The header is the budget/document CFO; line CFOs are accrual analytics.
            !capturedLines
                .Select(line => line.SortOrder)
                .SequenceEqual(capturedLines.Select(line => line.SortOrder).Order()))
        {
            throw InvariantViolation();
        }

        ExpenseRequestId = expenseRequestId;
        OrganizationId = organizationId;
        CostCenterId = costCenterId;
        RequestVersion = requestVersion;
        SourceUpdatedAtUtc = sourceUpdatedAtUtc;
        Lines = new ReadOnlyCollection<AccrualClosingSourceLine>(capturedLines);
        GrossAmount = Sum(capturedLines.Select(line => line.GrossAmount));
        VatAmount = Sum(capturedLines.Select(line => line.VatAmount));
        AmountExcludingVat = Sum(
            capturedLines.Select(line => line.AmountExcludingVat));
        GrossAmountKzt = Sum(capturedLines.Select(line => line.GrossAmountKzt));
        VatAmountKzt = Sum(capturedLines.Select(line => line.VatAmountKzt));
        AmountExcludingVatKzt = Sum(
            capturedLines.Select(line => line.AmountExcludingVatKzt));
        if (VatAmount + AmountExcludingVat != GrossAmount ||
            VatAmountKzt + AmountExcludingVatKzt != GrossAmountKzt)
        {
            throw InvariantViolation();
        }
    }

    internal Guid ExpenseRequestId { get; }

    internal Guid OrganizationId { get; }

    internal Guid CostCenterId { get; }

    internal Guid RequestVersion { get; }

    internal DateTimeOffset SourceUpdatedAtUtc { get; }

    internal IReadOnlyList<AccrualClosingSourceLine> Lines { get; }

    internal decimal GrossAmount { get; }

    internal decimal VatAmount { get; }

    internal decimal AmountExcludingVat { get; }

    internal decimal GrossAmountKzt { get; }

    internal decimal VatAmountKzt { get; }

    internal decimal AmountExcludingVatKzt { get; }

    private static decimal Sum(IEnumerable<decimal> values)
    {
        try
        {
            return values.Aggregate(0m, (total, value) => checked(total + value));
        }
        catch (OverflowException)
        {
            throw InvariantViolation();
        }
    }

    private static InvalidOperationException InvariantViolation() => new(
        "Accrual closing source snapshot violates the internal invariant.");
}

internal sealed class AccrualClosingSourceAssessment
{
    private AccrualClosingSourceAssessment(
        AccrualClosingSourceState state,
        AccrualClosingSourceSnapshot? snapshot)
    {
        if (!Enum.IsDefined(state) ||
            (state == AccrualClosingSourceState.Ready) != (snapshot is not null))
        {
            throw new ArgumentException(
                "Accrual closing source assessment is inconsistent.");
        }

        State = state;
        Snapshot = snapshot;
    }

    internal AccrualClosingSourceState State { get; }

    internal AccrualClosingSourceSnapshot? Snapshot { get; }

    internal static AccrualClosingSourceAssessment Ready(
        AccrualClosingSourceSnapshot snapshot) => new(
        AccrualClosingSourceState.Ready,
        snapshot ?? throw new ArgumentNullException(nameof(snapshot)));

    internal static AccrualClosingSourceAssessment Unavailable(
        AccrualClosingSourceState state) => new(state, null);
}

internal static class AccrualClosingSourceSnapshotBuilder
{
    internal static AccrualClosingSourceAssessment Capture(ExpenseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Status))
        {
            throw InvariantViolation();
        }

        if (request.Status != ExpenseRequestStatus.Paid)
        {
            return AccrualClosingSourceAssessment.Unavailable(
                AccrualClosingSourceState.RequestStatusIsNotPaid);
        }

        ExpenseRequestLine[] lines;
        try
        {
            lines = request.Lines.ToArray();
        }
        catch (NullReferenceException)
        {
            throw InvariantViolation();
        }
        var requestIdentity = ValidatePaidRequest(request, lines);
        var sourceLines = lines
            .Where(line => line.AccrualSnapshotFormsManagerialAccrual == true)
            .Select(CreateSourceLine)
            .ToArray();
        if (sourceLines.Length == 0)
        {
            return AccrualClosingSourceAssessment.Unavailable(
                AccrualClosingSourceState.NoManagerialAccrualLines);
        }

        return AccrualClosingSourceAssessment.Ready(
            new AccrualClosingSourceSnapshot(
                request.Id,
                requestIdentity.OrganizationId,
                requestIdentity.CostCenterId,
                request.ConcurrencyStamp,
                request.UpdatedAtUtc,
                sourceLines));
    }

    private static (Guid OrganizationId, Guid CostCenterId) ValidatePaidRequest(
        ExpenseRequest request,
        IReadOnlyCollection<ExpenseRequestLine> lines)
    {
        if (request.Id == Guid.Empty ||
            request.OrganizationId is not { } organizationId ||
            organizationId == Guid.Empty ||
            request.DefaultCostCenterId is not { } costCenterId ||
            costCenterId == Guid.Empty ||
            request.ConcurrencyStamp == Guid.Empty ||
            request.UpdatedAtUtc.Offset != TimeSpan.Zero ||
            request.ActiveApprovalRouteSnapshotNumber is not null ||
            request.ActiveApprovalRouteStageNumber != 0 ||
            lines.Count == 0 ||
            lines.Any(line => line is null) ||
            lines.Select(line => line.Id).Distinct().Count() != lines.Count ||
            lines.Select(line => line.SortOrder).Distinct().Count() != lines.Count)
        {
            throw InvariantViolation();
        }

        foreach (var line in lines)
        {
            var snapshotPairIsValid = line.AccrualSnapshotFormsManagerialAccrual switch
            {
                false => line.AccrualSnapshotExpenseItemId is null,
                true => line.AccrualSnapshotExpenseItemId is { } expenseItemId &&
                    expenseItemId != Guid.Empty,
                null => false
            };
            if (line.Id == Guid.Empty ||
                line.ExpenseRequestId != request.Id ||
                line.SortOrder <= 0 ||
                line.CashFlowItemId is not { } cashFlowItemId ||
                cashFlowItemId == Guid.Empty ||
                line.CostCenterId != costCenterId ||
                line.AccrualPeriod is { Day: not 1 } ||
                !line.VatRateCode.HasValue ||
                !Enum.IsDefined(line.VatRateCode.Value) ||
                line.AmountKzt is null ||
                line.VatAmount is null ||
                line.AmountExcludingVat is null ||
                line.VatAmountKzt is null ||
                line.AmountExcludingVatKzt is null ||
                !line.HasValidCalculatedAmounts() ||
                !snapshotPairIsValid ||
                (line.AccrualSnapshotFormsManagerialAccrual == true &&
                 line.AccrualPeriod is null))
            {
                throw InvariantViolation();
            }
        }

        return (organizationId, costCenterId);
    }

    private static AccrualClosingSourceLine CreateSourceLine(ExpenseRequestLine line)
    {
        if (line.CashFlowItemId is not { } cashFlowItemId ||
            (line.AccrualCostCenterId ?? line.CostCenterId) is not { } costCenterId ||
            line.AccrualSnapshotExpenseItemId is not { } expenseItemId ||
            line.AccrualPeriod is not { } accrualPeriod ||
            line.VatAmount is not { } vatAmount ||
            line.AmountExcludingVat is not { } amountExcludingVat ||
            line.AmountKzt is not { } grossAmountKzt ||
            line.VatAmountKzt is not { } vatAmountKzt ||
            line.AmountExcludingVatKzt is not { } amountExcludingVatKzt)
        {
            throw InvariantViolation();
        }

        return new AccrualClosingSourceLine(
            line.Id,
            line.SortOrder,
            cashFlowItemId,
            costCenterId,
            expenseItemId,
            accrualPeriod,
            line.Amount,
            vatAmount,
            amountExcludingVat,
            grossAmountKzt,
            vatAmountKzt,
            amountExcludingVatKzt);
    }

    private static InvalidOperationException InvariantViolation() => new(
        "Accrual closing source snapshot violates the internal invariant.");
}
