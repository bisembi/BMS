using System.Text.Json;
using Bms.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

internal static class BudgetAdjustmentProjection
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static async Task<BudgetInstance[]> ApplyAsync(BmsDbContext db, BudgetInstance[] originals, CancellationToken token)
    {
        var ids=originals.Where(b=>b.Status==BudgetInstanceStatus.Approved).Select(b=>b.Id).ToArray();
        if(ids.Length==0)return originals;
        var accepted=await db.BudgetAdjustments.AsNoTracking().Where(a=>ids.Contains(a.BaseBudgetId)&&a.State=="Approved")
            .OrderBy(a=>a.ApprovalOrder).ToArrayAsync(token);
        return originals.Select(b=>{
            var docs=accepted.Where(a=>a.BaseBudgetId==b.Id).ToArray();if(docs.Length==0)return b;
            var cells=BudgetAdjustmentPlan.Cells(b);
            foreach(var a in docs)cells=BudgetAdjustmentPlan.Apply(b.ValidFrom,b.ValidTo,cells,
                JsonSerializer.Deserialize<BudgetAdjustmentTransfer[]>(a.TransfersJson,Json)??throw new InvalidOperationException("Stored adjustment missing."));
            return b.ProjectApprovedCells(cells);
        }).ToArray();
    }
}
