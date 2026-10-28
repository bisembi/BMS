namespace Bms.Domain.ExpenseRequests;

public sealed record ExpenseRequestLineDraft(
    Guid? CashFlowItemId,
    Guid? CostCenterId,
    DateOnly? AccrualPeriod,
    decimal Amount,
    Guid? Id = null,
    ExpenseVatRate? VatRateCode = null,
    Guid? AccrualCostCenterId = null);
