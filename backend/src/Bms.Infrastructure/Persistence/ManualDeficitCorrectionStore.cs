using Bms.Application.Abstractions;
using Bms.Application.OverpaymentDeficits;
using Bms.Domain.Overpayments;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Bridges the application transaction to W150. A single outer command gate
/// precedes all reads; the inner writer retains its shared treasury lock order,
/// conditional writes and post-write verification. This adapter never commits.
/// </summary>
internal sealed class ManualDeficitCorrectionStore : IManualDeficitCorrectionStore
{
    private readonly ITreasuryManualDeficitCorrectionRuntime runtime;

    public ManualDeficitCorrectionStore(BmsDbContext dbContext)
        : this(new EfCoreTreasuryManualDeficitCorrectionRuntime(dbContext))
    {
    }

    internal ManualDeficitCorrectionStore(ITreasuryManualDeficitCorrectionRuntime runtime)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public async Task AcquireCommandGateAsync(Guid id, ManualDeficitCorrectionOperation operation,
        CancellationToken cancellationToken)
    {
        EnsureCleanAttempt(cancellationToken);
        await runtime.AcquireExclusiveTransactionLockAsync(CommandLockResource(id, operation), cancellationToken);
    }

    internal static string CommandLockResource(Guid id, ManualDeficitCorrectionOperation operation)
    {
        if (id == Guid.Empty) throw new ManualDeficitCorrectionUnavailableException();
        var kind = operation switch
        {
            ManualDeficitCorrectionOperation.Create => "deficit",
            ManualDeficitCorrectionOperation.Approve or ManualDeficitCorrectionOperation.Reject => "correction",
            _ => throw new ManualDeficitCorrectionUnavailableException()
        };
        // Do not include actor, key or approve/reject: competing decisions must
        // wait before taking shared snapshot locks or idempotency range locks.
        return $"bms-web:manual-deficit-workflow:{kind}:{id:N}";
    }

    public async Task<OverpaymentDeficitSnapshot?> FindDeficitAsync(Guid id, CancellationToken cancellationToken)
    {
        EnsureCleanAttempt(cancellationToken);
        var row = await runtime.FindDeficitByIdAsync(id, cancellationToken);
        return row is null ? null : ToSnapshot(row);
    }

    public async Task<ManualDeficitCorrectionContext?> FindCorrectionAsync(Guid id, CancellationToken cancellationToken)
    {
        EnsureCleanAttempt(cancellationToken);
        var row = await runtime.FindCorrectionByIdAsync(id, cancellationToken);
        if (row is null) return null;
        var deficit = await runtime.FindDeficitByIdAsync(row.DeficitId, cancellationToken)
            ?? throw new ManualDeficitCorrectionUnavailableException();
        return new(ToSnapshot(row), ToSnapshot(deficit));
    }

    public async Task<bool> HasPendingCorrectionAsync(Guid deficitId, CancellationToken cancellationToken)
    {
        EnsureCleanAttempt(cancellationToken);
        var rows = await runtime.FindPendingCorrectionsByDeficitAsync(deficitId, cancellationToken);
        if (rows.Count > 1) throw new ManualDeficitCorrectionUnavailableException();
        return rows.Count == 1;
    }

    public async Task<ManualDeficitCorrectionContext> CreateAsync(
        Guid commandId, Guid correctionId, Guid deficitId, decimal amount, string basis,
        Guid responsibleAccountantId, CancellationToken cancellationToken)
    {
        EnsureCleanAttempt(cancellationToken);
        var result = await TreasuryManualDeficitCorrectionWriter.CreateAsync(
            new(commandId, correctionId, deficitId, amount, basis, responsibleAccountantId),
            runtime, cancellationToken);
        var deficit = await runtime.FindDeficitByIdAsync(deficitId, cancellationToken)
            ?? throw new ManualDeficitCorrectionUnavailableException();
        return new(ToSnapshot(result.Correction), ToSnapshot(deficit));
    }

    public async Task<ManualDeficitCorrectionContext> DecideAsync(
        Guid commandId, Guid correctionId, bool approve, Guid financialDirectorId,
        string? decisionComment, string? rejectionReason, DateTimeOffset decidedAtUtc,
        CancellationToken cancellationToken)
    {
        EnsureCleanAttempt(cancellationToken);
        var result = await TreasuryManualDeficitCorrectionWriter.DecideAsync(
            new(commandId, correctionId,
                approve ? TreasuryManualDeficitCorrectionDecisionKind.Approve
                    : TreasuryManualDeficitCorrectionDecisionKind.Reject,
                financialDirectorId, decisionComment, rejectionReason),
            decidedAtUtc, runtime, cancellationToken);
        return new(ToSnapshot(result.Correction), ToSnapshot(result.Deficit));
    }

    private void EnsureCleanAttempt(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!runtime.HasSerializableTransaction || runtime.HasPendingChanges)
            throw new ManualDeficitCorrectionUnavailableException();
    }

    private static OverpaymentDeficitSnapshot ToSnapshot(StoredOverpaymentDeficit row) => new(
        row.DeficitId, row.OrganizationId, row.CounterpartyId, row.CurrencyCode,
        row.OccurredAtUtc, row.OriginalAmount, row.OriginalAmountKzt, row.CurrentAmount, row.CurrentAmountKzt);

    internal static ManualDeficitCorrectionSnapshot ToSnapshot(StoredManualOverpaymentDeficitCorrection row)
    {
        var state = row.State switch
        {
            "PendingApproval" => ManualOverpaymentDeficitCorrectionState.PendingApproval,
            "ApprovedAndApplied" => ManualOverpaymentDeficitCorrectionState.ApprovedAndApplied,
            "Rejected" => ManualOverpaymentDeficitCorrectionState.Rejected,
            "Annulled" => ManualOverpaymentDeficitCorrectionState.Annulled,
            _ => throw new ManualDeficitCorrectionUnavailableException()
        };
        var proposal = new ManualOverpaymentDeficitCorrectionProposal(
            row.CorrectionId, row.DeficitId, new(row.OrganizationId, row.CounterpartyId, row.CurrencyCode),
            row.DeficitAmountAtCreation, row.DeficitAmountKztAtCreation,
            row.CorrectionAmount, row.CorrectionAmountKzt, row.Basis, row.ResponsibleAccountantId, state);
        return new(proposal, row.CreationCommandId, row.DecisionCommandId, row.FinancialDirectorId,
            row.DecidedAtUtc, row.DecisionComment, row.RejectionReason);
    }
}
