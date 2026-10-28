using Bms.Application.Abstractions;

namespace Bms.Application.Common;

public static class ActorIdentity
{
    public const int MaximumExternalIdLength = 256;

    public static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaximumExternalIdLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !value.Any(char.IsControl);

    public static string Require(ICurrentActor currentActor)
    {
        var externalId = currentActor.ExternalId;
        if (!currentActor.IsAuthenticated || !IsCanonical(externalId))
        {
            throw new ActorNotAuthenticatedException();
        }

        return externalId!;
    }

    public static string RequireBusinessDataAccess(ICurrentActor currentActor)
    {
        var externalId = Require(currentActor);
        if (!currentActor.CanAccessBusinessData)
        {
            throw new ActorForbiddenException();
        }

        return externalId;
    }

    public static string RequireReferenceDataManagement(ICurrentActor currentActor)
    {
        var externalId = Require(currentActor);
        if (!currentActor.CanManageReferenceData)
        {
            throw new ActorForbiddenException();
        }

        return externalId;
    }

    public static async Task RequireCurrentReferenceDataManagementAsync(
        ICurrentActor actor, string expectedExternalId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var externalId = RequireReferenceDataManagement(actor);
        if (!string.Equals(externalId, expectedExternalId, StringComparison.Ordinal)
            || !await actor.CanManageReferenceDataAsync(token)) throw new ActorForbiddenException();
        token.ThrowIfCancellationRequested();
        if (!string.Equals(Require(actor), expectedExternalId, StringComparison.Ordinal))
            throw new ActorForbiddenException();
    }
}
