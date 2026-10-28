using System.Globalization;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Payments;

namespace Bms.Application.ExpenseRequests;

/// <summary>Display-only columns, without contract, payment or accrual identities.
/// Money uses exact canonical decimal text, never an approximate JSON number.</summary>
public sealed record ExpenseRequestListBusinessColumnsDto(
    string? ContractName,
    string? PaymentBasisCode,
    string ObligationAmount,
    string? ObligationAmountKzt,
    string PaidAmount,
    string RemainingAmount,
    DateOnly? FullyPaidDate,
    string ClosingState);

/// <summary>Pure projection of the captured request and its entire confirmed ledger.
/// It neither reads storage nor activates payment intake or closing commands.</summary>
public static class ExpenseRequestListBusinessColumnsProjector
{
    public static ExpenseRequestListBusinessColumnsDto Project(
        ExpenseRequest request, Guid currencyId, string? contractName,
        IReadOnlyList<ConfirmedPaymentEvent> events) =>
        Project(request, currencyId, contractName, events, []);

    public static ExpenseRequestListBusinessColumnsDto Project(
        ExpenseRequest request, Guid currencyId, string? contractName,
        IReadOnlyList<ConfirmedPaymentEvent> events,
        IReadOnlyList<ExpenseRequestAppliedOverpaymentSnapshot> allocations,
        IReadOnlyList<ApprovedPaymentRounding400>? approvedRounding = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(allocations);
        if (contractName is { } name &&
            (string.IsNullOrWhiteSpace(name) || name.Length > 300 || name.Any(char.IsControl)))
            throw new ExpenseRequestListBusinessColumnsUnavailableException();
        if (request.PaymentBasisCode is { } basis && !Enum.IsDefined(basis))
            throw new ExpenseRequestListBusinessColumnsUnavailableException();
        if (request.Status == ExpenseRequestStatus.Closed && request.ClosedPartially)
            throw new ExpenseRequestListBusinessColumnsUnavailableException();

        ExpenseRequestSettlementProjection settlement;
        try
        {
            settlement = ExpenseRequestSettlementProjector.Project(
                request, currencyId, events, allocations, approvedRounding);
        }
        catch (ExpenseRequestSettlementProjectionException exception)
        {
            throw new ExpenseRequestListBusinessColumnsUnavailableException(exception);
        }
        return new(contractName, request.PaymentBasisCode?.ToString(),
            MoneyText(settlement.ObligationAmount),
            request.TotalAmountKzt is { } kzt ? MoneyText(kzt) : null,
            MoneyText(settlement.SettledAmount), MoneyText(settlement.RemainingAmount),
            settlement.FullyPaidDate,
            request.Status == ExpenseRequestStatus.Closed ? "closed" :
                request.ClosedPartially ? "partiallyClosed" : "notClosed");
    }

    private static string MoneyText(decimal amount)
    {
        MoneyAmountPolicy.EnsureNonNegative(amount, "List business amount");
        return amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

}

public sealed class ExpenseRequestListBusinessColumnsUnavailableException : Exception
{
    public ExpenseRequestListBusinessColumnsUnavailableException()
        : base("Expense-request list business columns are temporarily unavailable.") { }

    public ExpenseRequestListBusinessColumnsUnavailableException(Exception inner)
        : base("Expense-request list business columns are temporarily unavailable.", inner) { }
}
