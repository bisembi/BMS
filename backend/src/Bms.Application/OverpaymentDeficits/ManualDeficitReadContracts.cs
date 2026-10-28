using Bms.Domain.Overpayments;

namespace Bms.Application.OverpaymentDeficits;

public sealed record ManualDeficitQuery(
    Guid? OrganizationId, bool IncludeSettled, bool OnlyPending, int Skip = 0, int Take = 25);

// Internal read boundary: the public endpoint never accepts these authority flags.
public sealed record ManualDeficitReadAccess(
    string ActorExternalId, bool ReadAllOrganizations, bool CanPropose, bool CanDecide);

public sealed record ManualDeficitReadRow(
    OverpaymentDeficitSnapshot Snapshot, string OrganizationName, string CounterpartyName,
    int CorrectionCount, bool HasPendingCorrection, bool OrganizationAllowed);

public sealed record ManualDeficitReadPage(IReadOnlyList<ManualDeficitReadRow> Items, int Total);

public sealed record ManualDeficitCorrectionReadPage(
    ManualDeficitReadRow Deficit, IReadOnlyList<ManualDeficitCorrectionSnapshot> Items, int Total);

public sealed record ManualDeficitDto(
    Guid Id, string OrganizationName, string CounterpartyName, string CurrencyCode,
    DateTimeOffset OccurredAtUtc, decimal OriginalAmount, decimal OriginalAmountKzt,
    decimal CurrentAmount, decimal CurrentAmountKzt, int CorrectionCount,
    bool HasPendingCorrection, bool CanPropose, bool CanDecide);

public sealed record ManualDeficitPageDto(
    int Skip, int Take, int Total, IReadOnlyList<ManualDeficitDto> Items);

public sealed record ManualDeficitDetailDto(
    ManualDeficitDto Deficit, int Skip, int Take, int Total,
    IReadOnlyList<ManualDeficitCorrectionDto> Corrections);

public sealed class InvalidManualDeficitQueryException : Exception
{
    public InvalidManualDeficitQueryException() : base("The deficit query is invalid.") { }
}
