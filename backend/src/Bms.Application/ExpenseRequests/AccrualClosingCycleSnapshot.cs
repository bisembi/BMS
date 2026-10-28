using System.Collections.ObjectModel;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// <summary>
/// One immutable source line captured for a particular closing cycle. The
/// record deliberately preserves gross, VAT and net facts simultaneously: it
/// does not decide the amount allocated by a closing command.
/// </summary>
internal sealed class AccrualClosingCycleLine
{
    internal AccrualClosingCycleLine(AccrualClosingSourceLine source)
    {
        ArgumentNullException.ThrowIfNull(source);
        LineId = source.LineId;
        SortOrder = source.SortOrder;
        CashFlowItemId = source.CashFlowItemId;
        CostCenterId = source.CostCenterId;
        ExpenseItemId = source.ExpenseItemId;
        AccrualPeriod = source.AccrualPeriod;
        GrossAmount = source.GrossAmount;
        VatAmount = source.VatAmount;
        AmountExcludingVat = source.AmountExcludingVat;
        GrossAmountKzt = source.GrossAmountKzt;
        VatAmountKzt = source.VatAmountKzt;
        AmountExcludingVatKzt = source.AmountExcludingVatKzt;
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

    internal AccrualClosingSourceLine ToSourceLine() => new(
        LineId,
        SortOrder,
        CashFlowItemId,
        CostCenterId,
        ExpenseItemId,
        AccrualPeriod,
        GrossAmount,
        VatAmount,
        AmountExcludingVat,
        GrossAmountKzt,
        VatAmountKzt,
        AmountExcludingVatKzt);
}

/// <summary>
/// Immutable evidence for one expense-request closing cycle. A new cycle
/// receives a new UUID and monotonically increasing request-local number. The
/// snapshot itself grants no command, chooses no allocation basis and does not
/// change the request status.
/// </summary>
internal sealed class AccrualClosingCycleSnapshot
{
    private readonly IReadOnlyList<AccrualClosingCycleLine> _lines;

    private AccrualClosingCycleSnapshot(
        Guid cycleId,
        int cycleNumber,
        DateTimeOffset capturedAtUtc,
        AccrualClosingSourceSnapshot source,
        decimal expectedGrossAmount,
        decimal expectedVatAmount,
        decimal expectedAmountExcludingVat,
        decimal expectedGrossAmountKzt,
        decimal expectedVatAmountKzt,
        decimal expectedAmountExcludingVatKzt)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureStorageTime(source.SourceUpdatedAtUtc);
        EnsureStorageTime(capturedAtUtc);
        if (cycleId == Guid.Empty ||
            cycleNumber <= 0 ||
            capturedAtUtc < source.SourceUpdatedAtUtc ||
            source.GrossAmount != expectedGrossAmount ||
            source.VatAmount != expectedVatAmount ||
            source.AmountExcludingVat != expectedAmountExcludingVat ||
            source.GrossAmountKzt != expectedGrossAmountKzt ||
            source.VatAmountKzt != expectedVatAmountKzt ||
            source.AmountExcludingVatKzt != expectedAmountExcludingVatKzt)
        {
            throw InvariantViolation();
        }

        var lines = source.Lines
            .Select(line => new AccrualClosingCycleLine(line))
            .ToArray();
        CycleId = cycleId;
        CycleNumber = cycleNumber;
        ExpenseRequestId = source.ExpenseRequestId;
        OrganizationId = source.OrganizationId;
        CostCenterId = source.CostCenterId;
        RequestVersion = source.RequestVersion;
        SourceUpdatedAtUtc = source.SourceUpdatedAtUtc;
        CapturedAtUtc = capturedAtUtc;
        GrossAmount = source.GrossAmount;
        VatAmount = source.VatAmount;
        AmountExcludingVat = source.AmountExcludingVat;
        GrossAmountKzt = source.GrossAmountKzt;
        VatAmountKzt = source.VatAmountKzt;
        AmountExcludingVatKzt = source.AmountExcludingVatKzt;
        _lines = new ReadOnlyCollection<AccrualClosingCycleLine>(lines);
    }

    internal Guid CycleId { get; }

    internal int CycleNumber { get; }

    internal Guid ExpenseRequestId { get; }

    internal Guid OrganizationId { get; }

