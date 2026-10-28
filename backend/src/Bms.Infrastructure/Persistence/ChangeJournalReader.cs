using Bms.Application.Abstractions;
using Bms.Application.Audit;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class ChangeJournalReader(BmsDbContext dbContext, IBusinessTransactionRunner transactions) : IChangeJournalReader
{
    private static readonly string[] References = ["ReferenceData", "Organization", "ExpenseItem", "CashFlowItem"];
    private static readonly string[] Employees = ["AuthorizationDirectoryEmployee", "BudgetOwnerAssignment", "FunctionalRoleReference"];
    private static readonly string[] Budgets = ["BudgetInstance", "BudgetCostCenterSettings", "BudgetPosition"];
    private static readonly string[] Accruals = ["ExpenseRequestAccrualSnapshot", "AccrualNotification", "AccrualSnapshot"];

    public Task<ChangeJournalReadPage> ReadAsync(
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? category,
        int skip, int take, CancellationToken cancellationToken)
        // The shared runner captures all reads in a short Serializable transaction
        // inside SQL Server's retry strategy; never begin a manual transaction
        // outside that strategy when EnableRetryOnFailure is configured.
        => transactions.ExecuteAsync(
            token => ReadPageAsync(fromUtc, toUtc, category, skip, take, token), cancellationToken);

    private async Task<ChangeJournalReadPage> ReadPageAsync(
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? category,
        int skip, int take, CancellationToken cancellationToken)
    {
        // Capture count, page and author names in one read-only transaction:
        // a concurrent Append must not invalidate the returned pagination.
        var query = dbContext.BusinessAuditEntries.AsNoTracking();
        if (fromUtc is not null) query = query.Where(a => a.OccurredAtUtc >= fromUtc.Value);
        if (toUtc is not null) query = query.Where(a => a.OccurredAtUtc < toUtc.Value);
        query = category switch
        {
            "references" => query.Where(a => References.Contains(a.ObjectType)),
            "employees" => query.Where(a => Employees.Contains(a.ObjectType)),
            "budgets" => query.Where(a => Budgets.Contains(a.ObjectType)),
            "expenses" => query.Where(a => a.ObjectType == "ExpenseRequest"),
            "accruals" => query.Where(a => Accruals.Contains(a.ObjectType)),
            "other" => query.Where(a => !References.Contains(a.ObjectType) && !Employees.Contains(a.ObjectType)
                && !Budgets.Contains(a.ObjectType) && a.ObjectType != "ExpenseRequest" && !Accruals.Contains(a.ObjectType)),
            _ => query
        };
        var total = await query.CountAsync(cancellationToken);
        var entries = await query.OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id)
            .Skip(skip).Take(take).Select(a => new
            {
                a.OccurredAtUtc, a.ActorExternalId, a.ObjectType, a.Action,
                AfterJson = a.AfterJson.Length <= 1_048_576 ? a.AfterJson : null
            }).ToArrayAsync(cancellationToken);
        var actors = entries.Select(a => a.ActorExternalId).Distinct().ToArray();
        // Resolve display names only. Ambiguous historical mappings do not
        // become a guessed author, and no SID is sent to the public DTO.
        var authors = await (from mapping in dbContext.EmployeeActorMappings.AsNoTracking()
                             where actors.Contains(mapping.ActorExternalId)
                             join employee in dbContext.Employees.AsNoTracking() on mapping.EmployeeId equals employee.Id
                             select new { mapping.ActorExternalId, employee.Name }).ToArrayAsync(cancellationToken);
        var names = authors.GroupBy(a => a.ActorExternalId).ToDictionary(g => g.Key,
            g => g.Select(a => a.Name).Distinct().Take(2).ToArray(), StringComparer.Ordinal);
        var result = new ChangeJournalReadPage(entries.Select(a => new ChangeJournalRecord(
            a.OccurredAtUtc,
            names.TryGetValue(a.ActorExternalId, out var possible) && possible.Length == 1 ? possible[0] : null,
            a.ObjectType, a.Action, a.AfterJson)).ToArray(), total);
        return result;
    }
}
