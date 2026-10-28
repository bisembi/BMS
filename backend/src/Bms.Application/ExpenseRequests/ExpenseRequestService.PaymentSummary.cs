using Bms.Application.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// Read-only payment summary of one expense request.
public sealed partial class ExpenseRequestService
{
    /// <summary>
    /// Returns how much of the request is settled by confirmed cash plus an
    /// applied overpayment. Visibility is exactly the visibility of the
    /// request itself, so a reader who cannot see the request cannot learn its
    /// payment state, and
    /// the integration administrator gains no business data. Returns null when
    /// the request is not visible; throws the fixed unavailable failure when
    /// the settlement evidence cannot be read, because reporting an outage as
    /// "not paid" would be a lie about money.
    /// </summary>
    public async Task<ExpenseRequestPaymentSummaryDto?> GetPaymentSummaryAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        try
        {
            var includeAll = currentActor.HasFullBusinessDataAccess;
            return await transactionRunner.ExecuteAsync<ExpenseRequestPaymentSummaryDto?>(
                async attemptCancellationToken =>
                {
                    var request = await repository.FindVisibleAsync(
                        id,
                        actorExternalId,
                        includeAll,
                        attemptCancellationToken);
                    if (request is null)
                    {
                        return null;
                    }

                    var currencyId = await paymentFactReader.FindCurrencyIdAsync(
                        request.CurrencyCode,
                        attemptCancellationToken);
                    if (currencyId is not { } resolvedCurrencyId)
                    {
                        throw new ExpenseRequestPaymentSummaryUnavailableException();
                    }

                    var events = await paymentFactReader.ReadForRequestAsync(
                        request.Id,
                        resolvedCurrencyId,
                        attemptCancellationToken);
                    var allocations = await paymentFactReader
                        .ReadOverpaymentAllocationsForRequestAsync(
                            request.Id,
                            attemptCancellationToken);
                    var rounding = await paymentFactReader.ReadApprovedRoundingAsync(request.Id, attemptCancellationToken);
                    var settlement = ExpenseRequestSettlementProjector.Project(
                        request,
                        resolvedCurrencyId,
                        events,
                        allocations, rounding);

                    return new ExpenseRequestPaymentSummaryDto(
                        request.CurrencyCode,
                        settlement.ObligationAmount,
                        settlement.SettledAmount,
                        settlement.RemainingAmount,
                        ExpenseRequestPaymentProgressCode.FromProgress(settlement.Progress),
                        settlement.HasConfirmedPaymentFacts,
                        settlement.FullyPaidDate,
                        settlement.ForeignCurrencyKzt?.ObligationAmountKzt,
                        settlement.ForeignCurrencyKzt?.PaidAmountKzt,
                        settlement.ForeignCurrencyKzt?.BankPaymentAmountKzt,
                        settlement.ForeignCurrencyKzt?.CurrencyDeviationKzt)
                    {
                        ApprovedRoundingAmount = rounding.Count > 0 ? rounding.Sum(r => r.Amount) : null,
                        CashPaymentAmount = rounding.Count > 0 ? settlement.CashPaymentAmount : null,
                    };
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ActorNotAuthenticatedException)
        {
            throw;
        }
        catch (ActorForbiddenException)
        {
            throw;
        }
        catch (ExpenseRequestPaymentSummaryUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ExpenseRequestPaymentSummaryUnavailableException(exception);
        }
    }
}
