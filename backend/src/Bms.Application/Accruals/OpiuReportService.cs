using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Application.Accruals;

public sealed record OpiuQuery(DateOnly From, DateOnly Through, Guid? OrganizationId = null,
    Guid? CostCenterId = null, Guid? ExpenseItemId = null, int Skip = 0, int Take = 50)
{
    public void Validate()
    {
        if (From.Day != 1 || Through.Day != 1 || From.Year < 2000 || Through.Year > 2099
            || Through < From || (Through.Year - From.Year) * 12 + Through.Month - From.Month >= 24
            || OrganizationId == Guid.Empty || CostCenterId == Guid.Empty || ExpenseItemId == Guid.Empty
            || Skip < 0 || Take is < 1 or > 100)
            throw new InvalidActiveAccrualQueryException();
    }
}

public sealed record OpiuArticleDto(Guid Id, string Name, IReadOnlyList<decimal> Months, decimal Total);
public sealed record OpiuDepartmentDto(Guid Id, string Name, IReadOnlyList<OpiuArticleDto> Articles,
    IReadOnlyList<decimal> Months, decimal Total);
public sealed record OpiuReportDto(DateTimeOffset CapturedAtUtc, IReadOnlyList<DateOnly> Months,
    IReadOnlyList<OpiuDepartmentDto> Departments, IReadOnlyList<decimal> Totals, decimal Total,
    int LineCount, string Basis = "currentUnclosedAccrualsExcludingVat");
public sealed record OpiuDetailDto(Guid ExpenseRequestId, int LineNumber, DateOnly AccrualPeriod,
    string Organization, string? Counterparty, string? Purpose, decimal RemainingNetAmountKzt);
public sealed record OpiuDetailsDto(IReadOnlyList<OpiuDetailDto> Items, int Skip, int Take, int Total,
    decimal TotalNetAmountKzt, DateTimeOffset CapturedAtUtc);

