using Bms.Application.ReferenceData;

namespace Bms.Application.Abstractions;

/// <summary>
/// Write boundary for the shared reference catalogues.  The application
/// service owns authorization, transaction and audit; the infrastructure
/// implementation owns the EF entity details and foreign-key rules.
/// </summary>
public interface IReferenceDataMaintenanceStore
{
    Task<ReferenceDataMaintenanceItem> CreateAsync(
        ReferenceDataMaintenanceCommand command,
        CancellationToken cancellationToken);

    Task<ReferenceDataMaintenanceItem> UpdateAsync(
        string directoryId,
        Guid id,
        ReferenceDataMaintenanceCommand command,
        CancellationToken cancellationToken);

    Task<ReferenceDataMaintenanceItem> DeleteAsync(
        string directoryId,
        Guid id,
        CancellationToken cancellationToken);
}
