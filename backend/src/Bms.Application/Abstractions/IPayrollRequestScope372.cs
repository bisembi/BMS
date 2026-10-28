namespace Bms.Application.Abstractions;

public interface IPayrollRequestScope372
{
    Task<IReadOnlyList<Guid>> ReadAsync(string actorExternalId, CancellationToken token);
}
