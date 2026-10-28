using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Application.Accruals;

/// <summary>
/// Read-only projection of the currently unclosed balances of qualified
/// managerial-accrual lines. It uses the report's own visibility and never grants a
/// closing command by implication.
/// </summary>
public sealed class ActiveAccrualReportService(
    IActiveAccrualReportReader reader,
    ICurrentActor currentActor,
    IAccrualAgingPolicy agingPolicy)
{
    public async Task<ActiveAccrualPageDto> GetAsync(
        ActiveAccrualQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        query.Validate();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var aging = agingPolicy.GetCurrentContext()
                ?? throw new ActiveAccrualReportUnavailableException();
            var result = await reader.ReadAsync(
                actorExternalId,
                currentActor.HasFullBusinessDataAccess,
                query.Filter,
                query.Skip,
                query.Take,
                // The list is paged: its scope is as large as the data is, and
                // no page of it is ever built in full.
                maximumScopeRows: null,
                aging,
                cancellationToken);
            return Project(result, query.Skip, query.Take, aging, query.Filter.DetailLevel, query.Filter.Grouping);
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
        catch (InvalidActiveAccrualQueryException)
        {
            throw;
        }
        catch (ActiveAccrualReportUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ActiveAccrualReportUnavailableException(exception);
        }
    }

    internal static ActiveAccrualPageDto Project(
        ActiveAccrualReadResult result,
        int skip,
        int take,
        AccrualAgingContext aging,
        ActiveAccrualDetailLevel detailLevel = ActiveAccrualDetailLevel.Line,
        ActiveAccrualGrouping grouping = ActiveAccrualGrouping.None)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(aging);
        if (!Enum.IsDefined(grouping)
            || result.HasInvariantViolation
            || result.Total < 0
            || result.Rows.Count > take
            || result.Rows.Count > result.Total
            || (result.Rows.Count > 0 && skip + result.Rows.Count > result.Total))
        {
            throw new ActiveAccrualReportUnavailableException();
        }

        ValidateTotals(result.Totals);
        if (skip == 0 && result.Rows.Count == result.Total)
        {
            var originalAmountKzt = result.Rows.Sum(row => row.OriginalAmountKzt);
            var confirmedClosedAmountKzt = result.Rows.Sum(row => row.ConfirmedClosedAmountKzt);
            var remainingAmountKzt = result.Rows.Sum(row => row.RemainingAmountKzt);
            if (originalAmountKzt != result.Totals.OriginalAmountKzt
                || confirmedClosedAmountKzt != result.Totals.ConfirmedClosedAmountKzt
                || remainingAmountKzt != result.Totals.RemainingAmountKzt)
            {
                throw new ActiveAccrualReportUnavailableException();
            }
        }

        var seenLines = new HashSet<(Guid RequestId, int LineNumber)>();
        var seenRequests = new HashSet<Guid>();
        var seenGroups = new HashSet<Guid?>();
        Guid? previousGroup = null;
        string? previousGroupName = null;
        var items = new List<ActiveAccrualRowDto>(result.Rows.Count);
        foreach (var row in result.Rows)
        {
            if (row.IsRequestSummary != (detailLevel == ActiveAccrualDetailLevel.Request)
                || (row.IsRequestSummary
                    ? !seenRequests.Add(row.ExpenseRequestId)
                    : !seenLines.Add((row.ExpenseRequestId, row.LineSortOrder))))
            {
                throw new ActiveAccrualReportUnavailableException();
            }

            ValidateRow(row);
            var grouped = grouping != ActiveAccrualGrouping.None;
            var groupWasSpecified = grouping == ActiveAccrualGrouping.Counterparty
                ? row.CounterpartyWasSpecified : row.ContractWasSpecified;
            var groupName = grouping == ActiveAccrualGrouping.Counterparty
                ? row.CounterpartyName : row.ContractName;
            if (row.GroupIdentity == Guid.Empty
                || (!grouped && row.GroupIdentity is not null)
                || (grouped && row.GroupIdentity.HasValue != groupWasSpecified))
            {
                throw new ActiveAccrualReportUnavailableException();
            }

            var startsGroup = grouped && (items.Count == 0 || previousGroup != row.GroupIdentity);
            if (grouped && (startsGroup
                ? !seenGroups.Add(row.GroupIdentity)
                : !string.Equals(previousGroupName, groupName, StringComparison.Ordinal)))
            {
                // Page-local proof: A-B-A or a changing label inside one
                // identity is not a group. Only export captures the whole scope.
                throw new ActiveAccrualReportUnavailableException();
            }

            var businessDate = aging.GetPaymentBusinessDate(row.FullyPaidAtUtc);
            var ageDays = aging.AsOfDate.DayNumber - businessDate.DayNumber;
            if (items.Count > 0 && !startsGroup && ageDays > items[^1].AgeDays)
            {
                // Never repair a misordered SQL page by sorting it locally.
                throw new ActiveAccrualReportUnavailableException();
            }

            var organizationName = RequirePublicName(row.OrganizationName, 500);
            var costCenterName = RequirePublicName(row.CostCenterName, 500);
            var cashFlowItemName = RequirePublicName(row.CashFlowItemName, 500);
            var expenseItemName = RequirePublicName(row.ExpenseItemName, 500);
            var initiatorName = RequirePublicName(row.InitiatorName, 500);
            var accrualPeriodThrough = row.AccrualPeriodThrough ?? row.AccrualPeriod;
            items.Add(new ActiveAccrualRowDto(
                row.ExpenseRequestId,
                row.IsRequestSummary ? null : row.LineSortOrder,
                row.FullyPaidAtUtc,
                row.AccrualPeriod,
                row.CurrencyCode,
                organizationName,
                costCenterName,
                cashFlowItemName,
                expenseItemName,
                row.CounterpartyName,
                row.Purpose,
                row.ConfirmedClosedAmount > 0m ? "partiallyClosed" : "open",
                row.OriginalAmount,
                row.ConfirmedClosedAmount,
                row.RemainingAmount,
                row.OriginalAmountKzt,
                row.ConfirmedClosedAmountKzt,
                row.RemainingAmountKzt,
                businessDate,
                ageDays,
                AccrualAgingContext.PublicZone(aging.Thresholds.Classify(ageDays)),
                accrualPeriodThrough,
                row.LineCount,
                row.IsRequestSummary,
                row.ContractName,
                initiatorName,
                grouped ? startsGroup : null));
            previousGroup = row.GroupIdentity;
            previousGroupName = groupName;
        }

        return new ActiveAccrualPageDto(
            items.AsReadOnly(),
            skip,
            take,
            result.Total,
            new ActiveAccrualTotalsDto(
                result.Totals.OriginalAmountKzt,
                result.Totals.ConfirmedClosedAmountKzt,
                result.Totals.RemainingAmountKzt),
            new ActiveAccrualAgingDto(
                aging.AsOfDate,
                aging.Thresholds.YellowFromDays,
                aging.Thresholds.OrangeFromDays,
                aging.Thresholds.RedFromDays),
            grouping switch
            {
                ActiveAccrualGrouping.Counterparty => "counterparty",
                ActiveAccrualGrouping.Contract => "contract",
                _ => null
            });
    }

    private static void ValidateRow(ActiveAccrualRow row)
    {
        if (row.ExpenseRequestId == Guid.Empty
            || row.LineSortOrder <= 0
            || row.LineCount <= 0
            || (!row.IsRequestSummary && row.LineCount != 1)
            || row.FullyPaidAtUtc == default
            || row.FullyPaidAtUtc.Offset != TimeSpan.Zero
            || row.AccrualPeriod == default
            || row.AccrualPeriod.Day != 1
            || (row.AccrualPeriodThrough is { } through
                && (through.Day != 1 || through < row.AccrualPeriod))
            || row.CurrencyCode is not { Length: 3 }
            || row.CurrencyCode.Any(character => character is < 'A' or > 'Z')
            || !HasPublicName(row.OrganizationName, 500)
            || !HasPublicName(row.CostCenterName, 500)
            || !HasPublicName(row.CashFlowItemName, 500)
            || !HasPublicName(row.ExpenseItemName, 500)
            || (row.CounterpartyWasSpecified && !HasPublicName(row.CounterpartyName, 500))
            || (!row.CounterpartyWasSpecified && row.CounterpartyName is not null)
            || (row.ContractWasSpecified && !HasPublicName(row.ContractName, 500))
            || (!row.ContractWasSpecified && row.ContractName is not null)
            || !HasPublicName(row.InitiatorName, 500)
            || (row.Purpose is not null && !HasPublicPurpose(row.Purpose)))
        {
            throw new ActiveAccrualReportUnavailableException();
        }

        MoneyAmountPolicy.EnsurePositive(row.OriginalAmount, "Active accrual amount");
        MoneyAmountPolicy.EnsureNonNegative(
            row.ConfirmedClosedAmount,
            "Active accrual confirmed amount");
        MoneyAmountPolicy.EnsurePositive(row.RemainingAmount, "Active accrual remainder");
        MoneyAmountPolicy.EnsureNonNegative(row.OriginalAmountKzt, "Active accrual KZT amount");
        MoneyAmountPolicy.EnsureNonNegative(
            row.ConfirmedClosedAmountKzt,
            "Active accrual confirmed KZT amount");
        MoneyAmountPolicy.EnsureNonNegative(
            row.RemainingAmountKzt,
            "Active accrual KZT remainder");
        if (row.ConfirmedClosedAmount + row.RemainingAmount != row.OriginalAmount
            || row.ConfirmedClosedAmountKzt + row.RemainingAmountKzt
                != row.OriginalAmountKzt)
        {
            throw new ActiveAccrualReportUnavailableException();
        }
    }

    private static void ValidateTotals(ActiveAccrualTotals totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        MoneyAmountPolicy.EnsureNonNegative(totals.OriginalAmountKzt, "Active accrual total");
        MoneyAmountPolicy.EnsureNonNegative(
            totals.ConfirmedClosedAmountKzt,
            "Active accrual confirmed total");
        MoneyAmountPolicy.EnsureNonNegative(
            totals.RemainingAmountKzt,
            "Active accrual remaining total");
        if (totals.ConfirmedClosedAmountKzt + totals.RemainingAmountKzt
            != totals.OriginalAmountKzt)
        {
            throw new ActiveAccrualReportUnavailableException();
        }
    }

    // Purpose is textarea content, not a single-line reference name. Preserve
    // its allowed line breaks/tabs in the page and XLSX; other controls remain invalid.
    private static bool HasPublicPurpose(string value) =>
        value.Length is > 0 and <= 1_000 && value.Trim() == value
        && !value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'));

    private static bool HasPublicName(string? value, int maximumLength) =>
        value is not null
        && value.Length is > 0
        && value.Length <= maximumLength
        && value.Trim() == value
        && !value.Any(character => char.IsControl(character));

    private static string RequirePublicName(string? value, int maximumLength)
    {
        if (value is null || !HasPublicName(value, maximumLength))
        {
            throw new ActiveAccrualReportUnavailableException();
        }

        return value;
    }
}

