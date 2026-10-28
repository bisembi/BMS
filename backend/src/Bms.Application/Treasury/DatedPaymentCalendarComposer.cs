using Bms.Domain.ExpenseRequests;

namespace Bms.Application.Treasury;

/// <summary>
/// Composes a complete period report with bounded rows and request count.
/// The reader must separately bound evidence and lines before materialization.
/// Authorization, exact request-scope selection and snapshot isolation belong
/// to the reader/service. Every supplied ledger is validated before optional
/// status/currency filtering. No monetary calculation is based on page rows.
/// </summary>
public static class DatedPaymentCalendarComposer
{
    public static DatedPaymentCalendarReport Compose(
        DatedPaymentCalendarQuery query,
        TimeZoneInfo businessTimeZone,
        IReadOnlyList<DatedPaymentCalendarRequestCapture> captures,
        CancellationToken cancellationToken) =>
        Compose(query, businessTimeZone, captures, PaymentCalendarExportPolicy.MaximumRows, cancellationToken);

    // The public ceiling is fixed. A lower internal ceiling allows addressed
    // boundary tests without allocating 100,000 domain aggregates per test.
    internal static DatedPaymentCalendarReport Compose(
        DatedPaymentCalendarQuery query,
        TimeZoneInfo businessTimeZone,
        IReadOnlyList<DatedPaymentCalendarRequestCapture> captures,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(businessTimeZone);
        ArgumentNullException.ThrowIfNull(captures);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumRows is < 1 or > PaymentCalendarExportPolicy.MaximumRows)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRows));
        }
        if (captures.Count > maximumRows)
        {
            throw new DatedPaymentCalendarScopeTooLargeException();
        }

        var period = new DatedPaymentCalendarPeriod(query.From, query.Through, businessTimeZone);
        var requestIds = new HashSet<Guid>();
        var organizations = new Dictionary<Guid, (string? Code, string? Name)>();
        var currencies = new Dictionary<Guid, string>();
        var currencyIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var rows = new List<OrderedRow>();
        foreach (var capture in captures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(capture);
            ArgumentNullException.ThrowIfNull(capture.Request);
            var request = capture.Request;
            if (!requestIds.Add(request.Id) || !PaymentCalendarStatusCodes.AllowedStatuses.Contains(request.Status))
            {
                throw new DatedPaymentCalendarProjectionException();
            }
            if (requestIds.Count > maximumRows)
            {
                throw new DatedPaymentCalendarScopeTooLargeException();
            }

            ValidateMetadata(capture, organizations, currencies, currencyIds);
            var projection = DatedPaymentCalendarProjector.Project(
                request, capture.CurrencyId, period, capture.Events, capture.Allocations, cancellationToken);
            var status = request.Status.ToCode();
            if ((query.StatusCode is not null && query.StatusCode != status)
                || (query.CurrencyCode is not null && query.CurrencyCode != projection.CurrencyCode))
            {
                continue;
            }

            var ordinal = 0;
            if (projection.Plan is { } plan)
            {
                AddRow(rows, maximumRows, capture, status, ordinal++, plan.BusinessDate, DatedPaymentCalendarRowKindCodes.Plan,
                    plan.Amount, plan.AmountKzt, null, null);
            }

            foreach (var movement in projection.Movements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var kind = movement.Kind switch
                {
                    DatedPaymentCalendarMovementKind.BankPayment => DatedPaymentCalendarRowKindCodes.BankPayment,
                    DatedPaymentCalendarMovementKind.BankReversal => DatedPaymentCalendarRowKindCodes.BankReversal,
                    DatedPaymentCalendarMovementKind.AppliedAllocation => DatedPaymentCalendarRowKindCodes.AppliedAllocation,
                    _ => throw new DatedPaymentCalendarProjectionException(),
                };
                AddRow(rows, maximumRows, capture, status, ordinal++, movement.BusinessDate, kind,
                    null, null, movement.SignedAmount, movement.SignedAmountKzt);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Ordinal preserves the per-request projector's date/UTC/source order.
        // All tie breakers are ordinal and independent of database/input order.
        var ordered = query.Grouping == PaymentCalendarGroupingCodes.Organization
            ? rows.OrderBy(row => row.Row.OrganizationId is null)
                .ThenBy(row => row.Row.OrganizationCode, StringComparer.Ordinal)
                .ThenBy(row => row.Row.OrganizationId?.ToString("D"), StringComparer.Ordinal)
                .ThenBy(row => row.Row.BusinessDate)
            : rows.OrderBy(row => row.Row.BusinessDate);
        var result = ordered.ThenBy(row => row.Row.CurrencyCode, StringComparer.Ordinal)
            .ThenBy(row => row.Row.ExpenseRequestId.ToString("D"), StringComparer.Ordinal)
            .ThenBy(row => row.Ordinal).Select(row => row.Row).ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var totals = Sum(result, cancellationToken);
        var groups = new List<DatedPaymentCalendarOrganizationGroupDto>();
        if (query.Grouping == PaymentCalendarGroupingCodes.Organization)
        {
            foreach (var organization in result.GroupBy(row => row.OrganizationId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var groupRows = organization.ToArray();
                var first = groupRows[0];
                var groupTotals = Sum(groupRows, cancellationToken);
                groups.Add(new DatedPaymentCalendarOrganizationGroupDto(
                    first.OrganizationId, first.OrganizationCode, first.OrganizationName,
                    groupRows.Length, groupTotals.Currencies, groupTotals.Kzt));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new DatedPaymentCalendarReport(
            query, businessTimeZone.Id, Array.AsReadOnly(result), totals.Currencies, totals.Kzt,
            Array.AsReadOnly(groups.ToArray()));
    }

    internal static void ValidateMetadata(
        DatedPaymentCalendarRequestCapture capture,
        Dictionary<Guid, (string? Code, string? Name)> organizations,
        Dictionary<Guid, string> currencies,
        Dictionary<string, Guid> currencyIds)
    {
        var id = capture.Request.OrganizationId;
        var code = capture.OrganizationCode;
        var name = capture.OrganizationName;
        // Same label-shape contract as the existing calendar. Do not apply it
        // to Purpose: domain-valid multiline free text is not a directory code.
        if (id == Guid.Empty || (code is null) != (name is null)
            || (id is null && code is not null)
            || (code is not null && (!IsSafeLabel(code, 100) || !IsSafeLabel(name, 500))))
        {
            throw new DatedPaymentCalendarProjectionException();
        }

        var organizationKey = id ?? Guid.Empty;
        if (organizations.TryGetValue(organizationKey, out var labels) && labels != (code, name))
        {
            throw new DatedPaymentCalendarProjectionException();
        }
        organizations[organizationKey] = (code, name);

        var currencyCode = capture.Request.CurrencyCode;
        if (capture.CurrencyId == Guid.Empty || currencyCode.Length != 3
            || currencyCode.Any(character => character is < 'A' or > 'Z')
            || (currencies.TryGetValue(capture.CurrencyId, out var previousCode) && previousCode != currencyCode)
            || (currencyIds.TryGetValue(currencyCode, out var previousId) && previousId != capture.CurrencyId))
        {
            throw new DatedPaymentCalendarProjectionException();
        }
        currencies[capture.CurrencyId] = currencyCode;
        currencyIds[currencyCode] = capture.CurrencyId;
    }

    private static bool IsSafeLabel(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !value.Any(char.IsControl);

    private static void AddRow(
        List<OrderedRow> rows, int maximumRows, DatedPaymentCalendarRequestCapture capture, string status,
        int ordinal, DateOnly businessDate, string kind, decimal? plan, decimal? planKzt,
        decimal? amount, decimal? amountKzt)
    {
        if (rows.Count >= maximumRows)
        {
            // A partial report must not masquerade as the complete scope.
            throw new DatedPaymentCalendarScopeTooLargeException();
        }
        rows.Add(new OrderedRow(new DatedPaymentCalendarRowDto(
            businessDate, kind, capture.Request.Id, status, capture.Request.CurrencyCode,
            capture.CounterpartyName, capture.Request.Purpose, capture.Request.OrganizationId,
            capture.OrganizationCode, capture.OrganizationName, plan, planKzt, amount, amountKzt,
            capture.CanOpen), ordinal));
    }

    private static (IReadOnlyList<DatedPaymentCalendarCurrencyTotalsDto> Currencies, DatedPaymentCalendarKztTotalsDto Kzt) Sum(
        IReadOnlyList<DatedPaymentCalendarRowDto> rows, CancellationToken cancellationToken)
    {
        var kzt = new TotalsAccumulator();
        var currencies = new SortedDictionary<string, TotalsAccumulator>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!currencies.TryGetValue(row.CurrencyCode, out var currency))
            {
                currency = new TotalsAccumulator();
                currencies.Add(row.CurrencyCode, currency);
            }
            currency.Add(row, inKzt: false);
            kzt.Add(row, inKzt: true);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return (
            Array.AsReadOnly(currencies.Select(pair => pair.Value.AsCurrency(pair.Key)).ToArray()),
            kzt.AsKzt());
    }

    private sealed record OrderedRow(DatedPaymentCalendarRowDto Row, int Ordinal);

    private sealed class TotalsAccumulator
    {
        private int _planCount;
        private int _unknownPlanCount;
        private decimal _plan;
        private decimal _bank;
        private decimal _reversed;
        private decimal _applied;
        private decimal _movement;

        public void Add(DatedPaymentCalendarRowDto row, bool inKzt)
        {
            try
            {
                checked
                {
                    if (row.Kind == DatedPaymentCalendarRowKindCodes.Plan)
                    {
                        _planCount++;
                        var plan = inKzt ? row.PlanAmountKzt : row.PlanAmount;
                        if (plan is { } known)
                        {
                            _plan += known;
                        }
                        else
                        {
                            _unknownPlanCount++;
                        }
                        return;
                    }

                    var amount = (inKzt ? row.SignedAmountKzt : row.SignedAmount)
                        ?? throw new DatedPaymentCalendarProjectionException();
                    switch (row.Kind)
                    {
                        case DatedPaymentCalendarRowKindCodes.BankPayment: _bank += amount; break;
                        case DatedPaymentCalendarRowKindCodes.BankReversal: _reversed += amount; break;
                        case DatedPaymentCalendarRowKindCodes.AppliedAllocation: _applied += amount; break;
                        default: throw new DatedPaymentCalendarProjectionException();
                    }
                    _movement += amount;
                }
            }
            catch (OverflowException)
            {
                throw new DatedPaymentCalendarProjectionException();
            }
        }

        // Sources are bounded money values; a report over many requests is not
        // one decimal(18,2) storage cell. Only CLR decimal overflow is refused.
        public DatedPaymentCalendarCurrencyTotalsDto AsCurrency(string code) =>
            new(code, _planCount, _plan, _bank, _reversed, _applied, _movement);

        public DatedPaymentCalendarKztTotalsDto AsKzt() =>
            new(_planCount, _unknownPlanCount, _unknownPlanCount == 0 ? _plan : null,
                _bank, _reversed, _applied, _movement);
    }
}
