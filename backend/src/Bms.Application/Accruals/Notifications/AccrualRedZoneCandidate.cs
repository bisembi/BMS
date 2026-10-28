using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Accruals;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.Accruals.Notifications;

internal static class AccrualNotificationCodes
{
    internal const string RedZone = "ACCRUAL_RED_ZONE";
    internal const string MissingAccountant = "NO_RESPONSIBLE_ACCOUNTANT";
    internal const string MissingInitiator = "INITIATOR_MAPPING_UNAVAILABLE";
}

/// <summary>
/// Internal source facts, not a public notification DTO. One request/cycle may
/// have many qualified lines but produces one key per recipient, not per line.
/// </summary>
internal sealed record AccrualRedZoneCandidate(
    Guid ExpenseRequestId,
    Guid OrganizationId,
    Guid CycleId,
    string InitiatorExternalId,
    DateTimeOffset FullyPaidAtUtc,
    DateOnly PaymentBusinessDate,
    DateOnly AsOfDate,
    int AgeDays,
    int RedFromDays,
    decimal RemainingAmountKzt)
{
    internal static AccrualRedZoneCandidate? Evaluate(
        ExpenseRequest request,
        AccrualClosingCycleSnapshot? cycle,
        AccrualAgingContext aging)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(aging);
        try
        {
            return EvaluateSource(request, cycle, aging);
        }
        catch (Exception exception) when (exception is InvalidOperationException or
            ArgumentException or OverflowException)
        {
            // Malformed source evidence is an unavailable control, never an
            // ordinary non-red candidate or a public diagnostic message.
            throw new AccrualNotificationControlUnavailableException(exception);
        }
    }

    private static AccrualRedZoneCandidate? EvaluateSource(
        ExpenseRequest request,
        AccrualClosingCycleSnapshot? cycle,
        AccrualAgingContext aging)
    {
        if (request.Status != ExpenseRequestStatus.Paid)
        {
            return null;
        }

        var assessment = AccrualClosingSourceSnapshotBuilder.Capture(request);
        if (assessment.State == AccrualClosingSourceState.NoManagerialAccrualLines &&
            request.AccrualCycleId is null)
        {
            return null;
        }
        if (assessment.State != AccrualClosingSourceState.Ready ||
            cycle is null || request.AccrualCycleId != cycle.CycleId ||
            !cycle.MatchesRequest(request) ||
            request.FullyPaidAtUtc is not { } paidAt ||
            request.OrganizationId is not { } organizationId ||
            organizationId == Guid.Empty || !ActorIdentity.IsCanonical(request.InitiatorExternalId))
        {
            throw new AccrualNotificationControlUnavailableException();
        }

        var currentLines = request.Lines.ToDictionary(line => line.Id);
        decimal remainingKzt = 0m;
        foreach (var line in cycle.Lines)
        {
            var current = currentLines[line.LineId];
            if (current.ConfirmedClosedAmount < 0m ||
                current.ConfirmedClosedAmount > line.GrossAmount ||
                current.ConfirmedClosedAmountKzt < 0m ||
                current.ConfirmedClosedAmountKzt > line.GrossAmountKzt)
            {
                throw new AccrualNotificationControlUnavailableException();
            }
            remainingKzt = checked(remainingKzt + line.GrossAmountKzt - current.ConfirmedClosedAmountKzt);
        }
        if (remainingKzt == 0m)
        {
            return null;
        }

        var paymentDate = aging.GetPaymentBusinessDate(paidAt);
        var ageDays = aging.AsOfDate.DayNumber - paymentDate.DayNumber;
        if (aging.Thresholds.Classify(ageDays) != AccrualAgeZone.Red)
        {
            return null;
        }
        return new AccrualRedZoneCandidate(request.Id, organizationId, cycle.CycleId,
            request.InitiatorExternalId, paidAt, paymentDate, aging.AsOfDate, ageDays,
            aging.Thresholds.RedFromDays, remainingKzt);
    }
}
