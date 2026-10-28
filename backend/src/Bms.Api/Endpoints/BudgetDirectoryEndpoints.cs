using System.Globalization;
using Bms.Api.Diagnostics;
using Bms.Api.Problems;
using Bms.Api.Security;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;

namespace Bms.Api.Endpoints;

/// <summary>
/// Budget-directory maintenance only. These selectors do not define the CFOs
/// in which a budget editor may create or edit a document.
/// </summary>
public static partial class BudgetDirectoryEndpoints
{
    public static RouteGroupBuilder MapBudgetDirectoryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/directory/cost-centers", ListCostCentersAsync)
            .WithName("ListBudgetDirectoryCostCenters");
        group.MapGet("/directory/employees", ListEmployeesAsync)
            .WithName("ListBudgetDirectoryEmployees");
        group.MapGet("/directory/cost-centers/{id:guid}", GetAsync)
            .WithName("GetBudgetDirectoryCostCenter");
        group.MapPut("/directory/cost-centers/{id:guid}", UpdateAsync)
            .RequireCsrfToken()
            .WithName("UpdateBudgetDirectoryCostCenter");
        group.MapPut("/directory/cost-centers/{id:guid}/purpose", SetPurpose457Async)
            .RequireCsrfToken().WithName("SetCostCenterPurpose457");
        return group;
    }

    private static async Task<IResult> ListCostCentersAsync(
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        BudgetDirectoryService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            var page = ParsePage(request);
            return Results.Ok(await service.ListCostCentersAsync(
                page.Query, page.Skip, page.Take, cancellationToken));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return MapException(request, exception, ApiFailureOperation.BudgetDirectoryList);
        }
    }

    private static async Task<IResult> ListEmployeesAsync(
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        BudgetDirectoryService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            var page = ParsePage(request);
            return Results.Ok(await service.ListEmployeesAsync(
                page.Query, page.Skip, page.Take, cancellationToken));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return MapException(request, exception, ApiFailureOperation.BudgetDirectoryList);
        }
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        BudgetDirectoryService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            if (request.Query.Count != 0)
            {
                return InvalidRequest(request, ApiFailureOperation.BudgetDirectoryRead);
            }

            return Results.Ok(await service.GetAsync(id, cancellationToken));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return MapException(request, exception, ApiFailureOperation.BudgetDirectoryRead);
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateBudgetDirectoryCommand? command,
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        BudgetDirectoryService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            if (command is null || request.Query.Count != 0)
            {
                return InvalidRequest(request, ApiFailureOperation.BudgetDirectoryWrite);
            }

            var key = RequireIdempotencyKey(request);
            return Results.Ok(await service.UpdateAsync(id, command, key, cancellationToken));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return MapException(request, exception, ApiFailureOperation.BudgetDirectoryWrite);
        }
    }

    // Cancellation and unexpected failures deliberately bypass this filter:
    // preserve cancellation/stack provenance and use the existing outer safe
    // exception boundary for faults which are not a reviewed API outcome.
    private static bool IsExpectedFailure(Exception exception) => exception is
        ActorNotAuthenticatedException
        or ActorForbiddenException
        or BudgetDirectoryNotFoundException
        or BudgetPrefixConflictException
        or BudgetInstanceConcurrencyConflictException
        or InvalidIdempotencyKeyException
        or IdempotencyConflictException
        or BudgetDocumentAuthorityUnavailableException
        or DomainRuleViolationException;

    private static IResult MapException(HttpRequest request, Exception exception, ApiFailureOperation operation) =>
        exception switch
        {
            ActorNotAuthenticatedException => SafeProblemResults.AuthenticationRequired(),
            ActorForbiddenException => SafeProblemResults.Forbidden(),
            BudgetDirectoryNotFoundException => SafeProblemResults.BudgetDirectoryNotFound(),
            BudgetPrefixConflictException => SafeProblemResults.BudgetPrefixConflict(),
            BudgetInstanceConcurrencyConflictException => SafeProblemResults.Conflict("version-conflict"),
            InvalidIdempotencyKeyException => SafeProblemResults.InvalidIdempotencyKey(),
            IdempotencyConflictException => SafeProblemResults.Conflict("idempotency-conflict"),
            BudgetDocumentAuthorityUnavailableException => AuthorityUnavailable(request, operation),
            DomainRuleViolationException => InvalidRequest(request, operation),
            _ => throw new InvalidOperationException("An unsupported budget-directory failure was mapped."),
        };

    private static IResult AuthorityUnavailable(HttpRequest request, ApiFailureOperation operation)
    {
        ApiFailureLog.DependencyUnavailable(request.HttpContext, operation);
        return SafeProblemResults.BudgetDirectoryUnavailable();
    }

    private static IResult InvalidRequest(HttpRequest request, ApiFailureOperation operation)
    {
        ApiFailureLog.InvalidRequest(request.HttpContext, operation);
        return SafeProblemResults.InvalidRequest();
    }

    private static (string? Query, int Skip, int Take) ParsePage(HttpRequest request)
    {
        if (request.Query.Keys.Any(key => key is not ("query" or "skip" or "take")))
        {
            throw new DomainRuleViolationException("The budget-directory query is invalid.");
        }

        var query = OptionalQuery(request);
        var skip = OptionalInt(request, "skip") ?? 0;
        var take = OptionalInt(request, "take") ?? 25;
        if (skip < 0 || take is < 1 or > 100)
        {
            throw new DomainRuleViolationException("The budget-directory page is invalid.");
        }

        return (query, skip, take);
    }

    private static string? OptionalQuery(HttpRequest request)
    {
        if (!request.Query.TryGetValue("query", out var values))
        {
            return null;
        }
        if (values.Count != 1)
        {
            throw new DomainRuleViolationException("One budget-directory query is required.");
        }

        var query = values[0] ?? string.Empty;
        if (query.Length > 200 || query.Any(char.IsControl))
        {
            throw new DomainRuleViolationException("The budget-directory query is invalid.");
        }

        return query.Trim();
    }

    private static int? OptionalInt(HttpRequest request, string name)
    {
        if (!request.Query.TryGetValue(name, out var values))
        {
            return null;
        }
        if (values.Count != 1
            || !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new DomainRuleViolationException("The budget-directory page is invalid.");
        }

        return parsed;
    }

    private static string RequireIdempotencyKey(HttpRequest request)
    {
        var values = request.Headers["Idempotency-Key"];
        if (values.Count != 1)
        {
            throw new InvalidIdempotencyKeyException("One idempotency key is required.");
        }

        var key = values[0] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key) || key.Length > 127)
        {
            throw new InvalidIdempotencyKeyException("One idempotency key is required.");
        }

        return key;
    }
}
