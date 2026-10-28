using Bms.Api.Diagnostics;
using Bms.Application.Abstractions;

namespace Bms.Api.Security;

public sealed class IntegrationAdministratorApiBoundaryMiddleware(RequestDelegate next)
{
    internal const string ApiPrefix = "/api/v1";
    internal const string SessionPath = "/api/v1/session";
    internal const string OutboxMonitorPath = "/api/v1/integration-monitor/outbox";
    internal const string OutboxBacklogSummaryPath = "/api/v1/integration-monitor/outbox-summary";
    internal const string DatabaseReadinessPath = "/api/v1/integration-monitor/database-readiness";
    internal const string AccrualNotificationMonitorPath = "/api/v1/integration-monitor/accrual-notifications";
    internal const string ReferenceProjectionInventoryPath =
        "/api/v1/integration-monitor/reference-projections";
    internal const string ProblemCode = "IntegrationAdministratorBusinessApiForbidden";

    public async Task InvokeAsync(HttpContext context, ICurrentActor currentActor)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(currentActor);

        if (!context.Request.Path.StartsWithSegments(ApiPrefix)
            || !currentActor.IsBmsIntegrationAdministrator
            || IsAllowedTechnicalPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        await Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Business API access is forbidden.",
            detail: "The integration administrator is restricted to technical service functions.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = ProblemCode,
                ["traceId"] = ApiTraceIdentifier.Canonicalize(context.TraceIdentifier)
            }).ExecuteAsync(context);
    }

    private static bool IsAllowedTechnicalPath(PathString path) =>
        string.Equals(path.Value, SessionPath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, Bms.Api.Endpoints.HelpGuideEndpoints.ExpenseRequestGuidePath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, Bms.Api.Endpoints.HelpGuideEndpoints.BudgetGuidePath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, Bms.Api.Endpoints.ActiveSessionEndpoints.PresencePath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, OutboxMonitorPath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, OutboxBacklogSummaryPath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, DatabaseReadinessPath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, AccrualNotificationMonitorPath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            path.Value,
            ReferenceProjectionInventoryPath,
            StringComparison.OrdinalIgnoreCase);
}
