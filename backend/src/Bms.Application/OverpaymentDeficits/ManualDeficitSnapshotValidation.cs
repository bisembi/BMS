using Bms.Domain.Common;
using Bms.Domain.Overpayments;

namespace Bms.Application.OverpaymentDeficits;

internal static class ManualDeficitSnapshotValidation
{
    internal static void Require(OverpaymentDeficitSnapshot? row)
    {
        if (row is null || row.DeficitId == Guid.Empty || row.OrganizationId == Guid.Empty
            || row.CounterpartyId == Guid.Empty || row.CurrencyCode is not { Length: 3 }
            || row.CurrencyCode.Any(c => c is < 'A' or > 'Z')
            || row.OccurredAtUtc == default || row.OccurredAtUtc.Offset != TimeSpan.Zero
            || row.OccurredAtUtc.Ticks % TimeSpan.TicksPerMillisecond != 0)
            throw new ManualDeficitCorrectionUnavailableException();
        try
        {
            MoneyAmountPolicy.EnsurePositive(row.OriginalAmount, "Original deficit");
            MoneyAmountPolicy.EnsurePositive(row.OriginalAmountKzt, "Original KZT deficit");
            MoneyAmountPolicy.EnsureNonNegative(row.CurrentAmount, "Current deficit");
            MoneyAmountPolicy.EnsureNonNegative(row.CurrentAmountKzt, "Current KZT deficit");
        }
        catch (DomainRuleViolationException exception)
        {
            throw new ManualDeficitCorrectionUnavailableException(exception);
        }
        if (row.CurrentAmount > row.OriginalAmount || row.CurrentAmountKzt > row.OriginalAmountKzt
            || ((row.CurrentAmount == 0m) != (row.CurrentAmountKzt == 0m))
            || (row.CurrencyCode == "KZT" && (row.OriginalAmount != row.OriginalAmountKzt
                || row.CurrentAmount != row.CurrentAmountKzt)))
            throw new ManualDeficitCorrectionUnavailableException();
    }
}
