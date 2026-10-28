using Bms.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class ExpenseRequestContractValidator(BmsDbContext dbContext)
    : IExpenseRequestContractValidator
{
    public Task<bool> MatchesAsync(Guid? contractId, Guid? organizationId,
        Guid? counterpartyId, string currencyCode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (contractId is null) return Task.FromResult(true);
        // One bounded server-side query, inside the caller's business transaction.
        // Inactive historical contracts remain readable; activation is a separate
        // existing CONFIRM_NSI rule, not a new restriction on draft editing.
        return (from contract in dbContext.Contracts.AsNoTracking()
                join currency in dbContext.Currencies.AsNoTracking()
                    on contract.CurrencyId equals currency.Id
                where contract.Id == contractId
                    && (organizationId == null || contract.OrganizationId == organizationId)
                    && (counterpartyId == null || contract.CounterpartyId == counterpartyId)
                    && currency.Code == currencyCode
                select contract.Id).AnyAsync(cancellationToken);
    }
}
