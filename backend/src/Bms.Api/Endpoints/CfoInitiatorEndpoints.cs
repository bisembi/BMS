using Bms.Api.Problems;
using Bms.Api.Security;
using Bms.Application.Authorization;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;

namespace Bms.Api.Endpoints;

public static class CfoInitiatorEndpoints
{
    public static RouteGroupBuilder MapUnifiedCfoDirectory(this RouteGroupBuilder group)
    {
        group.MapPost("/cost-centers/editor",
            async (CreateCostCenterEditorCommand command, HttpRequest request, CostCenterEditorService service, CancellationToken token) =>
                await Read(() => service.CreateAsync(command, Key(request), token))).RequireCsrfToken();
        group.MapGet("/cost-centers/{id:guid}/editor",
            async (Guid id, CostCenterEditorService service, CancellationToken token) => await Read(() => service.GetAsync(id, token)));
        group.MapPut("/cost-centers/{id:guid}/editor",
            async (Guid id, UpdateCostCenterEditorCommand command, HttpRequest request, CostCenterEditorService service, CancellationToken token) =>
                await Read(() => service.WriteAsync(id, command, Key(request), token))).RequireCsrfToken();
        group.MapGet("/authorization-directory/cfo-assignments",
            async (CfoDirectoryService service, CancellationToken token) => await Read(() => service.ListAsync(token)));
        group.MapGet("/authorization-directory/employees/{id:guid}/cost-centers",
            async (Guid id, CfoDirectoryService service, CancellationToken token) => await Read(() => service.GetAsync(id, token)));
        group.MapPut("/authorization-directory/employees/{id:guid}/cost-centers",
            async (Guid id, WriteEmployeeCfoCommand command, HttpRequest request, CfoDirectoryService service, CancellationToken token) =>
                await Read(() => service.WriteAsync(id, command, Key(request), token))).RequireCsrfToken();
        return group;
    }

    public static RouteGroupBuilder MapBudgetApprovalWorkflow(this RouteGroupBuilder group)
    {
        group.MapGet("/instances/{id:guid}/approval",
            async (Guid id, BudgetApprovalWorkflowService service, CancellationToken token) => await Read(() => service.GetAsync(id, token)));
        group.MapPost("/instances/{id:guid}/submit",
            async (Guid id, SubmitBudgetApprovalCommand command, HttpRequest request, BudgetApprovalWorkflowService service, CancellationToken token) =>
                await Read(() => service.SubmitAsync(id, command, Key(request), token))).RequireCsrfToken();
        group.MapPost("/instances/{id:guid}/approval-decisions",
            async (Guid id, DecideBudgetApprovalCommand command, HttpRequest request, BudgetApprovalWorkflowService service, CancellationToken token) =>
                await Read(() => service.DecideAsync(id, command, Key(request), token))).RequireCsrfToken();
        return group;
    }

    private static string Key(HttpRequest request) =>
        request.Headers["Idempotency-Key"].Count == 1 ? request.Headers["Idempotency-Key"][0] ?? ""
            : throw new InvalidIdempotencyKeyException("One idempotency key is required.");

    private static async Task<IResult> Read<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (ActorNotAuthenticatedException) { return SafeProblemResults.AuthenticationRequired(); }
        catch (ActorForbiddenException) { return SafeProblemResults.Forbidden(); }
        catch (BudgetInstanceNotFoundException) { return SafeProblemResults.BudgetInstanceNotFound(); }
        catch (AuthorizationDirectoryEmployeeNotFoundException) { return Results.NotFound(); }
        catch (AuthorizationDirectoryStateConflictException) { return SafeProblemResults.Conflict("version-conflict"); }
        catch (BudgetApprovalBaselineChangedException) { return SafeProblemResults.BudgetBaselineChangedConflict(); }
        catch (BudgetInstanceConcurrencyConflictException) { return SafeProblemResults.Conflict("version-conflict"); }
        catch (ExpenseRequestVersionConflictException) { return SafeProblemResults.Conflict("version-conflict"); }
        catch (IdempotencyConflictException) { return SafeProblemResults.Conflict("idempotency-conflict"); }
        catch (InvalidIdempotencyKeyException) { return SafeProblemResults.InvalidIdempotencyKey(); }
        catch (DomainRuleViolationException) { return SafeProblemResults.DomainRuleViolation(); }
        catch (AuthorizationDirectoryUnavailableException) { return SafeProblemResults.BudgetDirectoryUnavailable(); }
        catch (BudgetDocumentAuthorityUnavailableException) { return SafeProblemResults.BudgetDirectoryUnavailable(); }
    }
}
