using Bms.Application.Budgeting;

namespace Bms.Application.Abstractions;

/// <summary>
/// All operations share the caller's Serializable business transaction and unit
/// of work; no unrelated pending writes may be supplied. A store locks at most
/// one cost center within a transaction. Rollback belongs to the caller.
/// </summary>
public interface IBudgetDirectoryStore
{
    Task<bool> LockCostCenterAsync(Guid costCenterId, CancellationToken cancellationToken);
    Task<BudgetDirectorySettingsDto?> ReadAsync(Guid costCenterId, CancellationToken cancellationToken);
    Task<BudgetDirectoryOptionPageDto> ListCostCentersAsync(string query, int skip, int take, CancellationToken cancellationToken);
    Task<BudgetDirectoryOptionPageDto> ListEmployeesAsync(string query, int skip, int take, CancellationToken cancellationToken);
    Task<BudgetDirectorySettingsDto> ReplaceAsync(Guid costCenterId, UpdateBudgetDirectoryCommand command, CancellationToken cancellationToken);
    Task SetPurpose457Async(Guid costCenterId, SetCostCenterPurpose457Command command, CancellationToken cancellationToken)
        => throw new NotSupportedException("Purpose store unavailable.");
    // Caller must hold the same CFO lock across authority, replay, allocation,
    // document/audit/idempotency save and commit. This method never commits.
    Task<string> AllocateNumberAsync(Guid costCenterId, DateOnly documentDate, CancellationToken cancellationToken);
}
