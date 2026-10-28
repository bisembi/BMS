using System.Text.Json;
using System.Text.Json.Serialization;
using Bms.Api.Diagnostics;
using Bms.Api.Security;
using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Api.Endpoints;

internal static class ClientUiDiagnosticsEndpoints
{
    internal const string Path = "/api/v1/client-diagnostics";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 4,
    };

    internal static IEndpointRouteBuilder MapClientUiDiagnostics(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Path, Receive).RequireCsrfToken()
            .WithMetadata(new AuditorNonBusinessPost()).WithTags("Diagnostics");
        return endpoints;
    }

    internal static async Task<IResult> Receive(HttpContext context, ICurrentActor actor, ClientUiDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!actor.IsAuthenticated) return Results.Unauthorized();
        if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
        // Bound even chunked bodies, before parsing. Unknown fields are rejected.
        var bytes = new byte[2049];
        var length = 0;
        while (length < bytes.Length)
        {
            var read = await context.Request.Body.ReadAsync(bytes.AsMemory(length), cancellationToken);
            if (read == 0) break;
            length += read;
        }
        if (length > 2048) return Results.StatusCode(413);
        ClientUiFailure? value;
        try { value = JsonSerializer.Deserialize<ClientUiFailure>(bytes.AsSpan(0, length), Options); }
        catch (JsonException) { return Results.BadRequest(new { code = "invalid-ui-diagnostic" }); }
        if (value is null || !diagnostics.Valid(value)) return Results.BadRequest(new { code = "invalid-ui-diagnostic" });
        string identity;
        try { identity = ActorIdentity.Require(actor); }
        catch (ActorNotAuthenticatedException) { return Results.Unauthorized(); }
        return diagnostics.Record(value, identity) ? Results.NoContent() : Results.StatusCode(429);
    }
}
