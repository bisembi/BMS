using Bms.Application.Budgeting;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;
public sealed partial class BudgetDirectoryStore
{
    public async Task SetPurpose457Async(Guid id, SetCostCenterPurpose457Command command, CancellationToken token)
    {
        EnsureLocked(id);
        var center = await db.CostCenters.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, token)
            ?? throw new BudgetDirectoryNotFoundException();
        if (center.IsGroup) throw new DomainRuleViolationException("A group cannot be a cost department.");
        var settings = await db.CostCenterPurposes457.SingleOrDefaultAsync(s => s.CostCenterId == id, token);
        if (settings?.ConcurrencyStamp != command.ExpectedConcurrencyStamp)
            throw new BudgetInstanceConcurrencyConflictException();
        if (settings is null)
        {
            settings = new CostCenterPurpose457 { CostCenterId = id };
            db.CostCenterPurposes457.Add(settings);
        }
        settings.OnlyCostDepartment = command.OnlyCostDepartment;
        settings.ConcurrencyStamp = Guid.NewGuid();
        // No prefix allocation, membership, activity, source identity or financial writes.
    }
}
