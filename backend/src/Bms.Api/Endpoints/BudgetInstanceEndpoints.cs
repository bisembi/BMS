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
using Microsoft.AspNetCore.Mvc;

namespace Bms.Api.Endpoints;

/// <summary>
/// Budget-instance draft and approval surface. Extends the existing budget
/// route group so BmsApiHost does not need a new Map* call. Approval records
/// immutable metadata; it does not yet project Limit/Reserve/Paid values.
/// </summary>
public static class BudgetInstanceEndpoints
{
    public static RouteGroupBuilder MapBudgetInstanceEndpoints(this RouteGroupBuilder group)
    {
        group.MapBudgetApprovalWorkflow();
        group.MapBudgetAdjustments();
        group.MapGet("/instances", ListAsync).WithName("ListBudgetInstances");
        group.MapGet("/instances/cost-centers", ListCostCentersAsync).WithName("ListBudgetInstanceCostCenters");
        group.MapGet("/instances/creator-cost-center", GetCreatorCostCenterAsync).WithName("GetCreatorBudgetInstanceCostCenter");
        group.MapGet("/instances/creation-options", GetCreationOptionsAsync).WithName("GetBudgetInstanceCreationOptions");
        group.MapGet("/instances/{id:guid}", GetAsync).WithName("GetBudgetInstance");
        group.MapGet("/instances/{id:guid}/history", GetHistoryAsync).WithName("GetBudgetInstanceHistory");
        group.MapPost("/instances", CreateAsync).RequireCsrfToken().WithName("CreateBudgetInstance");
        group.MapPut("/instances/{id:guid}", UpdateAsync).RequireCsrfToken().WithName("UpdateBudgetInstance");
        group.MapDelete("/instances/{id:guid}", DeleteAsync).RequireCsrfToken().WithName("DeleteBudgetInstance");
        group.MapPost("/instances/{id:guid}/approve", ApproveAsync)
            .RequireCsrfToken()
            .WithName("ApproveBudgetInstance");
        return group;
    }

