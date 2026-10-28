using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

public sealed partial class ExpenseRequestService
{
    private async Task<(ExpensePaymentOperation Operation, Guid Article)> PrepareAdvance372Async(
        ExpensePaymentOperation operation, CancellationToken token)
    {
        if (currentActor.ActiveRoleCode is not ("CFO_INITIATOR" or "BUDGET_OWNER")) throw new ActorForbiddenException();
        var dimensions = paymentOperationCatalog is null ? throw new InvalidOperationException("Advance catalogue unavailable.")
            : await paymentOperationCatalog.ReadAdvanceDimensionsAsync(token);
        var normalized = ExpensePaymentOperation.Normalize(operation)!;
        if (normalized.BudgetCostCenterId is { } supplied && supplied != dimensions.CostCenterId)
            throw new DomainRuleViolationException("The advance budget CFO cannot be overridden.");
        return (normalized with { BudgetCostCenterId = dimensions.CostCenterId }, dimensions.CashFlowItemId);
    }

    private async Task<CreateExpenseRequestCommand> PrepareAdvance372Async(CreateExpenseRequestCommand command, CancellationToken token)
    {
        if (command.PaymentOperation is not { IsPayrollAdvance: true } operation) return command;
        if (payrollAdvanceCreationPolicy372?.Enabled == false) throw new ActorForbiddenException();
        var resolved = await PrepareAdvance372Async(operation, token);
        if (command.Lines?.Count > 1) throw new DomainRuleViolationException("An advance has one salary budget line.");
        var line = command.Lines?.SingleOrDefault();
        if (line?.CashFlowItemId is { } article && article != resolved.Article)
            throw new DomainRuleViolationException("The advance salary article cannot be overridden.");
        if (line?.CostCenterId is { } center && center != command.DefaultCostCenterId)
            throw new DomainRuleViolationException("The advance retains the author's CFO.");
        return command with { PaymentOperation = resolved.Operation,
            BusinessJustification = string.IsNullOrWhiteSpace(command.BusinessJustification) ? "Выплата аванса" : command.BusinessJustification,
            Lines = [new(resolved.Article, command.DefaultCostCenterId, ParseAdvancePeriod372(resolved.Operation.Period),
                resolved.Operation.AdvanceAmount ?? 0m, ExpenseVatRate.WithoutVat)] };
    }

    private async Task<UpdateExpenseRequestCommand> PrepareAdvance372Async(UpdateExpenseRequestCommand command, CancellationToken token)
    {
        if (command.PaymentOperation is not { IsPayrollAdvance: true } operation) return command;
        var resolved = await PrepareAdvance372Async(operation, token);
        if (command.Lines?.Count > 1) throw new DomainRuleViolationException("An advance has one salary budget line.");
        var line = command.Lines?.SingleOrDefault();
        if (line?.CashFlowItemId is { } article && article != resolved.Article)
            throw new DomainRuleViolationException("The advance salary article cannot be overridden.");
        if (line?.CostCenterId is { } center && center != command.DefaultCostCenterId)
            throw new DomainRuleViolationException("The advance retains the author's CFO.");
        return command with { PaymentOperation = resolved.Operation,
            BusinessJustification = string.IsNullOrWhiteSpace(command.BusinessJustification) ? "Выплата аванса" : command.BusinessJustification,
            Lines = [new(line?.Id, resolved.Article, command.DefaultCostCenterId, ParseAdvancePeriod372(resolved.Operation.Period),
                resolved.Operation.AdvanceAmount ?? 0m, ExpenseVatRate.WithoutVat)] };
    }
    private static DateOnly? ParseAdvancePeriod372(string? period) => period is null ? null
        : DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
