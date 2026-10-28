namespace Bms.Application.Abstractions;

/// Checks only an explicitly selected existing contract. Missing draft header
/// fields are allowed; supplied fields must agree with the current catalogue.
public interface IExpenseRequestContractValidator
{
    Task<bool> MatchesAsync(Guid? contractId, Guid? organizationId,
        Guid? counterpartyId, string currencyCode, CancellationToken cancellationToken);
}
