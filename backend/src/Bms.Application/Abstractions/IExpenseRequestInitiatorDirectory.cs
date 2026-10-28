namespace Bms.Application.Abstractions;

// Filter choices only: no account names, SIDs, roles or personnel details.
public sealed record ExpenseRequestInitiatorOption(Guid Id, string Name);

public interface IExpenseRequestInitiatorDirectory
{
    Task<IReadOnlyList<ExpenseRequestInitiatorOption>> ListAsync(CancellationToken cancellationToken);
}
