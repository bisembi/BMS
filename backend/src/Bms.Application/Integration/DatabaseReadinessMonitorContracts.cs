namespace Bms.Application.Integration;

public sealed record DatabaseReadinessMonitorRecord(
    bool DatabaseReady,
    int AppliedMigrationCount,
    int RequiredMigrationCount,
    IReadOnlyList<string> MissingComponents);

public sealed record DatabaseReadinessMonitorDto(
    bool DatabaseReady,
    int AppliedMigrationCount,
    int RequiredMigrationCount,
    DateTimeOffset CheckedAtUtc,
    IReadOnlyList<string> MissingComponents);

public interface IDatabaseReadinessMonitorReader
{
    Task<DatabaseReadinessMonitorRecord> ReadAsync(CancellationToken cancellationToken);
}
