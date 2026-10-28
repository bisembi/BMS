using Bms.Api.Problems;
using Bms.Application.Common;

namespace Bms.Api.Endpoints;

/// The request guards and problem mappings shared by the handlers. Every
/// failure they produce is a fixed public string from SafeProblemResults.
public static partial class ExpenseRequestEndpoints
{

    private static string RequireSingleIdempotencyKey(HttpRequest request)
    {
        var values = request.Headers["Idempotency-Key"];
        if (values.Count != 1)
        {
            throw new InvalidIdempotencyKeyException(
                "Exactly one Idempotency-Key header value is required.");
        }

        return values[0] ?? string.Empty;
    }

    private static IResult ConflictProblem(string code) => SafeProblemResults.Conflict(code);
}
