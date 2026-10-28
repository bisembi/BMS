using Bms.Api.Diagnostics;
using Bms.Application.Abstractions;

namespace Bms.Api.Security;

/// <summary>
/// Fails a production-like host before it serves requests when the complete
/// current-actor dependency graph cannot be composed. Resolving the graph does
/// not execute an identity projection query or contact an external provider.
/// </summary>
public static class ProductionIdentityCompositionStartupGuard
{
    internal const string IncompleteCompositionMessage =
        "The production identity service composition is incomplete.";

    public static void EnsureConfigured(
        IHostEnvironment environment,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(services);

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return;
        }

        try
        {
            using var scope = services.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<ICurrentActor>();
        }
        catch (Exception exception) when (!ApiProcessFatalExceptionClassifier.IsFatal(exception))
        {
            throw new InvalidOperationException(IncompleteCompositionMessage);
        }
    }
}
