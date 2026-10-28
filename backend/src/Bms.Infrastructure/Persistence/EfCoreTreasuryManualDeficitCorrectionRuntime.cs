using System.Data;
using Bms.Domain.Overpayments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed class EfCoreTreasuryManualDeficitCorrectionRuntime :
    ITreasuryManualDeficitCorrectionRuntime
{
    private const int ManagedLockTimeoutMilliseconds = 10_000;
    private readonly BmsDbContext dbContext;

    internal EfCoreTreasuryManualDeficitCorrectionRuntime(BmsDbContext dbContext)
    {
        this.dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public bool HasSerializableTransaction
    {
        get
        {
            var transaction = dbContext.Database.CurrentTransaction;
            return transaction is not null
                && transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable;
        }
    }

    public bool HasPendingChanges => dbContext.ChangeTracker.HasChanges();

    public async Task AcquireExclusiveTransactionLockAsync(
        string resource,
        CancellationToken cancellationToken)
    {
        if (!HasSerializableTransaction
            || string.IsNullOrWhiteSpace(resource)
            || resource.Length > BusinessManagedLockResources.SqlServerMaximumResourceLength)
        {
            throw new InvalidOperationException(
                "A bounded lock resource and Serializable transaction are required.");
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
            DECLARE @BmsManualDeficitCorrectionLockResult int;
            EXEC @BmsManualDeficitCorrectionLockResult = sys.sp_getapplock
                @Resource = {{resource}},
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = {{ManagedLockTimeoutMilliseconds}},
                @DbPrincipal = N'public';
            IF @BmsManualDeficitCorrectionLockResult < 0
                THROW 60134, N'BMS manual deficit correction lock was not acquired.', 1;
            """, cancellationToken);
    }

    public Task<bool> EmployeeExistsAsync(
        Guid employeeId,
        CancellationToken cancellationToken) =>
        dbContext.Employees
            .AsNoTracking()
            .AnyAsync(employee => employee.Id == employeeId, cancellationToken);

    public async Task<bool> CommandExistsAsync(
        Guid commandId,
        CancellationToken cancellationToken)
    {
        if (await dbContext.OverpaymentDeficits
            .AsNoTracking()
            .AnyAsync(deficit => deficit.CreationCommandId == commandId, cancellationToken))
        {
            return true;
        }
        if (await dbContext.OverpaymentAllocations
            .AsNoTracking()
            .AnyAsync(allocation => allocation.CommandId == commandId, cancellationToken))
        {
            return true;
        }
        if (await dbContext.ManualOverpaymentDeficitCorrections
            .AsNoTracking()
            .AnyAsync(correction => correction.CreationCommandId == commandId
                    || correction.DecisionCommandId == commandId,
                cancellationToken))
        {
            return true;
        }
        return await dbContext.OverpaymentDeficitSettlements
            .AsNoTracking()
            .AnyAsync(settlement => settlement.CommandId == commandId, cancellationToken);
    }

    public Task<bool> SourceMatchesScopeAsync(
        Guid sourceId,
        Guid organizationId,
        Guid counterpartyId,
        Guid currencyId,
        Guid? sourceExpenseRequestId,
        DateTimeOffset deficitOccurredAtUtc,
        CancellationToken cancellationToken) =>
        dbContext.OverpaymentSources
            .AsNoTracking()
            .AnyAsync(source => source.SourceId == sourceId
                && source.OrganizationId == organizationId
                && source.CounterpartyId == counterpartyId
                && source.CurrencyId == currencyId
                && (sourceExpenseRequestId == null
                    || source.SourceExpenseRequestId == sourceExpenseRequestId)
                && source.OccurredAt <= deficitOccurredAtUtc,
                cancellationToken);

    public Task<StoredOverpaymentDeficit?> FindDeficitByIdAsync(
        Guid deficitId,
        CancellationToken cancellationToken) =>
        BuildDeficitQuery(deficitId)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<StoredManualOverpaymentDeficitCorrection?> FindCorrectionByIdAsync(
        Guid correctionId,
        CancellationToken cancellationToken) =>
        BuildCorrectionQuery(correctionId: correctionId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<StoredManualOverpaymentDeficitCorrection>>
        FindPendingCorrectionsByDeficitAsync(
            Guid deficitId,
            CancellationToken cancellationToken)
    {
        var rows = await BuildCorrectionQuery(pendingDeficitId: deficitId)
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }

    public Task<StoredManualDeficitSettlement?> FindSettlementByCorrectionAsync(
        Guid correctionId,
        CancellationToken cancellationToken) =>
        BuildManualSettlementQuery(correctionId: correctionId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<StoredManualDeficitSettlement>>
        FindSettlementsByCommandAsync(
            Guid commandId,
            CancellationToken cancellationToken)
    {
        var rows = await BuildManualSettlementQuery(commandId: commandId)
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows);
    }

    public async Task<int> InsertCorrectionAsync(
        StoredManualOverpaymentDeficitCorrection correction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(correction);
        EnsureCleanSerializableAttempt();
        var entity = new ManualOverpaymentDeficitCorrection
        {
            CorrectionId = correction.CorrectionId,
            DeficitId = correction.DeficitId,
            OrganizationId = correction.OrganizationId,
            CounterpartyId = correction.CounterpartyId,
            CurrencyId = correction.CurrencyId,
            DeficitAmountAtCreation = correction.DeficitAmountAtCreation,
            DeficitAmountKztAtCreation = correction.DeficitAmountKztAtCreation,
            CorrectionAmount = correction.CorrectionAmount,
            CorrectionAmountKzt = correction.CorrectionAmountKzt,
            Basis = correction.Basis,
            ResponsibleAccountantId = correction.ResponsibleAccountantId,
            FinancialDirectorId = correction.FinancialDirectorId,
            State = correction.State,
            DecidedAt = correction.DecidedAtUtc,
            DecisionComment = correction.DecisionComment,
            RejectionReason = correction.RejectionReason,
            CreationCommandId = correction.CreationCommandId,
            DecisionCommandId = correction.DecisionCommandId
        };
        dbContext.ManualOverpaymentDeficitCorrections.Add(entity);
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.Entry(entity).State = EntityState.Detached;
        }
    }

    public async Task<int> UpdateDeficitAsync(
        StoredOverpaymentDeficit expectedDeficit,
        decimal amountAfter,
        decimal amountKztAfter,
        Guid actualCorrectionExecutorId,
        DateTimeOffset? settledAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedDeficit);
        EnsureCleanSerializableAttempt();

        return await dbContext.OverpaymentDeficits
            .Where(deficit => deficit.OrganizationId == expectedDeficit.OrganizationId
                && deficit.CounterpartyId == expectedDeficit.CounterpartyId
                && deficit.CurrencyId == expectedDeficit.CurrencyId
                && deficit.DeficitId == expectedDeficit.DeficitId
                && deficit.OriginalAmount == expectedDeficit.OriginalAmount
                && deficit.OriginalAmountKzt == expectedDeficit.OriginalAmountKzt
                && deficit.CurrentAmount == expectedDeficit.CurrentAmount
                && deficit.CurrentAmountKzt == expectedDeficit.CurrentAmountKzt
                && deficit.SourceOverpaymentId == expectedDeficit.SourceOverpaymentId
                && deficit.ExternalReversalId == expectedDeficit.ExternalReversalId
                && deficit.OccurredAt == expectedDeficit.OccurredAtUtc
                && deficit.SourceExpenseRequestId == expectedDeficit.SourceExpenseRequestId
                && deficit.CreationCommandId == expectedDeficit.CreationCommandId
                && deficit.ActualCorrectionExecutorId ==
                    expectedDeficit.ActualCorrectionExecutorId
                && deficit.SettledAt == expectedDeficit.SettledAtUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(deficit => deficit.CurrentAmount, amountAfter)
                    .SetProperty(deficit => deficit.CurrentAmountKzt, amountKztAfter)
                    .SetProperty(
                        deficit => deficit.ActualCorrectionExecutorId,
                        actualCorrectionExecutorId)
                    .SetProperty(deficit => deficit.SettledAt, settledAtUtc),
                cancellationToken);
    }

    public async Task<int> InsertSettlementAsync(
        StoredManualDeficitSettlement settlement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        EnsureCleanSerializableAttempt();
        var entity = new OverpaymentDeficitSettlement
        {
            DeficitId = settlement.DeficitId,
            CommandId = settlement.CommandId,
            Kind = settlement.Kind,
            SourceReference = null,
            SourceExpenseRequestId = null,
            CorrectionId = settlement.CorrectionId,
            Amount = settlement.Amount,
            AmountKzt = settlement.AmountKzt,
            AppliedAt = settlement.AppliedAtUtc,
            UserId = settlement.UserId
        };
        dbContext.OverpaymentDeficitSettlements.Add(entity);
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.Entry(entity).State = EntityState.Detached;
        }
    }

    public Task<int> UpdateCorrectionDecisionAsync(
        StoredManualOverpaymentDeficitCorrection expectedCorrection,
        ManualOverpaymentDeficitCorrectionDecision decision,
        Guid decisionCommandId,
        DateTimeOffset decidedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedCorrection);
        ArgumentNullException.ThrowIfNull(decision);
        EnsureCleanSerializableAttempt();

        return dbContext.ManualOverpaymentDeficitCorrections
            .Where(correction => correction.CorrectionId == expectedCorrection.CorrectionId
                && correction.DeficitId == expectedCorrection.DeficitId
                && correction.OrganizationId == expectedCorrection.OrganizationId
                && correction.CounterpartyId == expectedCorrection.CounterpartyId
                && correction.CurrencyId == expectedCorrection.CurrencyId
                && correction.DeficitAmountAtCreation ==
                    expectedCorrection.DeficitAmountAtCreation
                && correction.DeficitAmountKztAtCreation ==
                    expectedCorrection.DeficitAmountKztAtCreation
                && correction.CorrectionAmount == expectedCorrection.CorrectionAmount
                && correction.CorrectionAmountKzt == expectedCorrection.CorrectionAmountKzt
                && correction.Basis == expectedCorrection.Basis
                && correction.ResponsibleAccountantId ==
                    expectedCorrection.ResponsibleAccountantId
                && correction.FinancialDirectorId == expectedCorrection.FinancialDirectorId
                && correction.State == expectedCorrection.State
                && correction.DecidedAt == expectedCorrection.DecidedAtUtc
                && correction.DecisionComment == expectedCorrection.DecisionComment
                && correction.RejectionReason == expectedCorrection.RejectionReason
                && correction.CreationCommandId == expectedCorrection.CreationCommandId
                && correction.DecisionCommandId == expectedCorrection.DecisionCommandId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        correction => correction.FinancialDirectorId,
                        decision.FinancialDirectorId)
                    .SetProperty(correction => correction.State, decision.State.ToString())
                    .SetProperty(correction => correction.DecidedAt, decidedAtUtc)
                    .SetProperty(
                        correction => correction.DecisionComment,
                        decision.DecisionComment)
                    .SetProperty(
                        correction => correction.RejectionReason,
                        decision.RejectionReason)
                    .SetProperty(
                        correction => correction.DecisionCommandId,
                        decisionCommandId),
                cancellationToken);
    }

    private IQueryable<StoredOverpaymentDeficit> BuildDeficitQuery(Guid? deficitId = null)
    {
        var deficits = dbContext.OverpaymentDeficits.AsNoTracking();
        if (deficitId.HasValue)
        {
            deficits = deficits.Where(deficit => deficit.DeficitId == deficitId.Value);
        }

        return
            from deficit in deficits
            join currency in dbContext.Currencies.AsNoTracking()
                on deficit.CurrencyId equals currency.Id
            select new StoredOverpaymentDeficit(
                deficit.OrganizationId,
                deficit.CounterpartyId,
                deficit.CurrencyId,
                currency.Code,
                deficit.DeficitId,
                deficit.OriginalAmount,
                deficit.OriginalAmountKzt,
                deficit.CurrentAmount,
                deficit.CurrentAmountKzt,
                deficit.SourceOverpaymentId,
                deficit.ExternalReversalId,
                deficit.OccurredAt,
                deficit.SourceExpenseRequestId,
                deficit.CreationCommandId,
                deficit.ActualCorrectionExecutorId,
                deficit.SettledAt);
    }

    private IQueryable<StoredManualOverpaymentDeficitCorrection> BuildCorrectionQuery(
        Guid? correctionId = null,
        Guid? pendingDeficitId = null)
    {
        var corrections = dbContext.ManualOverpaymentDeficitCorrections.AsNoTracking();
        if (correctionId.HasValue)
        {
            corrections = corrections.Where(correction =>
                correction.CorrectionId == correctionId.Value);
        }
        if (pendingDeficitId.HasValue)
        {
            corrections = corrections.Where(correction =>
                correction.DeficitId == pendingDeficitId.Value &&
                correction.State == "PendingApproval");
        }

        return
            from correction in corrections
            join currency in dbContext.Currencies.AsNoTracking()
                on correction.CurrencyId equals currency.Id
            orderby correction.CorrectionId
            select new StoredManualOverpaymentDeficitCorrection(
                correction.CorrectionId,
                correction.DeficitId,
                correction.OrganizationId,
                correction.CounterpartyId,
                correction.CurrencyId,
                currency.Code,
                correction.DeficitAmountAtCreation,
                correction.DeficitAmountKztAtCreation,
                correction.CorrectionAmount,
                correction.CorrectionAmountKzt,
                correction.Basis,
                correction.ResponsibleAccountantId,
                correction.FinancialDirectorId,
                correction.State,
                correction.DecidedAt,
                correction.DecisionComment,
                correction.RejectionReason,
                correction.CreationCommandId,
                correction.DecisionCommandId);
    }

    private IQueryable<StoredManualDeficitSettlement> BuildManualSettlementQuery(
        Guid? correctionId = null,
        Guid? commandId = null)
    {
        var settlements = dbContext.OverpaymentDeficitSettlements
            .AsNoTracking()
            .Where(settlement => settlement.CorrectionId != null);
        if (correctionId.HasValue)
        {
            settlements = settlements.Where(settlement =>
                settlement.CorrectionId == correctionId.Value);
        }
        if (commandId.HasValue)
        {
            settlements = settlements.Where(settlement => settlement.CommandId == commandId.Value);
        }

        return
            from settlement in settlements
            orderby settlement.DeficitId
            select new StoredManualDeficitSettlement(
                settlement.DeficitId,
                settlement.CommandId,
                settlement.Kind,
                settlement.CorrectionId!.Value,
                settlement.Amount,
                settlement.AmountKzt,
                settlement.AppliedAt,
                settlement.UserId);
    }

    private void EnsureCleanSerializableAttempt()
    {
        if (!HasSerializableTransaction || HasPendingChanges)
        {
            throw new InvalidOperationException(
                "A clean Serializable transaction is required for manual correction writes.");
        }
    }
}
