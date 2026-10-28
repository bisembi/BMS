using Bms.Application.Budgeting;

namespace Bms.Application.Abstractions;

public interface IBddsReportStore
{
    Task<BddsImportResultDto?> FindImportAsync(string sourceHash, CancellationToken cancellationToken);

    Task<BddsImportResultDto> ImportAsync(
        ImportBddsSnapshotCommand command,
        Guid approvedByEmployeeId,
        string approvedByExternalId,
        DateTimeOffset approvedAtUtc,
        CancellationToken cancellationToken);

    Task<BddsReportDto?> ReadAsync(
        int year,
        Guid? costCenterId,
        Guid employeeId,
        bool canSeeAll,
        CancellationToken cancellationToken);

    Task<BddsReportDto?> ReadPeriodAsync(int year, Guid? costCenterId, Guid employeeId,
        bool canSeeAll, BddsReportOptions options, CancellationToken cancellationToken);
}
