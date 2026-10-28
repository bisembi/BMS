using System.Globalization;
using Bms.Api.Problems;
using Bms.Api.Diagnostics;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Application.ReferenceData;
using Bms.Api.Security;
using Bms.Domain.Common;

namespace Bms.Api.Endpoints;

public static class ReferenceDataEndpoints
{
    public static IEndpointRouteBuilder MapReferenceDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/reference-data").WithTags("Reference data");
        group.MapUnifiedCfoDirectory();
        group.MapGet("/payment-operation-options", async (ICurrentActor actor, IExpensePaymentOperationCatalog catalog,
            CancellationToken token) =>
        {
            if (!actor.IsAuthenticated || string.IsNullOrWhiteSpace(actor.ExternalId)) return Results.Unauthorized();
            try
            {
                ActorIdentity.RequireBusinessDataAccess(actor);
                if (!(await actor.ResolveBusinessDataReadScopeAsync(token)).HasAny) throw new ActorForbiddenException();
                return Results.Ok(await catalog.ReadAsync(token));
            }
            catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        });
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        group.MapGet("/organizations", ListOrganizationsAsync);
        group.MapPost("/organizations", CreateOrganizationAsync)
            .RequireCsrfToken()
            .WithName("CreateOrganization");
        group.MapPut("/organizations/{id:guid}", UpdateOrganizationAsync)
            .RequireCsrfToken()
            .WithName("UpdateOrganization");
        group.MapGet("/cost-centers", ListCostCentersAsync);
        group.MapGet("/cost-departments", async (ICurrentActor actor,
            IAccrualDepartmentCatalog catalog, CancellationToken token) =>
        {
            try
            {
                ActorIdentity.RequireBusinessDataAccess(actor);
                if (!(await actor.ResolveBusinessDataReadScopeAsync(token)).HasAny) throw new ActorForbiddenException();
                return Results.Ok(new { catalog = "cost-departments", source = "bms_web",
                    refreshedAtUtc = (DateTimeOffset?)null, items = await catalog.ListOnlyDepartmentsAsync(token) });
            }
            catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
            catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        });
        group.MapGet("/accrual-cost-centers", async (ICurrentActor actor,
            IAccrualDepartmentCatalog catalog, CancellationToken token) =>
        {
            if (!actor.IsAuthenticated || string.IsNullOrWhiteSpace(actor.ExternalId))
                return Results.Unauthorized();
            try
            {
                ActorIdentity.RequireBusinessDataAccess(actor);
                return Results.Ok(new { catalog = "accrual-cost-centers", source = "bms_web",
                    refreshedAtUtc = (DateTimeOffset?)null, items = await catalog.ListAsync(token) });
            }
            catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        });
        group.MapGet("/budget-owner-cost-center-assignments", ListBudgetOwnerCostCenterAssignmentsAsync);
        group.MapPost("/budget-owner-cost-center-assignments", CreateBudgetOwnerAssignmentAsync)
            .RequireCsrfToken()
            .WithName("CreateBudgetOwnerCostCenterAssignment");
        group.MapPut("/budget-owner-cost-center-assignments", UpdateBudgetOwnerAssignmentAsync)
            .RequireCsrfToken()
            .WithName("UpdateBudgetOwnerCostCenterAssignment");
        group.MapDelete("/budget-owner-cost-center-assignments", DeleteBudgetOwnerAssignmentAsync)
            .RequireCsrfToken()
            .WithName("DeleteBudgetOwnerCostCenterAssignment");
        group.MapPost("/authorization-directory/employees", CreateAuthorizationEmployeeAsync)
            .RequireCsrfToken()
            .WithName("CreateAuthorizationDirectoryEmployee");
        group.MapGet("/authorization-directory/employees", ListAuthorizationEmployeesAsync)
            .WithName("ListAuthorizationDirectoryEmployees");
        group.MapGet("/authorization-directory/employees/{id:guid}", GetAuthorizationEmployeeAsync)
            .WithName("GetAuthorizationDirectoryEmployee");
        group.MapGet("/authorization-directory/roles", ListAuthorizationRolesAsync)
            .WithName("ListAuthorizationDirectoryRoles");
        group.MapGet("/authorization-directory/active-ad-employees", ListActiveAdEmployeesAsync)
            .WithName("ListActiveAdEmployees");
        group.MapPost("/authorization-directory/employees/from-ad", CreateAuthorizationEmployeeFromAdAsync)
            .RequireCsrfToken()
            .WithName("CreateAuthorizationDirectoryEmployeeFromAd");
        group.MapPut("/authorization-directory/employees/{id:guid}/roles", UpdateAuthorizationEmployeeRoleAsync)
            .RequireCsrfToken()
            .WithName("UpdateAuthorizationDirectoryEmployeeRole");
        group.MapPut("/authorization-directory/employees/{id:guid}/actor-mapping",
                UpdateAuthorizationActorMappingAsync)
            .RequireCsrfToken()
            .WithName("UpdateAuthorizationDirectoryActorMapping");
        group.MapGet("/payment-bases", (ReferenceDataService service) => Fixed(service.ListPaymentBases));
        group.MapGet("/counterparty-residencies", (ReferenceDataService service) =>
            Fixed(service.ListCounterpartyResidencies));
        group.MapGet("/cash-flow-items", ListCashFlowItemsAsync);
        group.MapGet("/cash-flow-items/groups", ListCashFlowItemGroupsAsync);
        group.MapGet("/cash-flow-items/query", QueryCashFlowItemsAsync);
        group.MapGet("/cash-flow-items/resolve", ResolveCashFlowItemsAsync);
        group.MapGet("/expense-items", ListExpenseItemsAsync);
        group.MapPost("/expense-items", CreateExpenseItemAsync)
            .RequireCsrfToken()
            .WithName("CreateExpenseItem");
        group.MapGet("/counterparties", ListCounterpartiesAsync);
        group.MapGet("/contracts", ListContractsAsync);
        group.MapGet("/currencies", ListCurrenciesAsync);
        group.MapPost("/maintenance/{directoryId}", CreateMaintenanceItemAsync)
            .RequireCsrfToken()
            .WithName("CreateReferenceDataItem");
        group.MapPut("/maintenance/{directoryId}/{id:guid}", UpdateMaintenanceItemAsync)
            .RequireCsrfToken()
            .WithName("UpdateReferenceDataItem");
        group.MapDelete("/maintenance/{directoryId}/{id:guid}", DeleteMaintenanceItemAsync)
            .RequireCsrfToken()
            .WithName("DeleteReferenceDataItem");
        return endpoints;
    }

    private static async Task<IResult> CreateMaintenanceItemAsync(
        string directoryId,
        ReferenceDataMaintenanceCommand? command,
        HttpRequest request,
        HttpResponse response,
        ReferenceDataMaintenanceService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (command is null || request.Query.Count != 0)
                throw new DomainRuleViolationException("The reference-data command is invalid.");
            return Results.Ok(await service.CreateAsync(
                directoryId,
                command,
                RequireAuthorizationIdempotencyKey(request),
                cancellationToken));
        }
        catch (Exception exception) when (IsMaintenanceExpectedFailure(exception))
        {
            return MapMaintenanceException(response, exception);
        }
    }

    private static async Task<IResult> UpdateMaintenanceItemAsync(
        string directoryId,
        Guid id,
        ReferenceDataMaintenanceCommand? command,
        HttpRequest request,
        HttpResponse response,
        ReferenceDataMaintenanceService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (command is null || request.Query.Count != 0)
                throw new DomainRuleViolationException("The reference-data command is invalid.");
            return Results.Ok(await service.UpdateAsync(directoryId, id, command, cancellationToken));
        }
        catch (Exception exception) when (IsMaintenanceExpectedFailure(exception))
        {
            return MapMaintenanceException(response, exception);
        }
    }

    private static async Task<IResult> DeleteMaintenanceItemAsync(
        string directoryId,
        Guid id,
        HttpRequest request,
        HttpResponse response,
        ReferenceDataMaintenanceService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (request.Query.Count != 0)
                throw new DomainRuleViolationException("The reference-data command is invalid.");
            return Results.Ok(await service.DeleteAsync(directoryId, id, cancellationToken));
        }
        catch (Exception exception) when (IsMaintenanceExpectedFailure(exception))
        {
            return MapMaintenanceException(response, exception);
        }
    }

    private static bool IsMaintenanceExpectedFailure(Exception exception) => exception is
        ActorNotAuthenticatedException
        or ActorForbiddenException
        or ReferenceDataMaintenanceNotFoundException
        or ReferenceDataMaintenanceConflictException
        or ReferenceDataMaintenanceUnavailableException
        or InvalidIdempotencyKeyException
        or IdempotencyConflictException
        or DomainRuleViolationException;

    private static IResult MapMaintenanceException(HttpResponse response, Exception exception) => exception switch
    {
        ActorNotAuthenticatedException => SafeProblemResults.AuthenticationRequired(),
        ActorForbiddenException => SafeProblemResults.Forbidden(),
        ReferenceDataMaintenanceNotFoundException => SafeProblemResults.ReferenceDataNotFound(),
        ReferenceDataMaintenanceConflictException => SafeProblemResults.ReferenceDataConflict(),
        InvalidIdempotencyKeyException => SafeProblemResults.InvalidIdempotencyKey(),
        IdempotencyConflictException => SafeProblemResults.Conflict("idempotency-conflict"),
        DomainRuleViolationException => SafeProblemResults.InvalidRequest(),
        ReferenceDataMaintenanceUnavailableException => SafeProblemResults.ReferenceDataUnavailable(),
        _ => SafeProblemResults.InvalidRequest(),
    };

    private static async Task<IResult> CreateAuthorizationEmployeeAsync(
        CreateAuthorizationDirectoryEmployeeCommand? command,
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        AuthorizationDirectoryService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireReferenceDataManagement(actor);
            if (command is null || request.Query.Count != 0)
            {
                throw new DomainRuleViolationException("The authorization-directory command is invalid.");
            }

            var idempotencyKey = RequireAuthorizationIdempotencyKey(request);
            return Results.Ok(await service.CreateEmployeeAsync(
                command, idempotencyKey, cancellationToken));
        }
        catch (Exception exception) when (IsAuthorizationExpectedFailure(exception))
        {
            return MapAuthorizationException(request, exception, ApiFailureOperation.AuthorizationDirectoryWrite);
        }
    }

    private static async Task<IResult> ListAuthorizationEmployeesAsync(
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        AuthorizationDirectoryService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            var page = ParseAuthorizationPage(request);
            return Results.Ok(await service.ListEmployeesAsync(
                page.Query, page.Skip, page.Take, cancellationToken));
        }
        catch (Exception exception) when (IsAuthorizationExpectedFailure(exception))
        {
            return MapAuthorizationException(request, exception, ApiFailureOperation.AuthorizationDirectoryList);
        }
    }

    private static async Task<IResult> GetAuthorizationEmployeeAsync(
        Guid id,
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        AuthorizationDirectoryService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireBusinessDataAccess(actor);
            if (request.Query.Count != 0)
            {
                throw new DomainRuleViolationException("The authorization-directory query is invalid.");
            }

            return Results.Ok(await service.GetAsync(id, cancellationToken));
        }
        catch (Exception exception) when (IsAuthorizationExpectedFailure(exception))
        {
            return MapAuthorizationException(request, exception, ApiFailureOperation.AuthorizationDirectoryRead);
        }
    }

    private static async Task<IResult> UpdateAuthorizationActorMappingAsync(
        Guid id,
        UpdateAuthorizationDirectoryActorCommand? command,
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        AuthorizationDirectoryService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireReferenceDataManagement(actor);
            if (command is null || request.Query.Count != 0)
            {
                throw new DomainRuleViolationException("The authorization-directory command is invalid.");
            }

            var idempotencyKey = RequireAuthorizationIdempotencyKey(request);
            return Results.Ok(await service.UpdateActorMappingAsync(
                id, command, idempotencyKey, cancellationToken));
        }
        catch (Exception exception) when (IsAuthorizationExpectedFailure(exception))
        {
            return MapAuthorizationException(request, exception, ApiFailureOperation.AuthorizationDirectoryWrite);
        }
    }

    private static async Task<IResult> ListAuthorizationRolesAsync(
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        AuthorizationDirectoryRoleService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireReferenceDataManagement(actor);
            if (request.Query.Count != 0)
            {
                throw new DomainRuleViolationException("The authorization-directory query is invalid.");
            }
            return Results.Ok(await service.ListRolesAsync(cancellationToken));
        }
        catch (Exception exception) when (IsAuthorizationExpectedFailure(exception))
        {
            return MapAuthorizationException(request, exception, ApiFailureOperation.AuthorizationDirectoryList);
        }
    }

    private static async Task<IResult> ListActiveAdEmployeesAsync(
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        AuthorizationDirectoryRoleService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireReferenceDataManagement(actor);
            var page = ParseAuthorizationPage(request);
            return Results.Ok(await service.SearchActiveDirectoryAsync(
                page.Query, page.Skip, page.Take, cancellationToken));
        }
        catch (Exception exception) when (IsAuthorizationExpectedFailure(exception))
        {
            return MapAuthorizationException(request, exception, ApiFailureOperation.AuthorizationDirectoryList);
        }
    }

    private static async Task<IResult> CreateAuthorizationEmployeeFromAdAsync(
        CreateAuthorizationDirectoryEmployeeFromAdCommand? command,
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        AuthorizationDirectoryRoleService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireReferenceDataManagement(actor);
            if (command is null || request.Query.Count != 0)
            {
                throw new DomainRuleViolationException("The authorization-directory command is invalid.");
            }
            return Results.Ok(await service.CreateFromActiveDirectoryAsync(
                command, RequireAuthorizationIdempotencyKey(request), cancellationToken));
        }
        catch (Exception exception) when (IsAuthorizationExpectedFailure(exception))
        {
            return MapAuthorizationException(request, exception, ApiFailureOperation.AuthorizationDirectoryWrite);
        }
    }

    private static async Task<IResult> UpdateAuthorizationEmployeeRoleAsync(
        Guid id,
        AssignAuthorizationDirectoryRoleCommand? command,
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        AuthorizationDirectoryRoleService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireReferenceDataManagement(actor);
            if (command is null || request.Query.Count != 0)
            {
                throw new DomainRuleViolationException("The authorization-directory command is invalid.");
            }
            return Results.Ok(await service.AssignRoleAsync(
                id, command, RequireAuthorizationIdempotencyKey(request), cancellationToken));
        }
        catch (Exception exception) when (IsAuthorizationExpectedFailure(exception))
        {
            return MapAuthorizationException(request, exception, ApiFailureOperation.AuthorizationDirectoryWrite);
        }
    }

    private static bool IsAuthorizationExpectedFailure(Exception exception) => exception is
        ActorNotAuthenticatedException
        or ActorForbiddenException
        or AuthorizationDirectoryEmployeeNotFoundException
        or AuthorizationDirectoryAdEmployeeNotFoundException
        or AuthorizationDirectoryCodeConflictException
        or AuthorizationDirectoryActorConflictException
        or AuthorizationDirectoryStateConflictException
        or AuthorizationDirectoryUnavailableException
        or BudgetDocumentAuthorityUnavailableException
        or InvalidIdempotencyKeyException
        or IdempotencyConflictException
        or DomainRuleViolationException;

    private static IResult MapAuthorizationException(
        HttpRequest request,
        Exception exception,
        ApiFailureOperation operation) => exception switch
        {
            ActorNotAuthenticatedException => SafeProblemResults.AuthenticationRequired(),
            ActorForbiddenException => SafeProblemResults.Forbidden(),
            AuthorizationDirectoryEmployeeNotFoundException => SafeProblemResults.AuthorizationDirectoryNotFound(),
            AuthorizationDirectoryAdEmployeeNotFoundException => SafeProblemResults.AuthorizationDirectoryNotFound(),
            AuthorizationDirectoryCodeConflictException => SafeProblemResults.EmployeeCodeConflict(),
            AuthorizationDirectoryActorConflictException => SafeProblemResults.AuthorizationDirectoryActorConflict(),
            AuthorizationDirectoryStateConflictException => SafeProblemResults.Conflict("version-conflict"),
            BudgetDocumentAuthorityUnavailableException => AuthorizationUnavailable(request, operation),
            InvalidIdempotencyKeyException => SafeProblemResults.InvalidIdempotencyKey(),
            IdempotencyConflictException => SafeProblemResults.Conflict("idempotency-conflict"),
            AuthorizationDirectoryUnavailableException => AuthorizationUnavailable(request, operation),
            DomainRuleViolationException => AuthorizationInvalid(request, operation),
            _ => throw new InvalidOperationException("An unsupported authorization-directory failure was mapped."),
        };

    private static IResult AuthorizationUnavailable(HttpRequest request, ApiFailureOperation operation)
    {
        ApiFailureLog.DependencyUnavailable(request.HttpContext, operation);
        return SafeProblemResults.AuthorizationDirectoryUnavailable();
    }

    private static IResult AuthorizationInvalid(HttpRequest request, ApiFailureOperation operation)
    {
        ApiFailureLog.InvalidRequest(request.HttpContext, operation);
        return SafeProblemResults.InvalidRequest();
    }

    private static IResult ExpenseItemDirectoryUnavailable(
        HttpRequest request,
        ApiFailureOperation operation)
    {
        ApiFailureLog.DependencyUnavailable(request.HttpContext, operation);
        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            detail: "The expense-item directory is temporarily unavailable.");
    }

    private static (string? Query, int Skip, int Take) ParseAuthorizationPage(HttpRequest request)
    {
        if (request.Query.Keys.Any(key => key is not ("query" or "skip" or "take")))
        {
            throw new DomainRuleViolationException("The authorization-directory query is invalid.");
        }

        var query = request.Query.TryGetValue("query", out var values)
            ? values.Count == 1 ? values[0]?.Trim() : throw new DomainRuleViolationException(
                "One authorization-directory query is required.")
            : null;
        var skip = ParseAuthorizationInteger(request, "skip") ?? 0;
        var take = ParseAuthorizationInteger(request, "take") ?? 25;
        if (skip < 0 || take is < 1 or > 100 || query?.Length > 200
            || query?.Any(char.IsControl) == true)
        {
            throw new DomainRuleViolationException("The authorization-directory page is invalid.");
        }

        return (query, skip, take);
    }

    private static int? ParseAuthorizationInteger(HttpRequest request, string name)
    {
        if (!request.Query.TryGetValue(name, out var values)) return null;
        if (values.Count != 1
            || !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            throw new DomainRuleViolationException("The authorization-directory page is invalid.");
        }

        return value;
    }

    private static string RequireAuthorizationIdempotencyKey(HttpRequest request)
    {
        var values = request.Headers["Idempotency-Key"];
        var value = values.Count == 1 ? values[0] : null;
        if (value is null || string.IsNullOrWhiteSpace(value) || value.Length > 127)
        {
            throw new InvalidIdempotencyKeyException("One idempotency key is required.");
        }

        return value;
    }

    internal static async Task<IResult> ListBudgetOwnerCostCenterAssignmentsAsync(
        ICurrentActor actor,
        BudgetOwnerCostCenterAssignmentDirectoryService service,
        CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated || string.IsNullOrWhiteSpace(actor.ExternalId))
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.ListAsync(cancellationToken));
        }
        catch (ActorNotAuthenticatedException)
        {
            return Results.Unauthorized();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
    }

    private static async Task<IResult> CreateBudgetOwnerAssignmentAsync(
        BudgetOwnerCostCenterAssignmentWriteCommand? command,
        HttpRequest request,
        HttpResponse response,
        BudgetOwnerCostCenterAssignmentMaintenanceService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (command is null || request.Query.Count != 0) throw new DomainRuleViolationException("The assignment command is invalid.");
            return Results.Ok(await service.CreateAsync(command, RequireAuthorizationIdempotencyKey(request), cancellationToken));
        }
        catch (Exception exception) when (IsBudgetOwnerWriteExpectedFailure(exception))
        { return MapBudgetOwnerWriteException(exception); }
    }

    private static async Task<IResult> UpdateBudgetOwnerAssignmentAsync(
        string? employeeCode,
        string? costCenterCode,
        BudgetOwnerCostCenterAssignmentWriteCommand? command,
        HttpRequest request,
        HttpResponse response,
        BudgetOwnerCostCenterAssignmentMaintenanceService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (command is null || request.Query.Keys.Any(key => !string.Equals(key, "employeeCode", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, "costCenterCode", StringComparison.OrdinalIgnoreCase)))
                throw new DomainRuleViolationException("The assignment command is invalid.");
            return Results.Ok(await service.UpdateAsync(employeeCode ?? string.Empty, costCenterCode ?? string.Empty, command, cancellationToken));
        }
        catch (Exception exception) when (IsBudgetOwnerWriteExpectedFailure(exception))
        { return MapBudgetOwnerWriteException(exception); }
    }

    private static async Task<IResult> DeleteBudgetOwnerAssignmentAsync(
        string? employeeCode,
        string? costCenterCode,
        HttpRequest request,
        HttpResponse response,
        BudgetOwnerCostCenterAssignmentMaintenanceService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            if (request.Query.Keys.Any(key => !string.Equals(key, "employeeCode", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, "costCenterCode", StringComparison.OrdinalIgnoreCase)))
                throw new DomainRuleViolationException("The assignment command is invalid.");
            return Results.Ok(await service.DeleteAsync(employeeCode ?? string.Empty, costCenterCode ?? string.Empty, cancellationToken));
        }
        catch (Exception exception) when (IsBudgetOwnerWriteExpectedFailure(exception))
        { return MapBudgetOwnerWriteException(exception); }
    }

    private static bool IsBudgetOwnerWriteExpectedFailure(Exception exception) => exception is
        ActorNotAuthenticatedException or ActorForbiddenException
        or BudgetOwnerCostCenterAssignmentNotFoundException
        or BudgetOwnerCostCenterAssignmentConflictException
        or BudgetDocumentAuthorityUnavailableException
        or InvalidIdempotencyKeyException or IdempotencyConflictException
        or DomainRuleViolationException;

    private static IResult MapBudgetOwnerWriteException(Exception exception) => exception switch
    {
        ActorNotAuthenticatedException => SafeProblemResults.AuthenticationRequired(),
        ActorForbiddenException => SafeProblemResults.Forbidden(),
        BudgetOwnerCostCenterAssignmentNotFoundException => SafeProblemResults.BudgetOwnerAssignmentNotFound(),
        BudgetOwnerCostCenterAssignmentConflictException => SafeProblemResults.BudgetOwnerAssignmentConflict(),
        BudgetDocumentAuthorityUnavailableException => SafeProblemResults.ReferenceDataUnavailable(),
        InvalidIdempotencyKeyException => SafeProblemResults.InvalidIdempotencyKey(),
        IdempotencyConflictException => SafeProblemResults.Conflict("idempotency-conflict"),
        DomainRuleViolationException => SafeProblemResults.InvalidRequest(),
        _ => SafeProblemResults.InvalidRequest(),
    };

    private static async Task<IResult> ListOrganizationsAsync(
        bool? includeInactive,
        ICurrentActor actor,
        ReferenceDataService service,
        CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated || string.IsNullOrWhiteSpace(actor.ExternalId))
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.ListOrganizationsAsync(
                includeInactive ?? false,
                cancellationToken));
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
    }

    private static async Task<IResult> UpdateOrganizationAsync(
        Guid id,
        UpdateOrganizationCommand? command,
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        OrganizationDirectoryService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireReferenceDataManagement(actor);
            if (command is null || request.Query.Count != 0)
            {
                throw new DomainRuleViolationException("The organization command is invalid.");
            }

            return Results.Ok(await service.UpdateAsync(id, command, cancellationToken));
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (OrganizationDirectoryNotFoundException)
        {
            return SafeProblemResults.OrganizationNotFound();
        }
        catch (OrganizationCodeConflictException)
        {
            return SafeProblemResults.OrganizationCodeConflict();
        }
        catch (DomainRuleViolationException)
        {
            ApiFailureLog.InvalidRequest(
                response.HttpContext,
                ApiFailureOperation.ReferenceDataOrganizationWrite);
            return SafeProblemResults.InvalidRequest();
        }
        catch (OrganizationDirectoryUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.ReferenceDataOrganizationWrite);
            return SafeProblemResults.OrganizationDirectoryUnavailable();
        }
    }

    private static async Task<IResult> CreateOrganizationAsync(
        CreateOrganizationCommand? command,
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        OrganizationDirectoryService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireReferenceDataManagement(actor);
            if (command is null || request.Query.Count != 0)
            {
                throw new DomainRuleViolationException("The organization command is invalid.");
            }

            var idempotencyKey = RequireAuthorizationIdempotencyKey(request);
            return Results.Ok(await service.CreateAsync(command, idempotencyKey, cancellationToken));
        }
        catch (ActorNotAuthenticatedException)
        {
            return SafeProblemResults.AuthenticationRequired();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (OrganizationCodeConflictException)
        {
            return SafeProblemResults.OrganizationCodeConflict();
        }
        catch (InvalidIdempotencyKeyException)
        {
            return SafeProblemResults.InvalidIdempotencyKey();
        }
        catch (IdempotencyConflictException)
        {
            return SafeProblemResults.Conflict("idempotency-conflict");
        }
        catch (DomainRuleViolationException)
        {
            ApiFailureLog.InvalidRequest(
                response.HttpContext,
                ApiFailureOperation.ReferenceDataOrganizationWrite);
            return SafeProblemResults.InvalidRequest();
        }
        catch (OrganizationDirectoryUnavailableException)
        {
            ApiFailureLog.DependencyUnavailable(
                response.HttpContext,
                ApiFailureOperation.ReferenceDataOrganizationWrite);
            return SafeProblemResults.OrganizationDirectoryUnavailable();
        }
    }

    private static async Task<IResult> ListCostCentersAsync(
        bool? includeInactive,
        bool? includeGroups,
        DateOnly? asOf,
        ICurrentActor actor,
        ReferenceDataService service,
        CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated || string.IsNullOrWhiteSpace(actor.ExternalId))
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.ListCostCentersAsync(
                includeInactive ?? false,
                includeGroups ?? false,
                asOf,
                cancellationToken));
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
    }

    internal static async Task<IResult> ListCashFlowItemsAsync(
        bool? includeInactive,
        bool? includeGroups,
        ICurrentActor actor,
        ReferenceDataService service,
        CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated || string.IsNullOrWhiteSpace(actor.ExternalId))
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.ListCashFlowItemsAsync(
                includeInactive ?? false,
                includeGroups ?? false,
                cancellationToken));
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
    }

    internal static async Task<IResult> ListCashFlowItemGroupsAsync(
        ICurrentActor actor,
        ReferenceDataService service,
        CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated || string.IsNullOrWhiteSpace(actor.ExternalId))
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.ListCashFlowItemGroupsAsync(cancellationToken));
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
    }

    /// One page of the cash-flow-item catalogue, with the size of the whole
    /// filter it came from.
    ///
    /// The window and the term are judged by the service, not here, so there
    /// is one place that decides what a valid request is. This method maps the
    /// refusal onto the wire: an ArgumentException - which includes the
    /// out-of-range window - becomes the fixed 400.
    internal static async Task<IResult> QueryCashFlowItemsAsync(
        string? query,
        int? skip,
        int? take,
        bool? includeInactive,
        bool? includeGroups,
        ICurrentActor actor,
        ReferenceDataService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated || string.IsNullOrWhiteSpace(actor.ExternalId))
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.QueryCashFlowItemsAsync(
                query,
                skip ?? 0,
                take ?? ReferenceDataService.DefaultWindowSize,
                includeInactive ?? false,
                includeGroups ?? false,
                cancellationToken));
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (ArgumentException)
        {
            ApiFailureLog.InvalidRequest(
                response.HttpContext,
                ApiFailureOperation.ReferenceDataCashFlowItemQuery);
            return SafeProblemResults.InvalidRequest();
        }
    }

    /// Resolves the identifiers already carried by a form or a page without
    /// making the browser download the whole catalogue. The service owns both
    /// parsing and the batch limit so every caller is judged in one place.
    internal static async Task<IResult> ResolveCashFlowItemsAsync(
        string[]? ids,
        ICurrentActor actor,
        ReferenceDataService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated || string.IsNullOrWhiteSpace(actor.ExternalId))
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.ResolveCashFlowItemsAsync(ids, cancellationToken));
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (ArgumentException)
        {
            ApiFailureLog.InvalidRequest(
                response.HttpContext,
                ApiFailureOperation.ReferenceDataCashFlowItemResolve);
            return SafeProblemResults.InvalidRequest();
        }
    }

    internal static async Task<IResult> ListCounterpartiesAsync(
        bool? includeInactive,
        ICurrentActor actor,
        ReferenceDataService service,
        CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated || string.IsNullOrWhiteSpace(actor.ExternalId))
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.ListCounterpartiesAsync(
                includeInactive ?? false,
                cancellationToken));
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
    }

    internal static async Task<IResult> ListExpenseItemsAsync(
        ICurrentActor actor,
        ReferenceDataService service,
        CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated || string.IsNullOrWhiteSpace(actor.ExternalId))
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.ListExpenseItemsAsync(cancellationToken));
        }
        catch (ActorNotAuthenticatedException)
        {
            return Results.Unauthorized();
        }
        catch (ActorForbiddenException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Business data access is forbidden.");
        }
    }

    private static async Task<IResult> CreateExpenseItemAsync(
        CreateExpenseItemCommand? command,
        HttpRequest request,
        HttpResponse response,
        ICurrentActor actor,
        ExpenseItemDirectoryService service,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        try
        {
            ActorIdentity.RequireReferenceDataManagement(actor);
            if (command is null || request.Query.Count != 0)
            {
                throw new DomainRuleViolationException("The expense-item command is invalid.");
            }

            var idempotencyKey = RequireAuthorizationIdempotencyKey(request);
            return Results.Ok(await service.CreateAsync(
                command,
                idempotencyKey,
                cancellationToken));
        }
        catch (Exception exception) when (IsExpenseItemExpectedFailure(exception))
        {
            return MapExpenseItemException(request, exception);
        }
    }

    private static bool IsExpenseItemExpectedFailure(Exception exception) => exception is
        ActorNotAuthenticatedException
        or ActorForbiddenException
        or ExpenseItemCodeConflictException
        or ExpenseItemDirectoryUnavailableException
        or InvalidIdempotencyKeyException
        or IdempotencyConflictException
        or DomainRuleViolationException;

    private static IResult MapExpenseItemException(
        HttpRequest request,
        Exception exception) => exception switch
        {
            ActorNotAuthenticatedException => SafeProblemResults.AuthenticationRequired(),
            ActorForbiddenException => SafeProblemResults.Forbidden(),
            ExpenseItemCodeConflictException => SafeProblemResults.ExpenseItemCodeConflict(),
            ExpenseItemDirectoryUnavailableException => ExpenseItemDirectoryUnavailable(
                request,
                ApiFailureOperation.ReferenceDataExpenseItemWrite),
            InvalidIdempotencyKeyException => SafeProblemResults.InvalidIdempotencyKey(),
            IdempotencyConflictException => SafeProblemResults.Conflict("idempotency-conflict"),
            DomainRuleViolationException => AuthorizationInvalid(
                request,
                ApiFailureOperation.ReferenceDataExpenseItemWrite),
            _ => throw new InvalidOperationException("An unsupported expense-item failure was mapped."),
        };

    internal static async Task<IResult> ListContractsAsync(
        Guid? counterpartyId,
        Guid? organizationId,
        bool? includeInactive,
        ICurrentActor actor,
        ReferenceDataService service,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated || string.IsNullOrWhiteSpace(actor.ExternalId))
        {
            return Results.Unauthorized();
        }
        if (!counterpartyId.HasValue || counterpartyId.Value == Guid.Empty ||
            !organizationId.HasValue || organizationId.Value == Guid.Empty)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: "Both counterpartyId and organizationId are required.");
        }

        try
        {
            return Results.Ok(await service.ListContractsAsync(
                counterpartyId.Value,
                organizationId.Value,
                includeInactive ?? false,
                cancellationToken));
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
        catch (ArgumentException)
        {
            ApiFailureLog.InvalidRequest(
                response.HttpContext,
                ApiFailureOperation.ReferenceDataContracts);
            return SafeProblemResults.InvalidRequest();
        }
    }

    internal static async Task<IResult> ListCurrenciesAsync(
        bool? includeInactive,
        ICurrentActor actor,
        ReferenceDataService service,
        CancellationToken cancellationToken)
    {
        if (!actor.IsAuthenticated || string.IsNullOrWhiteSpace(actor.ExternalId))
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.ListCurrenciesAsync(
                includeInactive ?? false,
                cancellationToken));
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
    }

    private static IResult Fixed(Func<ReferenceCodeCatalogDto> catalogFactory)
    {
        try
        {
            return Results.Ok(catalogFactory());
        }
        catch (ActorNotAuthenticatedException)
        {
            return Results.Unauthorized();
        }
        catch (ActorForbiddenException)
        {
            return SafeProblemResults.Forbidden();
        }
    }

}
