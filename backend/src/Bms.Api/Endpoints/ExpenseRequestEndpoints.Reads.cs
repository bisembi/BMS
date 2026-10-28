using Bms.Domain.ExpenseRequests;
using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Application.Common;
using Bms.Application.Authorization;
using Bms.Application.ExpenseRequests;
using Bms.Application.Abstractions;

namespace Bms.Api.Endpoints;

/// Reading one expense request and the filtered list.
public static partial class ExpenseRequestEndpoints
{

    private static async Task<IResult> GetAsync(
        Guid id,
        ExpenseRequestService service,
        IEmployeeActorMappingReader employeeActorMappingReader,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            response.Headers.CacheControl = "no-store";
            var result = await service.GetDetailAsync(id, cancellationToken);
            var visible = await WithInitiatorDisplayNameAsync(
                result, employeeActorMappingReader, cancellationToken);
            return visible is null ? Results.NotFound() : Results.Ok(visible);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ExpenseRequestSubstitutionAuthorityUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                operation: ApiFailureOperation.ExpenseRequestSubstitutionAuthority);
            return SafeProblemResults.ExpenseRequestSubstitutionAuthorityUnavailable();
        }
        catch (BudgetOwnerCreationAuthorityUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                operation: ApiFailureOperation.ExpenseRequestCreationAuthority);
            return SafeProblemResults.ExpenseRequestCreationAuthorityUnavailable();
        }
    }

    // Presentation-only lookup AFTER the existing document visibility check.
    // It never rewrites the stored actor identity or grants document authority.
    internal static async Task<ExpenseRequestDetailDto?> WithInitiatorDisplayNameAsync(
        ExpenseRequestDetailDto? visible,
        IEmployeeActorMappingReader reader,
        CancellationToken cancellationToken)
    {
        if (visible is null) return null;
        cancellationToken.ThrowIfCancellationRequested();
        string? name = null;
        try
        {
            var actor = visible.ExpenseRequest.InitiatorExternalId;
            if (ActorIdentity.IsCanonical(actor))
            {
                var rows = await reader.ReadAsync(actor, cancellationToken);
                if (rows.Count == 1)
                {
                    var row = rows[0];
                    if (row.EmployeeId != Guid.Empty
                        && string.Equals(row.ActorExternalId, actor, StringComparison.Ordinal)
                        && row.MappingIsActive && row.EmployeeIsActive
                        && !string.IsNullOrWhiteSpace(row.EmployeeName))
                    {
                        var candidate = row.EmployeeName.Trim();
                        if (candidate.Length <= 256 && !candidate.Any(char.IsControl)
                            && !candidate.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
                            name = candidate;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Like the session display name, this optional catalogue read must
            // not turn an already-authorized card into an authority change.
        }
        return visible with { InitiatorDisplayName = name };
    }

    /// <summary>
    /// The read-only payment summary. Visibility equals the visibility of the
    /// request itself, the response is never cached, and a ledger outage
    /// answers 503 rather than pretending the request is unpaid.
    /// </summary>
    private static async Task<IResult> GetPaymentSummaryAsync(
        Guid id,
        ExpenseRequestService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            response.Headers.CacheControl = "no-store";
            var result = await service.GetPaymentSummaryAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (ExpenseRequestPaymentSummaryUnavailableException)
        {
            return SafeProblemResults.PaymentSummaryUnavailable();
        }
    }

    internal static async Task<IResult> ListAsync(
        int? skip,
        int? take,
        bool? isUrgent,
        DateOnly? plannedPaymentDate,
        Guid? organizationId,
        string? status,
        Guid? costCenterId,
        Guid? counterpartyId,
        string? initiatorExternalId,
        bool? onlyMyTasks,
        DateTimeOffset? createdFromUtc,
        DateTimeOffset? createdBeforeUtc,
        bool? includeBusinessColumns,
        ExpenseRequestService service,
        HttpResponse response,
        CancellationToken cancellationToken,
        IEmployeeActorDisplayNamesReader? employeeActorDisplayNamesReader = null,
        bool? includeInitiatorDisplayName = null,
        string? sortBy = null,
        string? sortDirection = null,
        string? search = null,
        Guid? initiatorEmployeeId = null,
        bool? includeRequestDate = null,
        bool? hideDrafts = null,
        bool? retainPaidBudget = null)
    {
        try
        {
            response.Headers.CacheControl = "no-store";
            if (skip is < 0 || take is < 1 or > 200)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(skip),
                    "Expense-request list paging is outside its public range.");
            }

            var result = await service.ListAsync(
                skip ?? 0,
                take ?? 50,
                new ExpenseRequestFilter(
                    IsUrgent: isUrgent,
                    PlannedPaymentDate: plannedPaymentDate,
                    OrganizationId: organizationId,
                    Status: ParseStatusFilter(status),
                    CostCenterId: costCenterId,
                    CounterpartyId: counterpartyId,
                    InitiatorExternalId: initiatorExternalId,
                    OnlyMyTasks: onlyMyTasks ?? false,
                    CreatedFromUtc: createdFromUtc,
                    CreatedBeforeUtc: createdBeforeUtc) { SortBy = sortBy, SortDirection = sortDirection, Search = search, InitiatorEmployeeId = initiatorEmployeeId, HideDrafts = hideDrafts ?? false },
                includeBusinessColumns ?? false,
                cancellationToken);
            // Old open tabs validate their original DTO strictly. Date display
            // is additive opt-in; default SQL ordering is date-based for everyone.
            if (includeRequestDate != true)
                result = result with { Items = result.Items.Select(item => item with { RequestDateUtc = null }).ToArray() };
            // Old open tabs require every indicator to be null on degradation.
            // New clients opt into immutable paid-history values, never live fallback.
            if (result.BudgetIndicatorsUnavailable == true && retainPaidBudget != true)
                result = result with { Items = result.Items.Select(item => item with { IsOverBudget = null }).ToArray() };
            return Results.Ok(includeInitiatorDisplayName != true || employeeActorDisplayNamesReader is null ? result
                : await WithInitiatorDisplayNamesAsync(result, employeeActorDisplayNamesReader, cancellationToken));
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ArgumentOutOfRangeException)
        {
            ApiFailureLog.InvalidRequest(
                response.HttpContext,
                ApiFailureOperation.ExpenseRequestList);
            return SafeProblemResults.InvalidRequest();
        }
        catch (ArgumentException)
        {
            ApiFailureLog.InvalidRequest(
                response.HttpContext,
                ApiFailureOperation.ExpenseRequestList);
            return SafeProblemResults.InvalidRequest();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (ExpenseRequestListBusinessColumnsUnavailableException)
        {
            return SafeProblemResults.ListBusinessColumnsUnavailable();
        }
    }

    internal static async Task<ExpenseRequestPageDto> WithInitiatorDisplayNamesAsync(
        ExpenseRequestPageDto visible, IEmployeeActorDisplayNamesReader reader, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var actors=visible.Items.Select(i=>i.InitiatorExternalId).Where(ActorIdentity.IsCanonical).Distinct(StringComparer.Ordinal).ToArray();
        if(visible.Items.Count>100)throw new ArgumentException("Presentation page exceeds public bounds.");
        IReadOnlyList<EmployeeActorMappingProjectionRow> rows=[];
        if(actors.Length>0)try { rows=await reader.ReadAsync(actors,token) ?? []; }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch { /* Optional presentation failure never changes visible items or authorization. */ }
        token.ThrowIfCancellationRequested();
        var items=visible.Items.Select(item=>{
            var matches=rows.Where(r=>string.Equals(r.ActorExternalId,item.InitiatorExternalId,StringComparison.Ordinal)).ToArray();
            string? name=null;
            if(ActorIdentity.IsCanonical(item.InitiatorExternalId) && matches.Length==1){
                var r=matches[0];var candidate=r.EmployeeName?.Trim();
                if(r.EmployeeId!=Guid.Empty && r.MappingIsActive && r.EmployeeIsActive && !string.IsNullOrWhiteSpace(candidate)
                    && candidate.Length<=256 && !candidate.Any(char.IsControl) && !candidate.StartsWith("S-1-",StringComparison.OrdinalIgnoreCase))name=candidate;
            }
            return item with {InitiatorDisplayName=name};
        }).ToArray();
        return visible with {Items=items};
    }

    private static ExpenseRequestStatus? ParseStatusFilter(string? statusCode)
    {
        if (statusCode is null)
        {
            return null;
        }

        if (!ExpenseRequestStatusCode.TryParse(statusCode, out var status))
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode));
        }

        return status;
    }
}
