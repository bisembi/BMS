namespace Bms.Application.Abstractions;

/// <summary>
/// Reads active employees eligible for each requested functional role. It is
/// only used to capture an immutable approval-route snapshot at submission.
/// </summary>
public interface IApprovalRouteParticipantReader
{
    Task<IReadOnlyList<ApprovalRouteParticipantProjectionRow>> ReadAsync(
        IReadOnlyCollection<string> roleCodes,
        CancellationToken cancellationToken);
}

public sealed record ApprovalRouteParticipantProjectionRow(
    Guid EmployeeId,
    string RoleCode);