/// <summary>
/// One bounded, coherent active-accrual capture, including its existing full
/// payment proof and canonical role/CFO scope. No cash-flow settings, current FX
/// rates, or browser page totals can rewrite the historical line snapshot.
/// </summary>
public sealed class OpiuReportService(IActiveAccrualReportReader reader, ICurrentActor actor,
    IAccrualAgingPolicy agingPolicy)
{
    public const int MaximumLines = 20_000;
    public const int MaximumArticles = 3_000;

    public async Task<OpiuReportDto> GetAsync(OpiuQuery query, CancellationToken cancellationToken)
    {
        var (rows, aging) = await CaptureAsync(query, cancellationToken);
        return Project(rows, query, aging.CapturedAtUtc);
    }

    public async Task<OpiuDetailsDto> DetailsAsync(OpiuQuery query, CancellationToken cancellationToken)
    {
        if (query.CostCenterId is null || query.ExpenseItemId is null)
            throw new InvalidActiveAccrualQueryException();
        var (rows, aging) = await CaptureAsync(query, cancellationToken);
        var selected = rows.Where(r => r.ExpenseItemIdentity == query.ExpenseItemId
            && r.CostCenterIdentity == query.CostCenterId).ToArray();
        var total = selected.Sum(RemainingNet);
        var items = selected.OrderBy(r => r.AccrualPeriod).ThenBy(r => r.ExpenseRequestId)
            .ThenBy(r => r.LineSortOrder).Skip(query.Skip).Take(query.Take)
            .Select(r => new OpiuDetailDto(r.ExpenseRequestId, r.LineSortOrder, r.AccrualPeriod,
                r.OrganizationName!, r.CounterpartyName, r.Purpose, RemainingNet(r))).ToArray();
        return new(items, query.Skip, query.Take, selected.Length, total, aging.CapturedAtUtc);
    }

    private async Task<(IReadOnlyList<ActiveAccrualRow>, AccrualAgingContext)> CaptureAsync(
        OpiuQuery query, CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        query.Validate();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var aging = agingPolicy.GetCurrentContext() ?? throw new ActiveAccrualReportUnavailableException();
            var filter = new ActiveAccrualFilter(query.OrganizationId, query.CostCenterId, query.From, query.Through);
            var read = await reader.ReadAsync(externalId, actor.HasFullBusinessDataAccess, filter,
                0, MaximumLines + 1, MaximumLines, aging, cancellationToken);
            if (read.Total > MaximumLines) throw new ActiveAccrualExportTooLargeException(read.Total, MaximumLines);
            // Reuse existing row, whole-scope total, date and ledger invariants.
            var validated = ActiveAccrualReportService.Project(read, 0, MaximumLines + 1, aging);
            if (validated.Total != read.Rows.Count) throw new ActiveAccrualReportUnavailableException();
            foreach (var row in read.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.AccrualPeriod < query.From || row.AccrualPeriod > query.Through)
                    throw new ActiveAccrualReportUnavailableException();
                _ = RemainingNet(row);
            }
            return (read.Rows, aging);
        }
        catch (OperationCanceledException) { throw; }
        catch (ActorNotAuthenticatedException) { throw; }
        catch (ActorForbiddenException) { throw; }
        catch (ActiveAccrualExportTooLargeException) { throw; }
        catch (ActiveAccrualReportUnavailableException) { throw; }
        catch (Exception error) { throw new ActiveAccrualReportUnavailableException(error); }
    }

    internal static decimal RemainingNet(ActiveAccrualRow row)
    {
        if (row.CostCenterIdentity is null || row.CostCenterIdentity == Guid.Empty)
            throw new ActiveAccrualReportUnavailableException();
        if (row.ExpenseItemIdentity is null || row.ExpenseItemIdentity == Guid.Empty
            || row.OriginalNetAmountKzt is not { } net || row.OriginalVatAmountKzt is not { } vat
            || net < 0 || vat < 0 || !MoneyAmountPolicy.IsRepresentable(net)
            || !MoneyAmountPolicy.IsRepresentable(vat) || net + vat != row.OriginalAmountKzt
            || row.ConfirmedClosedAmountKzt < 0 || row.RemainingAmountKzt < 0
            || row.ConfirmedClosedAmountKzt + row.RemainingAmountKzt != row.OriginalAmountKzt)
            throw new ActiveAccrualReportUnavailableException();
        // Closure is stored gross per immutable line. Allocate its net component
        // proportionally in minor units; full exhaustion transfers the exact net.
        // The resulting value is a report projection, never a new posting.
        return row.OriginalAmountKzt == 0m ? 0m : net - PairedMoneyAllocationPolicy.Allocate(
            net, row.ConfirmedClosedAmountKzt, row.OriginalAmountKzt);
    }

    internal static OpiuReportDto Project(IReadOnlyList<ActiveAccrualRow> rows, OpiuQuery query,
        DateTimeOffset capturedAtUtc)
    {
        query.Validate();
        if (rows.Count > MaximumLines) throw new ActiveAccrualExportTooLargeException(rows.Count, MaximumLines);
        var monthCount = (query.Through.Year - query.From.Year) * 12 + query.Through.Month - query.From.Month + 1;
        var months = Enumerable.Range(0, monthCount).Select(query.From.AddMonths).ToArray();
        var departments = new List<OpiuDepartmentDto>();
        var articleCount = 0;
        foreach (var department in rows.GroupBy(r => r.CostCenterIdentity))
        {
            var name = UniqueName(department.Select(r => r.CostCenterName));
            var articles = new List<OpiuArticleDto>();
            foreach (var article in department.GroupBy(r => r.ExpenseItemIdentity))
            {
                if (++articleCount > MaximumArticles) throw new ActiveAccrualExportTooLargeException(articleCount, MaximumArticles);
                var articleName = UniqueName(article.Select(r => r.ExpenseItemName));
                var amounts = new decimal[monthCount];
                foreach (var row in article)
                {
                    var value = RemainingNet(row);
                    var index = (row.AccrualPeriod.Year - query.From.Year) * 12 + row.AccrualPeriod.Month - query.From.Month;
                    if (index < 0 || index >= monthCount) throw new ActiveAccrualReportUnavailableException();
                    amounts[index] = checked(amounts[index] + value);
                }
                articles.Add(new(article.Key!.Value, articleName, amounts, amounts.Sum()));
            }
            var totals = Enumerable.Range(0, monthCount).Select(i => articles.Sum(a => a.Months[i])).ToArray();
            departments.Add(new(department.Key!.Value, name, articles.OrderBy(a => a.Name, StringComparer.Ordinal).ToArray(), totals, totals.Sum()));
        }
        var totalMonths = Enumerable.Range(0, monthCount).Select(i => departments.Sum(d => d.Months[i])).ToArray();
        return new(capturedAtUtc, months, departments.OrderBy(d => d.Name, StringComparer.Ordinal).ToArray(),
            totalMonths, totalMonths.Sum(), rows.Count);
    }

    private static string UniqueName(IEnumerable<string?> names)
    {
        var values = names.Distinct(StringComparer.Ordinal).Take(2).ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]) || values[0]!.Length > 500
            || values[0]!.Any(char.IsControl)) throw new ActiveAccrualReportUnavailableException();
        return values[0]!;
    }
}
