using Bms.Api.Problems;
using Bms.Api.Security;
using Microsoft.AspNetCore.Diagnostics;

namespace Bms.Api.Diagnostics;

/// Handles failures that escaped endpoint-specific boundaries without accepting
/// exception text into either the public response or the operational log.
internal sealed class SafeUnhandledExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);
        _ = cancellationToken;

        ApiFailureLog.Unhandled(httpContext);
        // ExceptionHandlerMiddleware clears the inner pipeline headers while
        // replacing the response. Reapply the reviewed API security boundary
        // so an unhandled exception has the same browser protections as any
        // endpoint-owned failure.
        ApiResponseSecurityHeadersMiddleware.ApplyTo(httpContext.Response);
        await SafeProblemResults.InternalServerError().ExecuteAsync(httpContext);
        return true;
    }
}
