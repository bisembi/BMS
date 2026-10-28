using Bms.Application.ExpenseRequests;

namespace Bms.Application.Abstractions;

public sealed record ExpenseRequestExportRow(
    DateTimeOffset CreatedAtUtc,
    DateOnly? PlannedPaymentDate,
    string? Purpose,
    string? OrganizationName,
    string? CostCenterName,
    string? CounterpartyName,
    string InitiatorExternalId,
    string Status,
    bool IsUrgent,
    decimal TotalAmount,
    string CurrencyCode,
    decimal? TotalAmountKzt,
    bool? IsOverBudget)
{
    // Presentation only. The stored actor remains the authorization/filter identity.
    public string? InitiatorDisplayName { get; init; }
}

public interface IExpenseRequestExportRepository
{
    Task<IReadOnlyList<ExpenseRequestExportRow>> ListVisibleAsync(
        string actorExternalId,
        bool includeAll,
        ExpenseRequestFilter filter,
        CancellationToken cancellationToken);
}
