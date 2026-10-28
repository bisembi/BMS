using Bms.Domain.Common;
using Bms.Domain.Workflow;

namespace Bms.Domain.Budgeting;

public static class SubmissionBudgetPreflightBlockers
{
    public const string ProjectionIncomplete = "BudgetProjection.Incomplete";
    public const string ProjectionDrift = "BudgetProjection.Drift";
    public const string SnapshotUnavailable = "BudgetSnapshot.Unavailable";
    public const string LimitUnavailable = "BudgetLimit.Unavailable";
    public const string DeficitScaleUnsupported = "ApprovalRoute.DeficitScaleUnsupported";
}

public sealed record SubmissionBudgetPositionSnapshot(
    BudgetKey Key,
    decimal? LimitAmountKzt,
    decimal CurrentReservedAmountKzt,
    decimal PaidAmountKzt,
    decimal ExistingRequestProjectionAmountKzt,
    decimal DesiredRequestProjectionAmountKzt,
    bool IsAggregateConsistent);

public sealed record SubmissionBudgetSnapshot(
    bool RequestProjectionMatches,
    IReadOnlyList<SubmissionBudgetPositionSnapshot> Positions)
{
    /// <summary>Only an immutable SQL register decision for the unchanged
    /// approved financial fingerprint may supply this internal override.</summary>
    public decimal? ApprovedDeficit343 { get; init; }
}

public sealed record SubmissionBudgetPreflight(
    bool IsReady,
    decimal? PositiveDeficitKzt,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<ApprovalRouteStage> RoutePreview);

public static class SubmissionBudgetPreflightEvaluator
{
    public static SubmissionBudgetPreflight Evaluate(SubmissionBudgetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // The record constructor does not enforce runtime collection integrity.
        // Treat a missing collection or null element as an unavailable snapshot
        // before any projection/limit dereference can throw.
        if (snapshot.Positions is null
            || snapshot.Positions.Count == 0
            || snapshot.Positions.Any(position => position is null))
        {
            return Blocked(SubmissionBudgetPreflightBlockers.SnapshotUnavailable);
        }

        if (!snapshot.RequestProjectionMatches ||
            snapshot.Positions.Any(position => !position.IsAggregateConsistent))
        {
            return Blocked(SubmissionBudgetPreflightBlockers.ProjectionDrift);
        }

        if (snapshot.Positions.Any(position => !position.LimitAmountKzt.HasValue))
        {
            return Blocked(SubmissionBudgetPreflightBlockers.LimitUnavailable);
        }

        var amounts = snapshot.Positions.SelectMany(position => new[]
        {
            position.LimitAmountKzt!.Value,
            position.CurrentReservedAmountKzt,
            position.PaidAmountKzt,
            position.ExistingRequestProjectionAmountKzt,
            position.DesiredRequestProjectionAmountKzt
        }).ToArray();
        if (snapshot.Positions.Any(position =>
                position.LimitAmountKzt < 0m ||
                position.CurrentReservedAmountKzt < 0m ||
                position.PaidAmountKzt < 0m ||
                position.ExistingRequestProjectionAmountKzt < 0m ||
                position.DesiredRequestProjectionAmountKzt < 0m) ||
            amounts.Any(amount =>
                amount < -MoneyAmountPolicy.Maximum ||
                amount > MoneyAmountPolicy.Maximum))
        {
            return Blocked(SubmissionBudgetPreflightBlockers.SnapshotUnavailable);
        }

        var hasScaleDrift = amounts.Any(amount =>
            decimal.Round(amount, MoneyAmountPolicy.Scale, MidpointRounding.ToEven) != amount);

        decimal positiveDeficitKzt = 0m;
        foreach (var position in snapshot.Positions)
        {
            decimal reservedAfter;
            decimal availableAfter;
            try
            {
                reservedAfter = position.CurrentReservedAmountKzt
                    - position.ExistingRequestProjectionAmountKzt
                    + position.DesiredRequestProjectionAmountKzt;
                availableAfter = position.LimitAmountKzt!.Value
                    - reservedAfter
                    - position.PaidAmountKzt;
                if (availableAfter < 0m)
                {
                    positiveDeficitKzt = checked(positiveDeficitKzt - availableAfter);
                }
            }
            catch (OverflowException)
            {
                return Blocked(SubmissionBudgetPreflightBlockers.SnapshotUnavailable);
            }

            if (reservedAfter < 0m)
            {
                return Blocked(SubmissionBudgetPreflightBlockers.ProjectionDrift);
            }

            if (reservedAfter < -MoneyAmountPolicy.Maximum ||
                reservedAfter > MoneyAmountPolicy.Maximum ||
                availableAfter < -MoneyAmountPolicy.Maximum ||
                availableAfter > MoneyAmountPolicy.Maximum ||
                positiveDeficitKzt > MoneyAmountPolicy.Maximum)
            {
                return Blocked(SubmissionBudgetPreflightBlockers.SnapshotUnavailable);
            }
        }

        if (hasScaleDrift)
        {
            // The snapshot contains a monetary value outside decimal(18,2).
            // Do not publish a derived deficit: it may carry the same
            // unsupported scale and cannot cross the public money boundary.
            return Blocked(SubmissionBudgetPreflightBlockers.DeficitScaleUnsupported);
        }

        try
        {
            if(snapshot.ApprovedDeficit343 is{} approved)
            {
                if(approved<0m||approved>MoneyAmountPolicy.Maximum||decimal.Round(approved,MoneyAmountPolicy.Scale)!=approved)
                    return Blocked(SubmissionBudgetPreflightBlockers.SnapshotUnavailable);
                positiveDeficitKzt=approved;
            }
            var route = ApprovalRoutePlanner.Plan(positiveDeficitKzt);
            return new SubmissionBudgetPreflight(
                true,
                positiveDeficitKzt,
                [],
                route);
        }
        catch (DomainRuleViolationException)
        {
            return Blocked(
                SubmissionBudgetPreflightBlockers.DeficitScaleUnsupported,
                positiveDeficitKzt);
        }
    }

    private static SubmissionBudgetPreflight Blocked(
        string blocker,
        decimal? positiveDeficitKzt = null) => new(
        false,
        positiveDeficitKzt,
        [blocker],
        []);
}
