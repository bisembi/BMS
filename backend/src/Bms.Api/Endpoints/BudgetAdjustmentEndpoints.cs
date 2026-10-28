using Bms.Api.Problems;
using Bms.Api.Security;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Bms.Infrastructure.Persistence;

namespace Bms.Api.Endpoints;

public static class BudgetAdjustmentEndpoints
{
    public static RouteGroupBuilder MapBudgetAdjustments(this RouteGroupBuilder group)
    {
        group.MapGet("/adjustments",(Guid? baseBudgetId,int? skip,int? take,HttpContext c,BudgetAdjustmentService s,CancellationToken t)=>
            HandleAsync(c,t,async()=>Results.Ok(await s.ListAsync(baseBudgetId,skip??0,take??25,t))));
        group.MapGet("/adjustments/basis/{baseId:guid}",(Guid baseId,HttpContext c,BudgetAdjustmentService s,CancellationToken t)=>
            HandleAsync(c,t,async()=>Results.Ok(await s.BasisAsync(baseId,t))));
        group.MapGet("/adjustments/{id:guid}",(Guid id,HttpContext c,BudgetAdjustmentService s,CancellationToken t)=>
            HandleAsync(c,t,async()=>Results.Ok(await s.GetAsync(id,t))));
        group.MapPost("/adjustments",(SaveBudgetAdjustmentCommand command,HttpContext c,BudgetAdjustmentService s,CancellationToken t)=>
            HandleAsync(c,t,async()=>Results.Ok(await s.SaveAsync(null,command,Key(c),t)))).RequireCsrfToken();
        group.MapPut("/adjustments/{id:guid}",(Guid id,SaveBudgetAdjustmentCommand command,HttpContext c,BudgetAdjustmentService s,CancellationToken t)=>
            HandleAsync(c,t,async()=>Results.Ok(await s.SaveAsync(id,command,Key(c),t)))).RequireCsrfToken();
        group.MapPost("/adjustments/{id:guid}/{action}",(Guid id,string action,BudgetAdjustmentActionCommand command,HttpContext c,BudgetAdjustmentService s,CancellationToken t)=>
            HandleAsync(c,t,async()=>Results.Ok(await s.ActAsync(id,action,command,Key(c),t)))).RequireCsrfToken();
        return group;
    }
    private static string Key(HttpContext c)=>c.Request.Headers.TryGetValue("Idempotency-Key",out var key)&&key.Count==1
        ?key[0]??throw new InvalidIdempotencyKeyException("Missing key."):throw new InvalidIdempotencyKeyException("Missing key.");
    private static async Task<IResult> HandleAsync(HttpContext context,CancellationToken token,Func<Task<IResult>> action)
    {
        context.Response.Headers.CacheControl="no-store";
        try{var result=await action();token.ThrowIfCancellationRequested();return result;}
        catch(ActorNotAuthenticatedException){return SafeProblemResults.AuthenticationRequired();}
        catch(ActorForbiddenException){return SafeProblemResults.Forbidden();}
        catch(BudgetInstanceNotFoundException){return Results.NotFound();}
        catch(BudgetInstanceConcurrencyConflictException){return Results.Conflict(new{message="Документ изменён. Обновите его перед повторным действием."});}
        catch(IdempotencyConflictException){return Results.Conflict(new{message="Этот запрос уже использован для другого действия."});}
        catch(BudgetAdjustmentRuleException e){return Results.Json(new{message=e.Message},statusCode:422);}
        catch(CashFlowActualsNotAvailableException){return Results.Json(new{message="Актуальный факт 1С недоступен или устарел. Проверьте журнал обмена и повторите действие после успешного обновления."},statusCode:422);}
        catch(CashFlowActualsPeriodUnavailableException){return Results.Json(new{message="Нет подтверждённого факта 1С за выбранный год. Перенос не выполнен."},statusCode:422);}
        catch(CashFlowActualsMappingUnavailableException){return Results.Json(new{message="Не удалось сопоставить статьи с фактом 1С. Требуется проверка справочника ДДС."},statusCode:422);}
        catch(InvalidIdempotencyKeyException){return SafeProblemResults.InvalidRequest();}
        catch(DomainRuleViolationException){return Results.Json(new{message="Проверьте статьи, периоды и точность сумм: не более двух знаков после запятой."},statusCode:422);}
        catch(ArgumentException){return SafeProblemResults.InvalidRequest();}
        catch(OperationCanceledException){throw;}
        catch(Exception){return Results.Problem(statusCode:503,title:"Корректировки временно недоступны",detail:"Операция не подтверждена. Сохраните введённые данные и повторите проверку состояния документа.");}
    }
}