public sealed record ActiveAccrualExportData(
    DateTimeOffset GeneratedAtUtc,
    ActiveAccrualFilter Filter,
    IReadOnlyList<ActiveAccrualRowDto> Rows,
    ActiveAccrualTotalsDto Totals,
    ActiveAccrualAgingDto Aging);

public static class ActiveAccrualExportPolicy
{
    public const int MaximumRows = 50_000;
}

public sealed class ActiveAccrualExportService(
    IActiveAccrualReportReader reader,
    ICurrentActor currentActor,
    IAccrualAgingPolicy agingPolicy)
{
    public async Task<ActiveAccrualExportData> ExportAsync(
        ActiveAccrualFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        filter.Validate();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var aging = agingPolicy.GetCurrentContext()
                ?? throw new ActiveAccrualReportUnavailableException();
            var result = await reader.ReadAsync(
                actorExternalId,
                currentActor.HasFullBusinessDataAccess,
                filter,
                0,
                ActiveAccrualExportPolicy.MaximumRows + 1,
                ActiveAccrualExportPolicy.MaximumRows,
                aging,
                cancellationToken);
            if (result.Total > ActiveAccrualExportPolicy.MaximumRows)
            {
                throw new ActiveAccrualExportTooLargeException(
                    result.Total,
                    ActiveAccrualExportPolicy.MaximumRows);
            }

            var page = ActiveAccrualReportService.Project(
                result,
                0,
                ActiveAccrualExportPolicy.MaximumRows + 1,
                aging,
                filter.DetailLevel,
                filter.Grouping);
            if (page.Items.Count != page.Total)
            {
                throw new ActiveAccrualReportUnavailableException();
            }

            return new ActiveAccrualExportData(
                aging.CapturedAtUtc,
                filter,
                page.Items,
                page.Totals,
                page.Aging);
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
        catch (InvalidActiveAccrualQueryException)
        {
            throw;
        }
        catch (ActiveAccrualExportTooLargeException)
        {
            throw;
        }
        catch (ActiveAccrualReportUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ActiveAccrualReportUnavailableException(exception);
        }
    }
}
