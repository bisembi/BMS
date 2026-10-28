using Bms.Application.ReferenceData;

namespace Bms.Application.Abstractions;

public interface IOrganizationDirectoryRepository
{
    Task<ReferenceDataItemDto> CreateAsync(
        string code,
        string name,
        CancellationToken cancellationToken);

    Task<OrganizationDirectoryRecord?> FindAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<ReferenceDataItemDto> UpdateAsync(
        Guid id,
        string code,
        string name,
        CancellationToken cancellationToken);
}
