using Bms.Application.OverpaymentDeficits;

namespace Bms.Application.Abstractions;

/// <summary>Reads one bounded page inside the caller's captured authorization transaction.</summary>
public interface IManualDeficitReader
{
    Task<ManualDeficitReadPage> ListAsync(
        ManualDeficitReadAccess access, ManualDeficitQuery query, CancellationToken cancellationToken);
    Task<ManualDeficitCorrectionReadPage?> ReadAsync(
        Guid deficitId, ManualDeficitReadAccess access, int skip, int take, CancellationToken cancellationToken);
}
