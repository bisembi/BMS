using Bms.Application.Budgeting;

namespace Bms.Application.Abstractions;

/// <summary>SQL143 snapshots only. Neither this store nor the copy route contacts the financial portal.</summary>
public interface ICashFlowActualsStore
{
    Task<CashFlowActualsImportResultDto?> FindImportAsync(string sourceHash, CancellationToken cancellationToken);
    Task<CashFlowActualsImportResultDto> ImportAsync(ImportCashFlowActualsCommand command,
        Guid employeeId, string externalId, DateTimeOffset importedAtUtc, CancellationToken cancellationToken);
    Task<BudgetActualsPreviewDto> PreviewAsync(PreviewBudgetActualsCommand command, CancellationToken cancellationToken);
}
