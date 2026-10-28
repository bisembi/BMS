using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Payments;

namespace Bms.Application.Treasury;

/// <summary>
/// One page of the payment calendar together with the totals of the whole
/// filtered scope, as the reader returns them. ConfirmedAmount is net bank
/// cash plus validated Applied credit, not reserved or released credit.
/// </summary>
public sealed record PaymentCalendarReadResult(
    IReadOnlyList<PaymentCalendarRow> Rows,
    int Total,
    PaymentCalendarTotals Totals)
{
    public IReadOnlyList<PaymentCalendarOrganizationGroup> OrganizationGroups { get; init; } = [];
}

public sealed record PaymentCalendarOrganizationGroup(
    Guid? OrganizationId,
    string? OrganizationCode,
    string? OrganizationName,
    int Total,
    PaymentCalendarTotals Totals);

public sealed record PaymentCalendarRow(
    Guid ExpenseRequestId,
    DateOnly PlannedPaymentDate,
    string Status,
    string CurrencyCode,
    string? CounterpartyName,
    string? Purpose,
    decimal ObligationAmount,
    decimal ConfirmedAmount)
{
    public Guid? OrganizationId { get; init; }
    public string? OrganizationCode { get; init; }
    public string? OrganizationName { get; init; }
    public decimal AppliedOverpaymentAmount { get; init; }
    public decimal ApprovedRoundingAmount { get; init; }
}

public sealed record PaymentCalendarTotals(
    decimal ObligationAmount,
    decimal PaidAmount,
    decimal RemainingAmount,
    decimal OverpaidAmount)
{
    public decimal AppliedOverpaymentAmount { get; init; }
    public decimal ApprovedRoundingAmount { get; init; }
    public decimal BankPaidAmount => PaidAmount - AppliedOverpaymentAmount - ApprovedRoundingAmount;
}

