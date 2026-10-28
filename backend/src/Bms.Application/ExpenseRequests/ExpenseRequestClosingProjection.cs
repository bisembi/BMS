using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

internal static class ExpenseRequestClosingProjection
{
    internal static ExpenseRequestClosingStateDto FromDomain(
        ExpenseRequest request,
        bool canExecuteClosingCommands)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Id == Guid.Empty ||
            request.ConcurrencyStamp == Guid.Empty ||
            !Enum.IsDefined(request.Status) ||
            request.CurrencyCode is not { Length: 3 } ||
            request.CurrencyCode.Any(character => character is < 'A' or > 'Z'))
        {
            throw InvariantViolation();
        }

        MoneyAmountPolicy.EnsureNonNegative(
            request.ConfirmedClosingControlAmount,
            "Closing control amount");
        var lines = new List<ExpenseRequestClosingLineDto>();
        foreach (var line in request.Lines.OrderBy(item => item.SortOrder))
        {
            if (line.AccrualSnapshotFormsManagerialAccrual != true)
            {
                if (line.ConfirmedClosedAmount != 0m ||
                    line.ConfirmedClosedAmountKzt != 0m)
                {
                    throw InvariantViolation();
                }

                continue;
            }

            if (line.Id == Guid.Empty ||
                line.SortOrder <= 0 ||
                line.AccrualSnapshotExpenseItemId is null ||
                line.AmountKzt is not { } amountKzt)
            {
                throw InvariantViolation();
            }

            MoneyAmountPolicy.EnsurePositive(line.Amount, "Closing source amount");
            MoneyAmountPolicy.EnsureNonNegative(amountKzt, "Closing source KZT amount");
            MoneyAmountPolicy.EnsureNonNegative(
                line.ConfirmedClosedAmount,
                "Confirmed closing amount");
            MoneyAmountPolicy.EnsureNonNegative(
                line.ConfirmedClosedAmountKzt,
                "Confirmed closing KZT amount");
            var remaining = line.Amount - line.ConfirmedClosedAmount;
            var remainingKzt = amountKzt - line.ConfirmedClosedAmountKzt;
            if (remaining < 0m ||
                remainingKzt < 0m ||
                (remaining == 0m && remainingKzt != 0m))
            {
                throw InvariantViolation();
            }

            lines.Add(new ExpenseRequestClosingLineDto(
                line.Id,
                line.SortOrder,
                line.Amount,
                amountKzt,
                line.ConfirmedClosedAmount,
                line.ConfirmedClosedAmountKzt,
                remaining,
                remainingKzt));
        }

        return new ExpenseRequestClosingStateDto(
            request.Id,
            request.Status.ToCode(),
            request.CurrencyCode,
            request.FullyPaidAtUtc,
            request.LatestClosingDocumentsReceivedDate,
            request.ConfirmedClosingControlAmount,
            request.ClosedPartially,
            canExecuteClosingCommands,
            request.ConcurrencyStamp,
            lines.AsReadOnly());
    }

    private static InvalidOperationException InvariantViolation() => new(
        "Expense-request closing projection violates the internal invariant.");
}
