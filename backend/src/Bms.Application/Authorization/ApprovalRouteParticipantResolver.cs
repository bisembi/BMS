using Bms.Application.Abstractions;
using Bms.Domain.Workflow;

namespace Bms.Application.Authorization;

/// <summary>
/// Converts current active functional-role assignments into the participant
/// rows of one immutable approval-route snapshot. Missing or contradictory
/// source data is an availability failure, never a shortened route.
/// </summary>
public sealed class ApprovalRouteParticipantResolver(IApprovalRouteParticipantReader reader)
{
    private static readonly IReadOnlyDictionary<int, string> ApprovalRoleByStage =
        new Dictionary<int, string>
        {
            [1] = ExpenseRequestWorkflowCodes.FinancialControllerRoleCode,
            [2] = ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode,
            [3] = ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode
        };

    private static readonly IReadOnlyDictionary<int, string> BudgetApprovalRoleByStage =
        new Dictionary<int, string>
        {
            [1] = ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode,
            [2] = ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode
        };

    public Task<IReadOnlyList<ApprovalRouteSnapshotStageParticipants>> ResolveAsync(
        IReadOnlyList<ApprovalRouteStage> route,
        CancellationToken cancellationToken) => ResolveCoreAsync(route, ApprovalRoleByStage, cancellationToken);

    public Task<IReadOnlyList<ApprovalRouteSnapshotStageParticipants>> ResolveBudgetAsync(
        IReadOnlyList<ApprovalRouteStage> route,
        CancellationToken cancellationToken) => ResolveCoreAsync(route, BudgetApprovalRoleByStage, cancellationToken);

    public Task<IReadOnlyList<ApprovalRouteSnapshotStageParticipants>> ResolveGeneralDirectorAsync(
        CancellationToken cancellationToken) => ResolveCoreAsync(
            new[] { new ApprovalRouteStage(1, ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode) },
            new Dictionary<int, string> { [1] = ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode }, cancellationToken);

    public Task<IReadOnlyList<ApprovalRouteSnapshotStageParticipants>> ResolveSeniorAsync(CancellationToken token) =>
        ResolveCoreAsync(ApprovalRoutePlanner.Plan(100000.01m, 4),
            new Dictionary<int, string> { [1] = ExpenseRequestWorkflowCodes.FinancialControllerRoleCode,
                [2] = ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode }, token);

    public Task<IReadOnlyList<ApprovalRouteSnapshotStageParticipants>> ResolveConfiguredAsync(
        IReadOnlyList<ApprovalRouteStage> route, CancellationToken token)
    {
        var roles = route.Select(s => s.RoleCode).ToArray();
        // Only the finance subsequences of the closed approved 251 definitions.
        if (!(roles.SequenceEqual(new[] { "FIN_CONTROLLER" })
            || roles.SequenceEqual(new[] { "FIN_CONTROLLER", "FIN_DIRECTOR" })
            || roles.SequenceEqual(new[] { "FIN_CONTROLLER", "GENERAL_DIRECTOR" })
            || roles.SequenceEqual(new[] { "FIN_CONTROLLER", "FIN_DIRECTOR", "GENERAL_DIRECTOR" })
            || roles.SequenceEqual(new[] { "FIN_DIRECTOR" })
            || roles.SequenceEqual(new[] { "PAYROLL_CALCULATOR" })
            || roles.SequenceEqual(new[] { "FIN_DIRECTOR", "PAYROLL_CALCULATOR" })
            || roles.SequenceEqual(new[] { "FIN_DIRECTOR", "GENERAL_DIRECTOR" })))
            throw new ApprovalRouteParticipantResolutionUnavailableException();
        return ResolveCoreAsync(route, route.ToDictionary(s => s.StageNumber, s => s.RoleCode), token);
    }

    private async Task<IReadOnlyList<ApprovalRouteSnapshotStageParticipants>> ResolveCoreAsync(
        IReadOnlyList<ApprovalRouteStage> route,
        IReadOnlyDictionary<int, string> roleByStage,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(route);
            if (route.Count == 0
                || route.Count > roleByStage.Count
                || route.Any(stage => stage is null
                    || !roleByStage.TryGetValue(stage.StageNumber, out var expectedRoleCode)
                    || !string.Equals(stage.RoleCode, expectedRoleCode, StringComparison.Ordinal))
                || route.Select(stage => stage.StageNumber).Distinct().Count() != route.Count
                || !route.Select(stage => stage.StageNumber).Order().SequenceEqual(
                    Enumerable.Range(1, route.Count)))
            {
                throw new ApprovalRouteParticipantResolutionUnavailableException();
            }

            var expectedRoleCodes = route
                .Select(stage => stage.RoleCode)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(roleCode => roleCode, StringComparer.Ordinal)
                .ToArray();
            if (expectedRoleCodes.Length != route.Count)
            {
                throw new ApprovalRouteParticipantResolutionUnavailableException();
            }

            var rows = await reader.ReadAsync(expectedRoleCodes, cancellationToken);
            if (rows is null || rows.Any(row => row is null))
            {
                throw new ApprovalRouteParticipantResolutionUnavailableException();
            }

            var expectedRoles = expectedRoleCodes.ToHashSet(StringComparer.Ordinal);
            if (rows.Any(row => row.EmployeeId == Guid.Empty
                || !expectedRoles.Contains(row.RoleCode)))
            {
                throw new ApprovalRouteParticipantResolutionUnavailableException();
            }

            var employeeIdsByRole = rows
                .GroupBy(row => row.RoleCode, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(row => row.EmployeeId).ToArray(),
                    StringComparer.Ordinal);
            if (employeeIdsByRole.Count != expectedRoleCodes.Length
                || employeeIdsByRole.Any(pair => pair.Value.Length == 0
                    || pair.Value.Distinct().Count() != pair.Value.Length))
            {
                throw new ApprovalRouteParticipantResolutionUnavailableException();
            }

            return Array.AsReadOnly(route
                .OrderBy(stage => stage.StageNumber)
                .Select(stage => new ApprovalRouteSnapshotStageParticipants(
                    stage.StageNumber,
                    employeeIdsByRole[stage.RoleCode]
                        .OrderBy(employeeId => employeeId)
                        .ToArray()))
                .ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ApprovalRouteParticipantResolutionUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ApprovalRouteParticipantResolutionUnavailableException(exception);
        }
    }
}

public sealed class ApprovalRouteParticipantResolutionUnavailableException : Exception
{
    public ApprovalRouteParticipantResolutionUnavailableException()
        : base("Approval-route participants could not be resolved consistently.")
    {
    }

    public ApprovalRouteParticipantResolutionUnavailableException(Exception innerException)
        : base("Approval-route participants could not be resolved consistently.", innerException)
    {
    }
}
