using Microsoft.AspNetCore.Http.Features;

namespace Bms.Api.Diagnostics;

internal enum ApiFailureOperation
{
    BusinessAuditSearch = 1,
    ExpenseRequestExport = 2,
    ExpenseRequestList = 3,
    ReferenceDataContracts = 4,
    ExpenseRequestCreateCurrencyCalculation = 5,
    ExpenseRequestUpdateCurrencyCalculation = 6,
    ExpenseRequestSubmissionPreflight = 7,
    IntegrationOutboxBacklogSummary = 8,
    IntegrationReferenceProjectionInventory = 9,
    IntegrationDatabaseReadiness = 10,
    ExpenseRequestWorkflowSubmission = 11,
    ExpenseRequestWorkflowIdempotencyResolution = 12,
    ExpenseRequestWorkflowCommand = 13,
    ExpenseRequestWorkflowCommandIdempotencyResolution = 14,
    ExpenseRequestWorkflowCommandAvailability = 15,
    ExpenseRequestCreationAuthority = 16,
    BudgetExecutionList = 17,
    BudgetExecutionExport = 18,
    ExpenseRequestClosingCommand = 19,
    ExpenseRequestClosingIdempotencyResolution = 20,
    ExpenseRequestClosingState = 21,
    ActiveAccrualList = 22,
    ActiveAccrualExport = 23,
    ApprovalMatrixRead = 24,
    AccrualNotificationList = 25,
    AccrualNotificationMonitor = 26,
    AccrualNotificationAdministrationQueue = 27,
    AccrualNotificationAdministrationIssues = 28,
    ManualDeficitList = 29,
    ManualDeficitDetail = 30,
    ManualDeficitCommand = 31,
    ManualDeficitRecovery = 32,
    CashFlowAccrualSettingsRead = 33,
    CashFlowAccrualSettingsWrite = 34,
    ExpenseRequestPaymentDetails = 35,
    ExpenseRequestSubstitutionAuthority = 36,
    ExpenseRequestAttachments = 37,
    BusinessAuditReadAuthority = 38,
    ReferenceDataCashFlowItemQuery = 39,
    ReferenceDataCashFlowItemResolve = 40,
    BudgetInstanceList = 41,
    BudgetInstanceRead = 42,
    BudgetInstanceWrite = 43,
    BudgetDirectoryList = 44,
    BudgetDirectoryRead = 45,
    BudgetDirectoryWrite = 46,
    AuthorizationDirectoryList = 47,
    AuthorizationDirectoryRead = 48,
    AuthorizationDirectoryWrite = 49,
    ReferenceDataExpenseItemWrite = 50,
    ReferenceDataOrganizationWrite = 51,
    CashFlowCatalogOrderRead = 52,
    CashFlowCatalogOrderWrite = 53,
}

/// Fixed, redacted operational events for failures that are intentionally
/// hidden from the HTTP response. No exception, request value, SQL text or
/// connection detail is accepted by this boundary.
internal static class ApiFailureLog
{
    internal const int InvalidRequestEventId = 9601;
    internal const int DependencyUnavailableEventId = 9602;
    internal const int UnhandledFailureEventId = 9603;
    internal const string CategoryName = "Bms.Api.RedactedFailures";
    internal const string UnavailableTraceIdentifier = ApiTraceIdentifier.Unavailable;

    private static readonly Action<ILogger, ApiFailureOperation, string, Exception?>
        InvalidRequestMessage = LoggerMessage.Define<ApiFailureOperation, string>(
            LogLevel.Information,
            new EventId(InvalidRequestEventId, "InvalidRequestRejected"),
            "Invalid API request rejected. Operation={Operation}; TraceIdentifier={TraceIdentifier}.");

    private static readonly Action<ILogger, ApiFailureOperation, string, Exception?>
        DependencyUnavailableMessage = LoggerMessage.Define<ApiFailureOperation, string>(
            LogLevel.Warning,
            new EventId(DependencyUnavailableEventId, "DependencyUnavailable"),
            "API dependency unavailable. Operation={Operation}; TraceIdentifier={TraceIdentifier}.");

    private static readonly Action<ILogger, string, Exception?> UnhandledFailureMessage =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(UnhandledFailureEventId, "UnhandledFailure"),
            "Unhandled API request failed. TraceIdentifier={TraceIdentifier}.");

    internal static void InvalidRequest(
        HttpContext? context,
        ApiFailureOperation operation,
        string? traceIdentifier = null) =>
        Write(context, operation, traceIdentifier, InvalidRequestMessage);

    internal static void DependencyUnavailable(
        HttpContext? context,
        ApiFailureOperation operation,
        string? traceIdentifier = null) =>
        Write(context, operation, traceIdentifier, DependencyUnavailableMessage);

    internal static void Unhandled(HttpContext? context, string? traceIdentifier = null)
    {
        TryWriteBestEffort(() =>
        {
            var loggerFactory = LoggerFactory(context);
            if (loggerFactory is null)
            {
                return;
            }

            UnhandledFailureMessage(
                loggerFactory.CreateLogger(CategoryName),
                ApiTraceIdentifier.Canonicalize(traceIdentifier ?? context?.TraceIdentifier),
                null);
        });
    }

    private static void Write(
        HttpContext? context,
        ApiFailureOperation operation,
        string? traceIdentifier,
        Action<ILogger, ApiFailureOperation, string, Exception?> write)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        TryWriteBestEffort(() =>
        {
            var loggerFactory = LoggerFactory(context);
            if (loggerFactory is null)
            {
                return;
            }

            write(
                loggerFactory.CreateLogger(CategoryName),
                operation,
                ApiTraceIdentifier.Canonicalize(traceIdentifier ?? context?.TraceIdentifier),
                null);
        });
    }

    private static void TryWriteBestEffort(Action write)
    {
        try
        {
            write();
        }
        catch (Exception exception) when (!ApiProcessFatalExceptionClassifier.IsFatal(exception))
        {
            // A failed observability sink must not replace the reviewed API outcome.
        }
    }

    private static ILoggerFactory? LoggerFactory(HttpContext? context) => context?
        .Features
        .Get<IServiceProvidersFeature>()?
        .RequestServices?
        .GetService<ILoggerFactory>();
}
