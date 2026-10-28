using Bms.Domain.ExpenseRequests;

namespace Bms.Application.Abstractions;

/// <summary>
/// Prepares the immutable accrual snapshot on a clean tracked request inside
/// the caller's Serializable transaction. It does not persist: the caller
/// composes it with the workflow transition and saves the unit of work once.
/// </summary>
public interface IAccrualSnapshotMaterializer
{
    Task<AccrualSnapshotPreparation> PrepareAsync(
        ExpenseRequest request,
        Guid expectedVersion,
        CancellationToken cancellationToken);
}

/// <summary>
/// A source-data issue is an expected no-write outcome; an inconsistent
/// tracked aggregate or persistence runtime is reported as an exception.
/// </summary>
public sealed record AccrualSnapshotPreparation(
    bool IsReady,
    Guid RequestVersion);
