namespace Bms.Application.Abstractions;

/// <summary>Only the selected NSI role and explicit active organization grants. Not financial read authority.</summary>
public interface INsiWorkQueueScopeReader
{
    Task<IReadOnlyList<Guid>> ReadOrganizationIdsAsync(string actorExternalId, CancellationToken cancellationToken);
}
