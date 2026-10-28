namespace Bms.Application.Abstractions;

/// <summary>
/// Internal cross-layer outcome marker: the database commit completed, but
/// cleanup after commit did not. It carries no database or business values in
/// its own message and must never be serialized as a public API contract.
/// </summary>
internal sealed class BusinessTransactionCommittedException : Exception
{
    private const string SafeMessage =
        "The business transaction committed, but post-commit cleanup did not complete.";

    internal BusinessTransactionCommittedException(Exception failure)
        : base(
            SafeMessage,
            failure ?? throw new ArgumentNullException(nameof(failure)))
    {
    }

    internal Exception Failure => InnerException!;
}

internal static class ProcessFatalExceptionClassifier
{
    internal static bool IsFatal(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is OutOfMemoryException or StackOverflowException or
            AccessViolationException or AppDomainUnloadedException or BadImageFormatException)
        {
            return true;
        }

        if (exception is AggregateException aggregate)
        {
            return aggregate.InnerExceptions.Any(IsFatal);
        }

        return exception.InnerException is not null && IsFatal(exception.InnerException);
    }
}