    private static async Task<IResult> GetHistoryAsync(Guid id, HttpRequest request, HttpResponse response,
        ICurrentActor actor, BudgetInstanceHistoryService service, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            if (request.Query.Keys.Any(key => key is not ("skip" or "take")))
                return SafeProblemResults.InvalidRequest();
            return Results.Ok(await service.ListAsync(id, OptionalInt(request, "skip") ?? 0,
                OptionalInt(request, "take") ?? 20, cancellationToken));
        }
        catch (Exception exception)
        {
            return MapException(request, exception, ApiFailureOperation.BudgetInstanceRead);
        }
    }

    private static async Task<IResult> ListAsync(
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        BudgetInstanceService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            if (request.Query.Keys.Any(key => key is not ("costCenterId" or "periodFrom" or "periodTo" or "skip" or "take" or "versionId")))
            {
                return SafeProblemResults.InvalidRequest();
            }
            var costCenterId = OptionalGuid(request, "costCenterId");
            var versionId = OptionalGuid(request, "versionId");
            var periodFrom = OptionalDate(request, "periodFrom");
            var periodTo = OptionalDate(request, "periodTo");
            var skip = OptionalInt(request, "skip") ?? 0;
            var take = OptionalInt(request, "take") ?? 25;
            var page = await service.ListAsync(costCenterId, periodFrom, periodTo, skip, take, cancellationToken, versionId);
            return Results.Ok(page);
        }
        catch (Exception exception)
        {
            return MapException(request, exception, ApiFailureOperation.BudgetInstanceList);
        }
    }

    private static async Task<IResult> ListCostCentersAsync(
        HttpRequest request, HttpResponse response, ICurrentActor actor,
        BudgetInstanceService service, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            if (request.Query.Keys.Any(key => key is not ("query" or "skip" or "take" or "validFrom"))
                || request.Query["query"].Count > 1)
            {
                return SafeProblemResults.InvalidRequest();
            }
            var validFrom = OptionalDate(request, "validFrom")
                ?? throw new DomainRuleViolationException("A start month is required.");
            return Results.Ok(await service.ListCostCentersAsync(request.Query["query"].FirstOrDefault(),
                validFrom, OptionalInt(request, "skip") ?? 0, OptionalInt(request, "take") ?? 25, cancellationToken));
        }
        catch (Exception exception)
        {
            return MapException(request, exception, ApiFailureOperation.BudgetInstanceList);
        }
    }

    private static async Task<IResult> GetCreatorCostCenterAsync(
        HttpRequest request, HttpResponse response, ICurrentActor actor,
        BudgetInstanceService service, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            if (request.Query.Keys.Any(key => key is not "validFrom")
                || request.Query["validFrom"].Count != 1)
            {
                return SafeProblemResults.InvalidRequest();
            }

            var validFrom = OptionalDate(request, "validFrom")
                ?? throw new DomainRuleViolationException("A start month is required.");
            return Results.Ok(await service.GetCreatorCostCenterAsync(validFrom, cancellationToken));
        }
        catch (Exception exception)
        {
            return MapException(request, exception, ApiFailureOperation.BudgetInstanceList);
        }
    }

    private static async Task<IResult> GetCreationOptionsAsync(
        HttpRequest request, HttpResponse response, ICurrentActor actor,
        BudgetInstanceService service, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            if (request.Query.Keys.Any(key => key is not "validFrom") || request.Query["validFrom"].Count != 1)
                return SafeProblemResults.InvalidRequest();
            var validFrom = OptionalDate(request, "validFrom")
                ?? throw new DomainRuleViolationException("A start month is required.");
            return Results.Ok(await service.GetCreationOptionsAsync(validFrom, cancellationToken));
        }
        catch (Exception exception)
        {
            return MapException(request, exception, ApiFailureOperation.BudgetInstanceList);
        }
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        BudgetInstanceService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            if (request.Query.Count != 0)
            {
                return SafeProblemResults.InvalidRequest();
            }

            return Results.Ok(await service.GetAsync(id, cancellationToken));
        }
        catch (Exception exception)
        {
            return MapException(request, exception, ApiFailureOperation.BudgetInstanceRead);
        }
    }

    private static async Task<IResult> CreateAsync(
        CreateBudgetInstanceCommand? command,
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        BudgetInstanceService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            if (command is null || request.Query.Count != 0)
            {
                return SafeProblemResults.InvalidRequest();
            }

            var key = RequireIdempotencyKey(request);
            var result = await service.CreateAsync(command, key, cancellationToken);
            return Results.Json(result.Instance, statusCode: result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        }
        catch (Exception exception)
        {
            return MapException(request, exception, ApiFailureOperation.BudgetInstanceWrite);
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateBudgetInstanceCommand? command,
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        BudgetInstanceService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            if (command is null || request.Query.Count != 0)
            {
                return SafeProblemResults.InvalidRequest();
            }

            var key = RequireIdempotencyKey(request);
            var result = await service.UpdateAsync(id, command, key, cancellationToken);
            return Results.Ok(result.Instance);
        }
        catch (Exception exception)
        {
            return MapException(request, exception, ApiFailureOperation.BudgetInstanceWrite);
        }
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, [FromBody] DeleteBudgetInstanceCommand? command,
        HttpRequest request, HttpResponse response, ICurrentActor actor,
        BudgetInstanceService service, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            if (command is null || request.Query.Count != 0) return SafeProblemResults.InvalidRequest();
            await service.DeleteAsync(id, command, RequireIdempotencyKey(request), cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception)
        {
            return MapException(request, exception, ApiFailureOperation.BudgetInstanceWrite);
        }
    }

    private static async Task<IResult> ApproveAsync(
        Guid id,
        ApproveBudgetInstanceCommand? command,
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        BudgetInstanceService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            if (command is null || request.Query.Count != 0)
            {
                return SafeProblemResults.InvalidRequest();
            }

            var key = RequireIdempotencyKey(request);
            var result = await service.ApproveAsync(id, command, key, cancellationToken);
            return Results.Ok(result.Instance);
        }
        catch (Exception exception)
        {
            return MapException(request, exception, ApiFailureOperation.BudgetInstanceWrite);
        }
    }

    private static IResult MapException(HttpRequest request, Exception exception, ApiFailureOperation operation) =>
        exception switch
        {
            ActorNotAuthenticatedException => SafeProblemResults.AuthenticationRequired(),
            ActorForbiddenException => SafeProblemResults.Forbidden(),
            BudgetInstanceNotFoundException => SafeProblemResults.BudgetInstanceNotFound(),
            BudgetNumberingNotInitializedException => MapNumberingUnresolved(request, operation),
            BudgetInstanceConcurrencyConflictException => SafeProblemResults.Conflict("version-conflict"),
            BudgetInstanceInCompanyVersionException => Results.Problem(statusCode: 409,
                detail: "Документ включён в версию бюджета. Сначала исключите его из редактируемых версий."),
            InvalidIdempotencyKeyException => SafeProblemResults.InvalidIdempotencyKey(),
            IdempotencyConflictException => SafeProblemResults.Conflict("idempotency-conflict"),
            BudgetDocumentAuthorityUnavailableException => MapAuthorityUnavailable(request, operation),
            DomainRuleViolationException violation => MapDomain(request, operation, violation),
            _ => throw exception,
        };

    private static IResult MapNumberingUnresolved(HttpRequest request, ApiFailureOperation operation)
    {
        // An unconfigured/legacy CFO must be initialized through the directory,
        // never by silently guessing a sequence during document creation.
        ApiFailureLog.InvalidRequest(request.HttpContext, operation);
        return SafeProblemResults.BudgetDraftValidation(DomainRuleViolationReason.BudgetNumberingUninitialized);
    }

    private static IResult MapAuthorityUnavailable(HttpRequest request, ApiFailureOperation operation)
    {
        ApiFailureLog.DependencyUnavailable(
            request.HttpContext,
            operation);
        return SafeProblemResults.BudgetDirectoryUnavailable();
    }

    private static IResult MapDomain(HttpRequest request, ApiFailureOperation operation, DomainRuleViolationException violation)
    {
        ApiFailureLog.InvalidRequest(request.HttpContext, operation);
        return violation.Reason is { } reason
            ? SafeProblemResults.BudgetDraftValidation(reason, violation.SubmittedRowIndex)
            : SafeProblemResults.DomainRuleViolation();
    }

    private static string RequireIdempotencyKey(HttpRequest request)
    {
        var values = request.Headers["Idempotency-Key"];
        if (values.Count != 1)
        {
            throw new InvalidIdempotencyKeyException("One idempotency key is required.");
        }

        return values[0] ?? string.Empty;
    }

    private static Guid? OptionalGuid(HttpRequest request, string name)
    {
        if (!request.Query.TryGetValue(name, out var values) || values.Count == 0)
        {
            return null;
        }

        if (values.Count != 1 || !Guid.TryParseExact(values[0], "D", out var parsed) || parsed == Guid.Empty)
        {
            throw new DomainRuleViolationException($"Query '{name}' is invalid.");
        }

        return parsed;
    }

    private static int? OptionalInt(HttpRequest request, string name)
    {
        if (!request.Query.TryGetValue(name, out var values) || values.Count == 0)
        {
            return null;
        }

        if (values.Count != 1
            || !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new DomainRuleViolationException($"Query '{name}' is invalid.");
        }

        return parsed;
    }

    private static DateOnly? OptionalDate(HttpRequest request, string name)
    {
        if (!request.Query.TryGetValue(name, out var values))
        {
            return null;
        }

        if (values.Count != 1 || values[0]?.Length != 10
            || !DateOnly.TryParseExact(values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            throw new DomainRuleViolationException($"Query '{name}' is invalid.");
        }

        return parsed;
    }
}
