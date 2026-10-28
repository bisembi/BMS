using Bms.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class ExpenseRequestSubstitutionReader(BmsDbContext dbContext)
    : IExpenseRequestSubstitutionReader
{
    public async Task<IReadOnlyList<ExpenseRequestSubstitutionProjectionRow>> ReadAsync(
        string principalActorExternalId,
        string deputyActorExternalId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalActorExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deputyActorExternalId);

        return await (
            from substitution in dbContext.ExpenseRequestSubstitutions.AsNoTracking()
            join principalMapping in dbContext.EmployeeActorMappings.AsNoTracking()
                on substitution.PrincipalEmployeeId equals principalMapping.EmployeeId
            join deputyMapping in dbContext.EmployeeActorMappings.AsNoTracking()
                on substitution.DeputyEmployeeId equals deputyMapping.EmployeeId
            join principalEmployee in dbContext.Employees.AsNoTracking()
                on substitution.PrincipalEmployeeId equals principalEmployee.Id
            join deputyEmployee in dbContext.Employees.AsNoTracking()
                on substitution.DeputyEmployeeId equals deputyEmployee.Id
            where principalMapping.ActorExternalId == principalActorExternalId
                && deputyMapping.ActorExternalId == deputyActorExternalId
            orderby substitution.ValidFromUtc, substitution.ValidThroughUtc, substitution.Id
            select new ExpenseRequestSubstitutionProjectionRow(
                substitution.Id,
                substitution.PrincipalEmployeeId,
                substitution.DeputyEmployeeId,
                principalMapping.ActorExternalId,
                deputyMapping.ActorExternalId,
                substitution.ValidFromUtc,
                substitution.ValidThroughUtc,
                substitution.Reason,
                substitution.IsActive,
                principalEmployee.IsActive,
                deputyEmployee.IsActive,
                principalMapping.IsActive,
                deputyMapping.IsActive))
            .ToArrayAsync(cancellationToken);
    }
}
