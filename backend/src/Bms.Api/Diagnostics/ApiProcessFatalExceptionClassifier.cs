namespace Bms.Api.Diagnostics;

internal static class ApiProcessFatalExceptionClassifier
{
    internal static bool IsFatal(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (IsDirectFatal(exception))
        {
            return true;
        }

        if (exception is AggregateException aggregate)
        {
            return aggregate.InnerExceptions.Any(IsFatal);
        }

        var current = exception;
        var nested = current.InnerException;
        return nested is not null && IsFatal(nested);
    }

    private static bool IsDirectFatal(Exception exception) =>
        exception is OutOfMemoryException or StackOverflowException or
        AccessViolationException or AppDomainUnloadedException or BadImageFormatException;
}
