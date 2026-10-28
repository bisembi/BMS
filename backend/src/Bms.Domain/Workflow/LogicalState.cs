namespace Bms.Domain.Workflow;

/// <summary>
/// Experimental working classification retained only for source compatibility
/// with the first W-05 draft.
///
/// OPEN-01 does not approve this exact set or its mapping to persisted statuses.
/// The executable workflow and the database schema therefore do not use this
/// enum. Approved logical-state codes must be loaded as data only after OPEN-01
/// is closed.
/// </summary>
[Obsolete("OPEN-01 is unresolved. Use approved DocumentStatus.LogicalStateCode data instead.")]
public enum LogicalState
{
    /// <summary>Draft that lacks required data: it reserves no budget (ТЗ 2.2).</summary>
    DraftIncomplete = 1,

    /// <summary>Complete draft: the first save creates the reservation.</summary>
    DraftComplete = 2,

    InApproval = 3,

    Returned = 4,

    Approved = 5,

    /// <summary>Mandatory master-data gate before treasury (ADR-30).</summary>
    NsiGate = 6,

    ToPay = 7,

    /// <summary>Partial payment never creates a managerial accrual (ТЗ 2.3).</summary>
    PartiallyPaid = 8,

    Paid = 9,

    Closed = 10,

    Rejected = 11,

    Cancelled = 12
}
