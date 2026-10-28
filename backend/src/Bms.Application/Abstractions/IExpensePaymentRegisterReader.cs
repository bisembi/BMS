namespace Bms.Application.Abstractions;

public sealed record ExpensePaymentRegisterRow(Guid ExpenseRequestId, string OrganizationName,
    DateOnly? PlannedPaymentDate, string CostCenterName, string InitiatorName, string CounterpartyName,
    string CurrencyCode, string Amount, string? EquivalentKzt);
public sealed record ExpensePaymentRegisterCurrencyTotal(string CurrencyCode, string Amount);
public sealed record ExpensePaymentRegisterDto(DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ExpensePaymentRegisterRow> Items, IReadOnlyList<ExpensePaymentRegisterCurrencyTotal> CurrencyTotals,
    string? TotalEquivalentKzt, bool HasUnknownEquivalents);

public interface IExpensePaymentRegisterReader
{
    Task<ExpensePaymentRegisterDto> ReadAsync(CancellationToken token);
}
