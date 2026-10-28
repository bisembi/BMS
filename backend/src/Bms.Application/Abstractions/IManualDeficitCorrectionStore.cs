using Bms.Application.OverpaymentDeficits;
using Bms.Domain.Overpayments;

namespace Bms.Application.Abstractions;

/// <summary>
/// Inner persistence operations for an already-authorized Serializable attempt.
/// Writes reuse W150 and never own commit, audit, outbox or HTTP idempotency.
/// Returned snapshots must represent the complete post-write state.
/// </summary>
public interface IManualDeficitCorrectionStore
{
    // Exactly one transaction-owned command gate, before idempotency and
    // snapshot reads. Approve/reject share a gate across all actors and keys.
    Task AcquireCommandGateAsync(Guid id, ManualDeficitCorrectionOperation operation, CancellationToken cancellationToken);
    Task<OverpaymentDeficitSnapshot?> FindDeficitAsync(Guid id, CancellationToken cancellationToken);
    Task<ManualDeficitCorrectionContext?> FindCorrectionAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> HasPendingCorrectionAsync(Guid deficitId, CancellationToken cancellationToken);

    Task<ManualDeficitCorrectionContext> CreateAsync(
        Guid commandId, Guid correctionId, Guid deficitId, decimal amount, string basis,
        Guid responsibleAccountantId, CancellationToken cancellationToken);

    Task<ManualDeficitCorrectionContext> DecideAsync(
        Guid commandId, Guid correctionId, bool approve, Guid financialDirectorId,
        string? decisionComment, string? rejectionReason, DateTimeOffset decidedAtUtc,
        CancellationToken cancellationToken);
}
