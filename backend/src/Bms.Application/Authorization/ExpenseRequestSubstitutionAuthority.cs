using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.Authorization;

/// <summary>
/// Resolves whether the current actor may act in the immutable initiator's
/// capacity for an existing expense request. It never grants creation, copy,
/// approval, cancellation or closing rights.
/// </summary>
public sealed class ExpenseRequestSubstitutionAuthority(
    IExpenseRequestSubstitutionReader reader)
{
    private const int MaximumReasonLength = 500;

    public async Task<ExpenseRequestActionAuthority> ResolveAsync(
        string initiatorExternalId,
        string actorExternalId,
        DateTimeOffset authorizationAtUtc,
        CancellationToken cancellationToken)
    {
        if (!ActorIdentity.IsCanonical(initiatorExternalId)
            || !ActorIdentity.IsCanonical(actorExternalId))
        {
            throw new ExpenseRequestSubstitutionAuthorityUnavailableException();
        }

        if (string.Equals(initiatorExternalId, actorExternalId, StringComparison.Ordinal))
        {
            return ExpenseRequestActionAuthority.Direct(initiatorExternalId);
        }

        try
        {
            var rows = await reader.ReadAsync(
                initiatorExternalId,
                actorExternalId,
                cancellationToken);
            if (rows is null || rows.Any(row => row is null))
            {
                throw new ExpenseRequestSubstitutionAuthorityUnavailableException();
            }

            var active = new List<ExpenseRequestSubstitutionProjectionRow>(1);
            foreach (var row in rows)
            {
                ValidateRow(row, initiatorExternalId, actorExternalId);
                if (row.SubstitutionIsActive
                    && row.PrincipalEmployeeIsActive
                    && row.DeputyEmployeeIsActive
                    && row.PrincipalMappingIsActive
                    && row.DeputyMappingIsActive
                    && row.ValidFromUtc <= authorizationAtUtc
                    && row.ValidThroughUtc >= authorizationAtUtc)
                {
                    active.Add(row);
                }
            }

            return active.Count switch
            {
                0 => ExpenseRequestActionAuthority.Denied(
                    initiatorExternalId,
                    actorExternalId),
                1 => ExpenseRequestActionAuthority.Substituted(active[0]),
                _ => throw new ExpenseRequestSubstitutionAuthorityUnavailableException()
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ExpenseRequestSubstitutionAuthorityUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ExpenseRequestSubstitutionAuthorityUnavailableException(exception);
        }
    }

    private static void ValidateRow(
        ExpenseRequestSubstitutionProjectionRow row,
        string expectedPrincipalActorExternalId,
        string expectedDeputyActorExternalId)
    {
        if (row.SubstitutionId == Guid.Empty
            || row.PrincipalEmployeeId == Guid.Empty
            || row.DeputyEmployeeId == Guid.Empty
            || row.PrincipalEmployeeId == row.DeputyEmployeeId
            || !string.Equals(
                row.PrincipalActorExternalId,
                expectedPrincipalActorExternalId,
                StringComparison.Ordinal)
            || !string.Equals(
                row.DeputyActorExternalId,
                expectedDeputyActorExternalId,
                StringComparison.Ordinal)
            || row.ValidFromUtc > row.ValidThroughUtc
            || !IsCanonicalReason(row.Reason))
        {
            throw new ExpenseRequestSubstitutionAuthorityUnavailableException();
        }
    }

    private static bool IsCanonicalReason(string? value) =>
        value is not null
        && value.Length is > 0 and <= MaximumReasonLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && value.All(character => !char.IsControl(character));
}

public sealed record ExpenseRequestActionAuthority(
    bool IsAuthorized,
    string InitiatorExternalId,
    string ActorExternalId,
    Guid? SubstitutionId,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidThroughUtc,
    string? Reason)
{
    public bool IsSubstitution => SubstitutionId.HasValue;

    public static ExpenseRequestActionAuthority Direct(string actorExternalId) =>
        new(true, actorExternalId, actorExternalId, null, null, null, null);

    public static ExpenseRequestActionAuthority Denied(
        string initiatorExternalId,
        string actorExternalId) =>
        new(false, initiatorExternalId, actorExternalId, null, null, null, null);

    public static ExpenseRequestActionAuthority Substituted(
        ExpenseRequestSubstitutionProjectionRow row) =>
        new(
            true,
            row.PrincipalActorExternalId,
            row.DeputyActorExternalId,
            row.SubstitutionId,
            row.ValidFromUtc,
            row.ValidThroughUtc,
            row.Reason);
}

public sealed class ExpenseRequestSubstitutionAuthorityUnavailableException : Exception
{
    private const string SafeMessage =
        "The expense-request substitution authority could not be resolved consistently.";

    public ExpenseRequestSubstitutionAuthorityUnavailableException()
        : base(SafeMessage)
    {
    }

    public ExpenseRequestSubstitutionAuthorityUnavailableException(Exception innerException)
        : base(SafeMessage, innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}
