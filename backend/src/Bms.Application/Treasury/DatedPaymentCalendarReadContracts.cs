namespace Bms.Application.Treasury;

/// <summary>
/// Captures the exact visible period scope in the caller's Serializable
/// transaction. The reader owns authorization SQL, metadata, card visibility,
/// count guards BEFORE materialization, and the COMPLETE evidence of each
/// selected request. It never pages or filters evidence by status/currency/date.
/// Optional public status/currency filters belong to the validated composer.
/// </summary>
public interface IDatedPaymentCalendarReader
{
    Task<IReadOnlyList<DatedPaymentCalendarRequestCapture>> ReadVisibleScopeAsync(
        string actorExternalId,
        bool includeAll,
        DatedPaymentCalendarPeriod period,
        CancellationToken cancellationToken);
}

/// <summary>Absent configuration is null, never an implicit machine/UTC zone.</summary>
public interface IDatedPaymentCalendarTimeZoneProvider
{
    TimeZoneInfo? Resolve();
}

/// <summary>
/// Technical synchronous-capture ceilings, not monetary or business limits.
/// Each source set must be checked in SQL before materializing its rows.
/// Exceeding any ceiling refuses the report; no truncated evidence is valid.
/// </summary>
public static class DatedPaymentCalendarCapturePolicy
{
    public const int MaximumRequests = PaymentCalendarExportPolicy.MaximumRows;
    public const int MaximumLines = PaymentCalendarExportPolicy.MaximumRows;
    public const int MaximumPaymentFacts = PaymentCalendarExportPolicy.MaximumRows;
    public const int MaximumAllocations = PaymentCalendarExportPolicy.MaximumRows;
}
