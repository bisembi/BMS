using Bms.Application.Abstractions;

namespace Bms.Api.Security;

public sealed class BudgetExecutionHttpIntent343(IHttpContextAccessor accessor) : IBudgetExecutionTransactionIntent343
{
    public bool IsReadOnly => accessor.HttpContext?.Request is { } request
        && (HttpMethods.IsGet(request.Method)||HttpMethods.IsHead(request.Method)
            ||(HttpMethods.IsPost(request.Method)&&request.Path.Equals(new PathString("/api/v1/budget/expense-draft/preview"))));
}
