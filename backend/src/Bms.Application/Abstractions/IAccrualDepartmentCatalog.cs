using Bms.Application.ReferenceData;
namespace Bms.Application.Abstractions;

// Classification only. This catalogue never grants access to a budget or document.
public interface IAccrualDepartmentCatalog
{
    Task<IReadOnlyList<ReferenceDataItemDto>> ListAsync(CancellationToken token);
    Task<IReadOnlyList<ReferenceDataItemDto>> ListOnlyDepartmentsAsync(CancellationToken token);
    Task<bool> AreActiveAsync(IReadOnlyCollection<Guid> ids,CancellationToken token);
}
