using Bms.Domain.ExpenseRequests;
namespace Bms.Application.Abstractions;
public sealed record ExpenseOperationEmployeeOption(Guid Id, string Name);
public sealed record ExpenseOperationTaxAuthorityOption(Guid Id, string Name, string? Bin);
public sealed record ExpensePaymentOperationOptions(IReadOnlyList<ExpenseOperationEmployeeOption> Employees,
    IReadOnlyList<ExpenseOperationTaxAuthorityOption> TaxAuthorities, bool ManualEmployeeAllowed = true);
public sealed record PayrollAdvanceDimensions372(Guid CostCenterId, Guid CashFlowItemId);
public interface IExpensePaymentOperationCatalog
{
    Task<ExpensePaymentOperationOptions> ReadAsync(CancellationToken token);
    Task<bool> MatchesAsync(ExpensePaymentOperation operation, CancellationToken token);
    Task<PayrollAdvanceDimensions372> ReadAdvanceDimensionsAsync(CancellationToken token) =>
        throw new InvalidOperationException("Advance budget configuration is unavailable.");
}
