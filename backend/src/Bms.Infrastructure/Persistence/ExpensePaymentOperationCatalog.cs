using Bms.Application.Abstractions;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
namespace Bms.Infrastructure.Persistence;
public sealed class ExpensePaymentOperationCatalog(BmsDbContext db) : IExpensePaymentOperationCatalog
{
    public async Task<PayrollAdvanceDimensions372> ReadAdvanceDimensionsAsync(CancellationToken token)
    {
        var centers = await db.CostCenters.AsNoTracking().Where(c => c.Code == "HR" && c.IsActive && !c.IsGroup)
            .Select(c => c.Id).Take(2).ToArrayAsync(token);
        if (centers.Length != 1) throw new InvalidOperationException("A unique active HR budget CFO is required.");
        var articles = await db.CashFlowItems.AsNoTracking().Where(c => c.IsActive && !c.IsGroup
                && c.CostCenterId == centers[0] && c.Name.StartsWith("332001 "))
            .Select(c => c.Id).Take(2).ToArrayAsync(token);
        if (articles.Length != 1) throw new InvalidOperationException("A unique HR salary article 332001 is required.");
        return new(centers[0], articles[0]);
    }
    public async Task<ExpensePaymentOperationOptions> ReadAsync(CancellationToken token) => new(
        await db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.Name).ThenBy(e => e.Id)
            .Select(e => new ExpenseOperationEmployeeOption(e.Id, e.Name)).ToArrayAsync(token),
        await db.Counterparties.AsNoTracking().Where(c => c.IsActive &&
            (c.Name.Contains("УГД") || c.Name.Contains("государственных доходов"))).OrderBy(c => c.Name)
            .Select(c => new ExpenseOperationTaxAuthorityOption(c.Id, c.Name, c.Bin)).ToArrayAsync(token));
    public Task<bool> MatchesAsync(ExpensePaymentOperation operation, CancellationToken token) =>
        operation.EmployeeId is not Guid id ? Task.FromResult(true) : db.Employees.AsNoTracking()
            .AnyAsync(e => e.Id == id && e.IsActive && e.Name == operation.EmployeeName, token);
}
