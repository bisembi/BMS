using Bms.Application.Abstractions;
using Bms.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class ExpenseRequestInitiatorDirectory(
    BmsDbContext db, ICurrentActor actor, IBusinessTransactionRunner transactions) : IExpenseRequestInitiatorDirectory
{
    public Task<IReadOnlyList<ExpenseRequestInitiatorOption>> ListAsync(CancellationToken cancellationToken)
    {
        var actorId = ActorIdentity.RequireBusinessDataAccess(actor);
        return transactions.ExecuteAsync<IReadOnlyList<ExpenseRequestInitiatorOption>>(async token => {
            var scope = await BusinessDataReadScopeQuery.ResolveAsync(db, actorId, token);
            if (!scope.HasAny) throw new ActorForbiddenException();
            return await db.Employees.AsNoTracking().Where(employee => employee.IsActive)
                .OrderBy(employee => EF.Functions.Collate(employee.Name, "Cyrillic_General_100_CI_AS"))
                .ThenBy(employee => employee.Id)
                .Select(employee => new ExpenseRequestInitiatorOption(employee.Id, employee.Name)).ToArrayAsync(token);
        }, cancellationToken);
    }
}
