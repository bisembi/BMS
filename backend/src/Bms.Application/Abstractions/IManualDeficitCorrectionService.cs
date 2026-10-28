using Bms.Application.OverpaymentDeficits;

namespace Bms.Application.Abstractions;

public interface IManualDeficitCorrectionService
{
    Task<ManualDeficitCorrectionCommandResult> CreateAsync(
        Guid deficitId, CreateManualDeficitCorrectionCommand command,
        string idempotencyKey, CancellationToken cancellationToken);

    Task<ManualDeficitCorrectionCommandResult> ApproveAsync(
        Guid correctionId, ApproveManualDeficitCorrectionCommand command,
        string idempotencyKey, CancellationToken cancellationToken);

    Task<ManualDeficitCorrectionCommandResult> RejectAsync(
        Guid correctionId, RejectManualDeficitCorrectionCommand command,
        string idempotencyKey, CancellationToken cancellationToken);

    Task<ManualDeficitCorrectionDto?> ResolveIdempotencyAsync(
        Guid resourceId, ManualDeficitCorrectionOperation operation,
        string idempotencyKey, CancellationToken cancellationToken);
}