/// <summary>
/// Reads the payment calendar. The implementation owns the single bounded
/// query; this seam keeps the application layer free of EF Core and makes the
/// visibility contract explicit -- the reader is handed the actor and must
/// apply the same list visibility the expense-request list applies.
/// </summary>
public interface IPaymentCalendarReader
{
    Task<PaymentCalendarReadResult> ReadAsync(
        string actorExternalId,
        bool includeAll,
        PaymentCalendarQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListOpenableIdsAsync(
        string actorExternalId,
        bool includeAll,
        IReadOnlyCollection<Guid> expenseRequestIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// The read-only payment calendar: which requests are due to be paid in a
/// period, how much of each is already confirmed by the accounting ledger, and
/// what remains. It never writes, never registers a payment and never invents
/// a rate; every amount comes from stored facts.
/// </summary>
public sealed class PaymentCalendarService(
    IPaymentCalendarReader reader,
    ICurrentActor currentActor)
{
    public async Task<PaymentCalendarPageDto> GetAsync(
        PaymentCalendarQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Business-data access first: the integration administrator is denied
        // here, before anything about the period is read.
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        query.Validate();

        try
        {
            var includeAll = currentActor.HasFullBusinessDataAccess;
            var result = await reader.ReadAsync(
                actorExternalId,
                includeAll,
                query,
                cancellationToken);

            var rowIds = result.Rows.Select(row => row.ExpenseRequestId).ToArray();
            if (rowIds.Distinct().Count() != rowIds.Length)
            {
                throw new PaymentCalendarUnavailableException();
            }

            // canOpen comes from the same card-visibility source the list uses;
            // it is never derived from the status.
            var openableIds = await reader.ListOpenableIdsAsync(
                actorExternalId,
                includeAll,
                rowIds,
                cancellationToken);
            var openable = openableIds.ToHashSet();
            if (openable.Count != openableIds.Count
                || openable.Any(id => !rowIds.Contains(id)))
            {
                throw new PaymentCalendarUnavailableException();
            }

            var items = result.Rows
                .Select(row => ToDto(row, openable.Contains(row.ExpenseRequestId)))
                .ToArray();
            return new PaymentCalendarPageDto(
                items,
                query.Skip,
                query.Take,
                result.Total,
                ToTotals(result.Totals))
            {
                Grouping = query.Grouping,
                OrganizationGroups = ToOrganizationGroups(result, query.Grouping, cancellationToken),
            };
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
        catch (InvalidPaymentCalendarQueryException)
        {
            throw;
        }
        catch (PaymentCalendarUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PaymentCalendarUnavailableException(exception);
        }
    }

    internal static PaymentCalendarRowDto ToDto(PaymentCalendarRow row, bool canOpen)
    {
        ValidateOrganization(row.OrganizationId, row.OrganizationCode, row.OrganizationName);

        // Only the stored KZT circuit carries a trustworthy settlement split;
        // there is no approved rule for dividing banked KZT against a foreign
        // obligation, so anything else fails closed rather than showing a
        // plausible-looking number.
        if (!string.Equals(row.CurrencyCode, "KZT", StringComparison.Ordinal)
            || !PaymentCalendarStatusCodes.IsAllowed(row.Status)
            || row.ObligationAmount <= 0m
            || row.ConfirmedAmount < 0m
            || !IsValidAppliedAmount(row.ApprovedRoundingAmount,row.ConfirmedAmount-row.AppliedOverpaymentAmount)
            || !IsValidAppliedAmount(row.AppliedOverpaymentAmount,
                decimal.Min(row.ConfirmedAmount, row.ObligationAmount)))
        {
            throw new PaymentCalendarUnavailableException();
        }

        // The reader supplies cash plus Applied credit and refuses credit
        // above the obligation. Capping their sum is exactly equivalent to
        // the card's applied + min(cash, obligation - applied). Cash excess
        // belongs to the source, never to another request's obligation.
        var paid = decimal.Min(row.ConfirmedAmount, row.ObligationAmount);
        var progress = paid switch
        {
            0m => PaymentSettlementProgress.Unpaid,
            _ when paid == row.ObligationAmount => PaymentSettlementProgress.FullySettled,
            _ => PaymentSettlementProgress.PartiallySettled,
        };

        return new PaymentCalendarRowDto(
            row.ExpenseRequestId,
            row.PlannedPaymentDate,
            row.Status,
            row.CurrencyCode,
            row.CounterpartyName,
            row.Purpose,
            row.ObligationAmount,
            paid,
            row.ObligationAmount - paid,
            ExpenseRequestPaymentProgressCode.FromProgress(progress),
            canOpen)
        {
            OrganizationId = row.OrganizationId,
            OrganizationCode = row.OrganizationCode,
            OrganizationName = row.OrganizationName,
            AppliedOverpaymentAmount = row.AppliedOverpaymentAmount,
            ApprovedRoundingAmount = row.ApprovedRoundingAmount,
        };
    }

    /// <summary>
    /// Projects full-scope groups, never page sums. Page rows only establish
    /// membership, contiguous order and lower bounds; equality is required
    /// when all rows of a group (or the whole period) are represented. The
    /// same linear pass also validates the bounded whole-workbook capture.
    /// </summary>
    internal static IReadOnlyList<PaymentCalendarOrganizationGroupDto> ToOrganizationGroups(
        PaymentCalendarReadResult capture,
        string grouping,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(capture.OrganizationGroups);
        if (grouping == PaymentCalendarGroupingCodes.Date)
        {
            if (capture.OrganizationGroups.Count != 0)
            {
                throw new PaymentCalendarUnavailableException();
            }
            return [];
        }

        if (grouping != PaymentCalendarGroupingCodes.Organization
            || capture.Total < 0 || capture.Rows.Count > capture.Total)
        {
            throw new PaymentCalendarUnavailableException();
        }
        _ = ToTotals(capture.Totals);

        var visible = new List<PaymentCalendarOrganizationGroup>();
        var seen = new HashSet<Guid>();
        foreach (var row in capture.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateOrganization(row.OrganizationId, row.OrganizationCode, row.OrganizationName);
            if (row.ObligationAmount <= 0m || row.ConfirmedAmount < 0m
                || decimal.Round(row.ObligationAmount, 2) != row.ObligationAmount
                || decimal.Round(row.ConfirmedAmount, 2) != row.ConfirmedAmount
                || !IsValidAppliedAmount(row.ApprovedRoundingAmount,row.ConfirmedAmount-row.AppliedOverpaymentAmount)
                || !IsValidAppliedAmount(row.AppliedOverpaymentAmount,
                    decimal.Min(row.ConfirmedAmount, row.ObligationAmount)))
            {
                throw new PaymentCalendarUnavailableException();
            }

            if (visible.Count == 0 || visible[^1].OrganizationId != row.OrganizationId)
            {
                // Empty is reserved for the genuinely missing ID; an actual
                // Guid.Empty is rejected by ValidateOrganization above.
                if (!seen.Add(row.OrganizationId ?? Guid.Empty))
                {
                    throw new PaymentCalendarUnavailableException();
                }
                visible.Add(new PaymentCalendarOrganizationGroup(
                    row.OrganizationId, row.OrganizationCode, row.OrganizationName,
                    0, new PaymentCalendarTotals(0, 0, 0, 0)));
            }

            var group = visible[^1];
            if (group.OrganizationCode != row.OrganizationCode || group.OrganizationName != row.OrganizationName)
            {
                throw new PaymentCalendarUnavailableException();
            }
            var paid = decimal.Min(row.ConfirmedAmount, row.ObligationAmount);
            visible[^1] = group with
            {
                Total = group.Total + 1,
                Totals = AddTotals(group.Totals, new PaymentCalendarTotals(
                    row.ObligationAmount, paid, row.ObligationAmount - paid, row.ConfirmedAmount - paid)
                {
                    AppliedOverpaymentAmount = row.AppliedOverpaymentAmount,
                    ApprovedRoundingAmount = row.ApprovedRoundingAmount,
                }),
            };
        }

        if (visible.Count != capture.OrganizationGroups.Count)
        {
            throw new PaymentCalendarUnavailableException();
        }

        var result = new List<PaymentCalendarOrganizationGroupDto>(visible.Count);
        long representedCount = 0;
        var representedTotals = new PaymentCalendarTotals(0, 0, 0, 0);
        for (var index = 0; index < visible.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageGroup = visible[index];
            var group = capture.OrganizationGroups[index];
            var totals = ToTotals(group.Totals);
            if (group.OrganizationId != pageGroup.OrganizationId
                || group.OrganizationCode != pageGroup.OrganizationCode
                || group.OrganizationName != pageGroup.OrganizationName
                || group.Total < pageGroup.Total
                || group.Total > capture.Total
                || !IncludesTotals(group.Totals, pageGroup.Totals)
                || (group.Total == pageGroup.Total && group.Totals != pageGroup.Totals))
            {
                throw new PaymentCalendarUnavailableException();
            }
            representedCount += group.Total;
            representedTotals = AddTotals(representedTotals, group.Totals);
            result.Add(new PaymentCalendarOrganizationGroupDto(
                group.OrganizationId, group.OrganizationCode, group.OrganizationName, group.Total, totals));
        }

        if (representedCount > capture.Total
            || !IncludesTotals(capture.Totals, representedTotals)
            || (representedCount == capture.Total && representedTotals != capture.Totals))
        {
            throw new PaymentCalendarUnavailableException();
        }
        return result;
    }

    private static void ValidateOrganization(Guid? id, string? code, string? name)
    {
        if (id == Guid.Empty || (code is null) != (name is null)
            || (id is null && code is not null)
            || (code is not null && (!IsSafeLabel(code, 100) || !IsSafeLabel(name, 500))))
        {
            throw new PaymentCalendarUnavailableException();
        }
    }

    private static bool IsSafeLabel(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !value.Any(char.IsControl);

    private static PaymentCalendarTotals AddTotals(PaymentCalendarTotals left, PaymentCalendarTotals right) =>
        new(left.ObligationAmount + right.ObligationAmount, left.PaidAmount + right.PaidAmount,
            left.RemainingAmount + right.RemainingAmount, left.OverpaidAmount + right.OverpaidAmount)
        {
            AppliedOverpaymentAmount = left.AppliedOverpaymentAmount + right.AppliedOverpaymentAmount,
            ApprovedRoundingAmount = left.ApprovedRoundingAmount + right.ApprovedRoundingAmount,
        };

    private static bool IncludesTotals(PaymentCalendarTotals whole, PaymentCalendarTotals part) =>
        whole.ObligationAmount >= part.ObligationAmount && whole.PaidAmount >= part.PaidAmount
        && whole.RemainingAmount >= part.RemainingAmount && whole.OverpaidAmount >= part.OverpaidAmount
        && whole.AppliedOverpaymentAmount >= part.AppliedOverpaymentAmount
        && whole.ApprovedRoundingAmount >= part.ApprovedRoundingAmount
        && whole.BankPaidAmount >= part.BankPaidAmount;

    private static bool IsValidAppliedAmount(decimal applied, decimal paid) =>
        applied >= 0m && applied <= paid && decimal.Round(applied, 2) == applied;

    private static PaymentCalendarTotalsDto ToTotals(PaymentCalendarTotals totals)
    {
        if (totals.ObligationAmount < 0m
            || totals.PaidAmount < 0m
            || totals.RemainingAmount < 0m
            || totals.OverpaidAmount < 0m
            || totals.PaidAmount + totals.RemainingAmount != totals.ObligationAmount
            || !IsValidAppliedAmount(totals.AppliedOverpaymentAmount, totals.PaidAmount)
            || !IsValidAppliedAmount(totals.ApprovedRoundingAmount,totals.BankPaidAmount+totals.ApprovedRoundingAmount))
        {
            throw new PaymentCalendarUnavailableException();
        }

        return new PaymentCalendarTotalsDto(
            totals.ObligationAmount,
            totals.PaidAmount,
            totals.RemainingAmount,
            totals.OverpaidAmount)
        {
            AppliedOverpaymentAmount = totals.AppliedOverpaymentAmount,
            ApprovedRoundingAmount = totals.ApprovedRoundingAmount,
        };
    }
}