    internal Guid CostCenterId { get; }

    internal Guid RequestVersion { get; }

    internal DateTimeOffset SourceUpdatedAtUtc { get; }

    internal DateTimeOffset CapturedAtUtc { get; }

    internal decimal GrossAmount { get; }

    internal decimal VatAmount { get; }

    internal decimal AmountExcludingVat { get; }

    internal decimal GrossAmountKzt { get; }

    internal decimal VatAmountKzt { get; }

    internal decimal AmountExcludingVatKzt { get; }

    internal IReadOnlyList<AccrualClosingCycleLine> Lines => _lines;

    // Payment replay and closing must check the same immutable business facts.
    // Request version/time and current closing allocations may legitimately
    // advance; they must not replace this source snapshot.
    internal bool MatchesRequest(ExpenseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (ExpenseRequestId != request.Id || request.OrganizationId != OrganizationId ||
            request.DefaultCostCenterId != CostCenterId)
        {
            return false;
        }
        var qualified = request.Lines
            .Where(line => line.AccrualSnapshotFormsManagerialAccrual == true)
            .OrderBy(line => line.SortOrder).ToArray();
        if (qualified.Length != Lines.Count)
        {
            return false;
        }
        for (var index = 0; index < qualified.Length; index++)
        {
            var current = qualified[index];
            var captured = Lines[index];
            if (current.Id != captured.LineId || current.SortOrder != captured.SortOrder ||
                current.CashFlowItemId != captured.CashFlowItemId ||
                (current.AccrualCostCenterId ?? current.CostCenterId) != captured.CostCenterId ||
                current.AccrualSnapshotExpenseItemId != captured.ExpenseItemId ||
                current.AccrualPeriod != captured.AccrualPeriod ||
                current.Amount != captured.GrossAmount || current.VatAmount != captured.VatAmount ||
                current.AmountExcludingVat != captured.AmountExcludingVat ||
                current.AmountKzt != captured.GrossAmountKzt ||
                current.VatAmountKzt != captured.VatAmountKzt ||
                current.AmountExcludingVatKzt != captured.AmountExcludingVatKzt)
            {
                return false;
            }
        }
        return true;
    }

    internal static AccrualClosingCycleSnapshot Capture(
        Guid cycleId,
        int cycleNumber,
        DateTimeOffset capturedAtUtc,
        AccrualClosingSourceSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AccrualClosingCycleSnapshot(
            cycleId,
            cycleNumber,
            capturedAtUtc,
            source,
            source.GrossAmount,
            source.VatAmount,
            source.AmountExcludingVat,
            source.GrossAmountKzt,
            source.VatAmountKzt,
            source.AmountExcludingVatKzt);
    }

    internal static AccrualClosingCycleSnapshot Restore(
        Guid cycleId,
        int cycleNumber,
        Guid expenseRequestId,
        Guid organizationId,
        Guid costCenterId,
        Guid requestVersion,
        DateTimeOffset sourceUpdatedAtUtc,
        DateTimeOffset capturedAtUtc,
        decimal grossAmount,
        decimal vatAmount,
        decimal amountExcludingVat,
        decimal grossAmountKzt,
        decimal vatAmountKzt,
        decimal amountExcludingVatKzt,
        IEnumerable<AccrualClosingCycleLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        AccrualClosingSourceSnapshot source;
        try
        {
            source = new AccrualClosingSourceSnapshot(
                expenseRequestId,
                organizationId,
                costCenterId,
                requestVersion,
                sourceUpdatedAtUtc,
                lines.Select(line =>
                    line?.ToSourceLine() ?? throw InvariantViolation()));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw InvariantViolation();
        }

        return new AccrualClosingCycleSnapshot(
            cycleId,
            cycleNumber,
            capturedAtUtc,
            source,
            grossAmount,
            vatAmount,
            amountExcludingVat,
            grossAmountKzt,
            vatAmountKzt,
            amountExcludingVatKzt);
    }

    private static void EnsureStorageTime(DateTimeOffset value)
    {
        if (value == default ||
            value.Offset != TimeSpan.Zero ||
            value.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw InvariantViolation();
        }
    }

    private static InvalidOperationException InvariantViolation() => new(
        "Accrual closing cycle snapshot violates the internal invariant.");
}
