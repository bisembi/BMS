using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Payments;
using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

/// Read-only payment rows. Authority and every input to the reconciliation
/// are captured anew in each Serializable attempt, before paging the result.
public sealed class ExpenseRequestPaymentDetailsService(
    IExpenseRequestPaymentDetailsReader reader, ICurrentActor actor,
    EmployeeActorResolver employees, EmployeeFunctionalRoleResolver roles,
    IBusinessTransactionRunner transactions)
{
    public async Task<ExpenseRequestPaymentDetailsDto?> ReadAsync(
        Guid requestId, int skip, int take, CancellationToken cancellationToken)
    {
        var expectedActor = ActorIdentity.RequireBusinessDataAccess(actor);
        if (skip < 0 || take is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(skip));
        try
        {
            return await transactions.ExecuteAsync<ExpenseRequestPaymentDetailsDto?>(async token =>
            {
                var includeAll = await AuthorizeAsync(expectedActor, token);
                if (requestId == Guid.Empty) return null;
                var capture = await reader.ReadVisibleAsync(requestId, expectedActor, includeAll, token);
                if (capture is null) return null;
                if (capture.ExpenseRequestId != requestId || capture.CurrencyId == Guid.Empty
                    || capture.CurrencyCode is not { Length: 3 } currency
                    || currency.Any(c => c is < 'A' or > 'Z'))
                    throw new ExpenseRequestPaymentDetailsUnavailableException();

                // Reconcile the whole capture, even when this page is empty.
                // An orphan reversal or a wrong-currency fact is not "no payment".
                var ledger = PaymentFactLedgerReconciler.Reconcile(requestId, capture.CurrencyId, capture.Events);
                if (currency == "KZT" && capture.Events.Any(e => e.Amount != e.AmountKzt))
                    throw new ExpenseRequestPaymentDetailsUnavailableException();
                token.ThrowIfCancellationRequested();
                var items = capture.Events.OrderBy(e => e.PaidAt.UtcDateTime)
                    .ThenBy(e => e.EventId.ToString("D"), StringComparer.Ordinal)
                    .Skip(skip).Take(take)
                    .Select((e, index) => new ExpenseRequestPaymentDetailDto(checked(skip + index + 1),
                        e.PaidAt, string.IsNullOrWhiteSpace(e.PaymentNumber) ? null : e.PaymentNumber,
                        e.Amount, e.AmountKzt, e.IsReversal)).ToArray();
                token.ThrowIfCancellationRequested();
                return new(currency, capture.Events.Count, skip, take,
                    new(ledger.OriginalAmount, ledger.OriginalAmountKzt,
                        ledger.ReversedAmount, ledger.ReversedAmountKzt, ledger.ActiveAmount, ledger.ActiveAmountKzt),
                    Array.AsReadOnly(items));
            }, cancellationToken);
        }
        catch (Exception exception) when (!ProcessFatalExceptionClassifier.IsFatal(exception)
            && exception is not (OperationCanceledException or ActorNotAuthenticatedException
                or ActorForbiddenException or ExpenseRequestPaymentDetailsUnavailableException))
        {
            throw new ExpenseRequestPaymentDetailsUnavailableException(exception);
        }
    }

    private async Task<bool> AuthorizeAsync(string expectedActor, CancellationToken cancellationToken)
    {
        var current = ActorIdentity.RequireBusinessDataAccess(actor);
        if (!string.Equals(current, expectedActor, StringComparison.Ordinal)) throw new ActorForbiddenException();
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await actor.ResolveBusinessDataReadScopeAsync(cancellationToken);
        if (!scope.HasAny) throw new ActorForbiddenException();
        if (scope.CanReadAll) return true;
        var employee = await employees.ResolveAsync(current, cancellationToken);
        if (!employee.HasActiveEmployee || employee.EmployeeId is not Guid employeeId)
            throw new ActorForbiddenException();
        var currentRoles = await roles.ResolveAsync(employeeId, cancellationToken);
        if (!currentRoles.HasExactRole(ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode)
            && !currentRoles.HasExactRole(ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode))
            throw new ActorForbiddenException();
        return false;
    }
}
