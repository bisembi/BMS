using Bms.Application.Budgeting;
using Bms.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class BudgetApprovalStore(BmsDbContext db) : IBudgetApprovalStore
{
    public async Task CaptureActiveVersionAsync(Guid reviewId, CancellationToken token)
    {
        RequireTransaction();
        var active = await CompanyBudgetPlanReader.ActiveAsync(db, token);
        db.CompanyBudgetApprovalBaselines.Add(new() { ReviewId = reviewId, VersionId = active.VersionId, ActiveConcurrencyStamp = active.ConcurrencyStamp });
    }
    public async Task<bool> ActiveVersionMatchesAsync(Guid reviewId, CancellationToken token)
    {
        RequireTransaction();
        var active = await CompanyBudgetPlanReader.ActiveAsync(db, token);
        var frozen = db.CompanyBudgetApprovalBaselines.Local.SingleOrDefault(x => x.ReviewId == reviewId)
            ?? await db.CompanyBudgetApprovalBaselines.AsNoTracking().SingleOrDefaultAsync(x => x.ReviewId == reviewId, token);
        // Pre-SQL048 reviews have no invented provenance. They retain the old
        // comparison only while the initial seed is active; a later activation
        // requires Return/resubmit, just as an existing changed baseline does.
        return frozen is null
            ? !await db.CompanyBudgetActivations.AnyAsync(x => x.PreviousVersionId.HasValue, token)
            : frozen.VersionId == active.VersionId && frozen.ActiveConcurrencyStamp == active.ConcurrencyStamp;
    }
    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != System.Data.IsolationLevel.Serializable)
            throw new InvalidOperationException("Budget review requires Serializable transaction.");
    }
    public async Task<BudgetApprovalSubmission?> LatestAsync(Guid budgetId, CancellationToken token)
    {
        RequireTransaction();
        return await db.BudgetApprovalSubmissions.AsNoTracking().Where(r => r.BudgetInstanceId == budgetId)
            .OrderByDescending(r => r.SubmissionNumber).FirstOrDefaultAsync(token);
    }
    public async Task<BudgetApprovalSubmission?> FindAsync(Guid budgetId, Guid reviewId, CancellationToken token)
    {
        RequireTransaction();
        var local = db.ChangeTracker.Entries<BudgetApprovalSubmission>().SingleOrDefault(e => e.Entity.Id == reviewId);
        if (local is not null)
        {
            if (local.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("An unsaved review must not be refreshed.");
            local.State = EntityState.Detached;
        }
        return await db.BudgetApprovalSubmissions.SingleOrDefaultAsync(r => r.BudgetInstanceId == budgetId && r.Id == reviewId, token);
    }
    public void Add(BudgetApprovalSubmission review) { RequireTransaction(); db.BudgetApprovalSubmissions.Add(review); }
    public async Task<IReadOnlyList<BudgetApprovalBaselineVersion>> ReadBaselinesAsync(BudgetInstance proposal, CancellationToken token)
    {
        RequireTransaction();
        var approved = (await CompanyBudgetPlanReader.ReadAsync(db, null, new[] { proposal.CostCenterId }, token)).Instances
            .Where(b => b.Id != proposal.Id && b.ValidFrom <= proposal.ValidTo && b.ValidTo >= proposal.ValidFrom).ToArray();
        return approved.Select(b => new BudgetApprovalBaselineVersion(b.Id, b.CostCenterId, b.ValidFrom,
            b.ValidTo, b.ApprovalOrdinal!.Value, b.ApprovedAtUtc!.Value,
            b.Lines.Select(l => new BudgetApprovalBaselineLine(l.CashFlowItemId, l.BudgetMonth, l.LimitAmountKzt, l.FlowDirection)).ToArray())).ToArray();
    }
}
