namespace Bms.Application.Abstractions;

public sealed record TreasuryPaymentDateState(Guid RequestId, Guid RequestVersion, DateOnly? Date,
    DateOnly DefaultDate, bool CanEdit, bool Confirmed);
public sealed record SetTreasuryPaymentDateCommand(Guid ExpectedVersion, DateOnly Date);
public sealed record TreasuryPaymentDateSaveResult(TreasuryPaymentDateState State, Bms.Application.ExpenseRequests.ExpenseRequestDto Request);
public sealed class TreasuryPaymentDateConfirmedException() : Exception("Confirmed payment date cannot be changed.");
public sealed class TreasuryReschedulingPausedException() : Exception("Treasury rescheduling is paused.");
public sealed class TreasuryBudgetCheckUnavailableException() : Exception("Source-aware treasury budget check is unavailable.");

public interface ITreasuryPaymentDateService
{
    Task<TreasuryPaymentDateState> ReadAsync(Guid id, CancellationToken token);
    Task<TreasuryPaymentDateSaveResult> SetAsync(Guid id, SetTreasuryPaymentDateCommand command, string key, CancellationToken token);
}

/// <summary>Append-only, request-locked treasury date evidence. Never the initiator's planned date.</summary>
public interface ITreasuryPaymentDateJournal
{
    Task<bool> PrepareHandoffAsync(Guid requestId, Guid expectedVersion, string actorExternalId,
        DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.FromResult(true);
    Task RecordHandoffAsync(Guid requestId, Guid previousVersion, Guid newVersion, string actorExternalId,
        DateTimeOffset occurredAtUtc, CancellationToken cancellationToken);
}
