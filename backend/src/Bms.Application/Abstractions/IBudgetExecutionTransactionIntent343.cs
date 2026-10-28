namespace Bms.Application.Abstractions;

/// <summary>Host-owned transaction intent, never a flag from a request body.
/// GET/HEAD and the existing read-only draft preview may only acquire a shared
/// register gate; background and financial write operations acquire exclusive.</summary>
public interface IBudgetExecutionTransactionIntent343
{
    bool IsReadOnly { get; }
}
