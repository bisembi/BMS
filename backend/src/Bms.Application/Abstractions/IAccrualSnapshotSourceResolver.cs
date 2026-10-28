namespace Bms.Application.Abstractions;

public sealed record AccrualSnapshotSourceLine(
    int LineNumber,
    Guid LineId,
    Guid? CashFlowItemId,
    DateOnly? AccrualPeriod);

public sealed record AccrualSnapshotSourceIssue(
    int? LineNumber,
    string Code);

public sealed record AccrualSnapshotSourceCandidate(
    Guid LineId,
    bool FormsManagerialAccrual,
    Guid? AccrualExpenseItemId);

public sealed record AccrualSnapshotSourceResolution(
    IReadOnlyList<AccrualSnapshotSourceIssue> Issues,
    IReadOnlyList<AccrualSnapshotSourceCandidate> Candidates);

public interface IAccrualSnapshotSourceResolver
{
    Task<AccrualSnapshotSourceResolution> ResolveAsync(
        IReadOnlyList<AccrualSnapshotSourceLine> lines,
        CancellationToken cancellationToken);
}
