namespace Bms.Application.Abstractions;

/// <summary>
/// Reads the server-authoritative substitution rows that may let one employee
/// act for the immutable initiator of an existing expense request.
/// </summary>
public interface IExpenseRequestSubstitutionReader
{
    Task<IReadOnlyList<ExpenseRequestSubstitutionProjectionRow>> ReadAsync(
        string principalActorExternalId,
        string deputyActorExternalId,
        CancellationToken cancellationToken);
}

public sealed record ExpenseRequestSubstitutionProjectionRow(
    Guid SubstitutionId,
    Guid PrincipalEmployeeId,
    Guid DeputyEmployeeId,
    string PrincipalActorExternalId,
    string DeputyActorExternalId,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidThroughUtc,
    string Reason,
    bool SubstitutionIsActive,
    bool PrincipalEmployeeIsActive,
    bool DeputyEmployeeIsActive,
    bool PrincipalMappingIsActive,
    bool DeputyMappingIsActive);
