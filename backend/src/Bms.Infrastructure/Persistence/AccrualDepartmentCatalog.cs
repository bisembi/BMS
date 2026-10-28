using Bms.Application.Abstractions;
using Bms.Application.ReferenceData;
using Microsoft.EntityFrameworkCore;
namespace Bms.Infrastructure.Persistence;
public sealed class AccrualDepartmentCatalog(BmsDbContext db):IAccrualDepartmentCatalog
{
    // Classification labels only; never expose budget amounts or change scope.
    public async Task<IReadOnlyList<ReferenceDataItemDto>> ListOnlyDepartmentsAsync(CancellationToken token) =>
        await db.CostCenters.AsNoTracking().Where(c => c.IsActive && !c.IsGroup
            && db.CostCenterPurposes457.Any(p => p.CostCenterId == c.Id && p.OnlyCostDepartment))
            .OrderBy(c => c.Code).ThenBy(c => c.Id)
            .Select(c => new ReferenceDataItemDto(c.Id, c.Code, c.Name)).ToArrayAsync(token);
    public async Task<IReadOnlyList<ReferenceDataItemDto>> ListAsync(CancellationToken token) =>
        await db.CostCenters.AsNoTracking().Where(c=>c.IsActive&&!c.IsGroup).OrderBy(c=>c.Name)
            .Select(c=>new ReferenceDataItemDto(c.Id,c.Code,c.Name)).ToArrayAsync(token);
    public async Task<bool> AreActiveAsync(IReadOnlyCollection<Guid> ids,CancellationToken token)
    {
        var unique=ids.Distinct().ToArray();
        return !unique.Contains(Guid.Empty) && await db.CostCenters.CountAsync(c=>unique.Contains(c.Id)&&c.IsActive&&!c.IsGroup,token)==unique.Length;
    }
}
