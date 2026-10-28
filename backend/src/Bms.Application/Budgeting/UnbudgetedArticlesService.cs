using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Workflow;

namespace Bms.Application.Budgeting;

public sealed class UnbudgetedArticlesService(IUnbudgetedArticlesStore store,
    EmployeeActorResolver employees, EmployeeFunctionalRoleResolver roles,
    ICurrentActor actor, IBusinessTransactionRunner transactions)
{
    public Task<UnbudgetedArticlesReportDto> ReadAsync(UnbudgetedArticlesOptions options,
        CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        options.Months();
        return transactions.ExecuteAsync(async token =>
        {
            var employee = await employees.ResolveAsync(externalId, token);
            var id = employee.EmployeeId ?? throw new ActorForbiddenException();
            var current = await roles.ResolveAsync(id, token);
            if (current.EmployeeId != id || !current.EmployeeExists || !current.EmployeeIsActive)
                throw new BudgetOwnerCreationAuthorityUnavailableException();
            var global = BusinessDataReadPolicy.HasGlobalRole(current);
            if (!global && !current.HasExactRole(ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode)
                && !current.HasExactRole(ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode))
                throw new ActorForbiddenException();
            return await store.ReadAsync(options, id, global, token);
        }, cancellationToken);
    }
}
