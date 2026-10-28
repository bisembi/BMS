using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.OverpaymentDeficits;
using Bms.Domain.Overpayments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed class ManualDeficitReader(BmsDbContext dbContext) : IManualDeficitReader
{
    public async Task<ManualDeficitReadPage> ListAsync(
        ManualDeficitReadAccess access, ManualDeficitQuery query, CancellationToken cancellationToken)
    {
        EnsureAttempt();
        var visible = VisibleDeficits(access);
        if (query.OrganizationId is { } organizationId)
            visible = visible.Where(row => row.OrganizationId == organizationId);
        if (!query.IncludeSettled)
            visible = visible.Where(row => row.CurrentAmount > 0m
                || dbContext.ManualOverpaymentDeficitCorrections.Any(correction =>
                    correction.DeficitId == row.DeficitId && correction.State == "PendingApproval"));
        if (query.OnlyPending)
            visible = visible.Where(row => dbContext.ManualOverpaymentDeficitCorrections.Any(correction =>
                correction.DeficitId == row.DeficitId && correction.State == "PendingApproval"));
        var total = await visible.CountAsync(cancellationToken);
        var page = visible.OrderByDescending(row => row.OccurredAt).ThenBy(row => row.DeficitId)
            .Skip(query.Skip).Take(query.Take);
        return new(await Project(page, access).ToArrayAsync(cancellationToken), total);
    }

    public async Task<ManualDeficitCorrectionReadPage?> ReadAsync(
        Guid deficitId, ManualDeficitReadAccess access, int skip, int take, CancellationToken cancellationToken)
    {
        EnsureAttempt();
        var deficit = await Project(VisibleDeficits(access).Where(row => row.DeficitId == deficitId), access)
            .SingleOrDefaultAsync(cancellationToken);
        if (deficit is null) return null;
        var corrections = dbContext.ManualOverpaymentDeficitCorrections.AsNoTracking()
            .Where(row => row.DeficitId == deficitId);
        var rows = await (
            from correction in corrections
                .OrderBy(row => row.State == "PendingApproval" ? 0 : 1)
                .ThenByDescending(row => row.DecidedAt).ThenBy(row => row.CorrectionId)
                .Skip(skip).Take(take)
            join currency in dbContext.Currencies.AsNoTracking() on correction.CurrencyId equals currency.Id
            select new { Correction = correction, CurrencyCode = currency.Code })
            .ToArrayAsync(cancellationToken);
        return new(deficit, rows.Select(row => ManualDeficitCorrectionStore.ToSnapshot(
            new StoredManualOverpaymentDeficitCorrection(row.Correction.CorrectionId,
                row.Correction.DeficitId, row.Correction.OrganizationId, row.Correction.CounterpartyId,
                row.Correction.CurrencyId, row.CurrencyCode, row.Correction.DeficitAmountAtCreation,
                row.Correction.DeficitAmountKztAtCreation, row.Correction.CorrectionAmount,
                row.Correction.CorrectionAmountKzt, row.Correction.Basis, row.Correction.ResponsibleAccountantId,
                row.Correction.FinancialDirectorId, row.Correction.State, row.Correction.DecidedAt,
                row.Correction.DecisionComment, row.Correction.RejectionReason,
                row.Correction.CreationCommandId, row.Correction.DecisionCommandId))).ToArray(),
            deficit.CorrectionCount);
    }

    private IQueryable<OverpaymentDeficit> VisibleDeficits(ManualDeficitReadAccess access)
    {
        var rows = dbContext.OverpaymentDeficits.AsNoTracking();
        if (access.ReadAllOrganizations) return rows;
        var organizations = ExpenseRequestVisibilityQuery.ActiveActorOrganizationIds(dbContext, access.ActorExternalId);
        return rows.Where(row => organizations.Contains(row.OrganizationId));
    }

    private IQueryable<ManualDeficitReadRow> Project(
        IQueryable<OverpaymentDeficit> rows, ManualDeficitReadAccess access)
    {
        var organizations = ExpenseRequestVisibilityQuery.ActiveActorOrganizationIds(dbContext, access.ActorExternalId);
        return from deficit in rows
               join organization in dbContext.Organizations.AsNoTracking() on deficit.OrganizationId equals organization.Id
               join counterparty in dbContext.Counterparties.AsNoTracking() on deficit.CounterpartyId equals counterparty.Id
               join currency in dbContext.Currencies.AsNoTracking() on deficit.CurrencyId equals currency.Id
               select new ManualDeficitReadRow(
                   new OverpaymentDeficitSnapshot(deficit.DeficitId, deficit.OrganizationId,
                       deficit.CounterpartyId, currency.Code, deficit.OccurredAt, deficit.OriginalAmount,
                       deficit.OriginalAmountKzt, deficit.CurrentAmount, deficit.CurrentAmountKzt),
                   organization.Name, counterparty.Name,
                   dbContext.ManualOverpaymentDeficitCorrections.Count(correction => correction.DeficitId == deficit.DeficitId),
                   dbContext.ManualOverpaymentDeficitCorrections.Any(correction => correction.DeficitId == deficit.DeficitId
                       && correction.State == "PendingApproval"),
                   organizations.Contains(deficit.OrganizationId));
    }

    private void EnsureAttempt()
    {
        if (dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || dbContext.ChangeTracker.HasChanges())
            throw new ManualDeficitCorrectionUnavailableException();
    }
}
