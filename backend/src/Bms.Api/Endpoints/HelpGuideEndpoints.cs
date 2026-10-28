using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Application.Authorization;

namespace Bms.Api.Endpoints;

public static class HelpGuideEndpoints
{
    public const string ExpenseRequestGuidePath = "/api/v1/help/expense-requests/guide";
    public const string BudgetGuidePath = "/api/v1/help/budget-instances/guide";
    public const string LegacyExpenseRequestGuidePath = "/assets/bms-expense-requests-guide-20261019-D5GCpNBY.pdf";
    public const string LegacyBudgetGuidePath = "/assets/bms-budget-filling-guide-20261019.pdf";

    public static IEndpointRouteBuilder MapHelpGuideEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The existing /api/v1 pipeline challenges Windows authentication,
        // resolves the active employee and validates the assigned BMS role.
        endpoints.MapMethods(ExpenseRequestGuidePath, ["GET", "HEAD"], ReadExpenseRequestGuide)
            .WithName("GetExpenseRequestGuide");
        endpoints.MapMethods(BudgetGuidePath, ["GET", "HEAD"], ReadBudgetGuide)
            .WithName("GetBudgetFillingGuide");
        // Previously published links may remain in an open tab or bookmark.
        // Redirect only these exact URLs: never restore anonymous static PDFs.
        endpoints.MapMethods(LegacyExpenseRequestGuidePath, ["GET", "HEAD"],
            (HttpResponse response) => RedirectLegacyGuide(response, ExpenseRequestGuidePath));
        endpoints.MapMethods(LegacyBudgetGuidePath, ["GET", "HEAD"],
            (HttpResponse response) => RedirectLegacyGuide(response, BudgetGuidePath));
        return endpoints;
    }

    private static IResult RedirectLegacyGuide(HttpResponse response, string target)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.Redirect(target, permanent: false, preserveMethod: true);
    }

    internal static IResult ReadExpenseRequestGuide(ICurrentActor actor, HttpResponse response)
        => ReadGuide(actor, response, "Bms.Api.Guides.ExpenseRequests20261019.pdf", "BMS-expense-requests-guide.pdf");

    internal static IResult ReadBudgetGuide(ICurrentActor actor, HttpResponse response)
        => ReadGuide(actor, response, "Bms.Api.Guides.BudgetFilling20261019.pdf", "BMS-budget-filling-guide.pdf");

    private static IResult ReadGuide(ICurrentActor actor, HttpResponse response, string resource, string fileName)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        try { _ = ActorIdentity.Require(actor); }
        catch (ActorNotAuthenticatedException)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.");
        }
        var stream = typeof(HelpGuideEndpoints).Assembly.GetManifestResourceStream(resource);
        if (stream is null) return Results.NotFound();
        response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";
        return Results.Stream(stream, "application/pdf", enableRangeProcessing: true);
    }
}
