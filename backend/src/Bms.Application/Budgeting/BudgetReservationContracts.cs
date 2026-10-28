namespace Bms.Application.Budgeting;

/// <summary>
/// One expense request that holds part of the current reserve of one budget
/// position.
///
/// A request can put several of its lines on the same budget key, and the
/// projection stores one row per line. Those rows are aggregated here into one
/// visible participant: <see cref="LineCount"/> says how many lines it has on
/// this key and <see cref="ReservedAmountKzt"/> is their sum.
///
/// The request is described by what the model actually holds. There is no
/// request number in this system - the form itself shows the number as "will be
/// assigned" - so a participant is identified by its purpose, its planned
/// payment date and its status. The request identifier is deliberately absent:
/// nothing on this surface opens a request, and an identifier published without
/// a use is an identifier leaked.
/// </summary>
public sealed record BudgetReservationParticipantDto(
    string Status,
    string? Purpose,
    DateOnly PlannedPaymentDate,
    int LineCount,
    decimal ReservedAmountKzt);

/// <summary>
/// The composition of one budget position's current reserve.
///
/// <see cref="TotalReservedAmountKzt"/> is the whole reserve of the position,
/// not the sum of the page, and it must equal the position's own reserved
/// amount - the writer keeps those two equal by construction, so a reader that
/// finds them apart is looking at a projection it cannot trust.
///
/// Only the current reserve is described. The paid part of a position is not
/// broken down per request anywhere in this contract: a payment fact carries a
/// request and no budget key, a request's lines can span several keys, and no
/// allocation rule exists to divide one by the other.
/// </summary>
public sealed record BudgetReservationPageDto(
    IReadOnlyList<BudgetReservationParticipantDto> Items,
    int Skip,
    int Take,
    int Total,
    int TotalLineCount,
    decimal TotalReservedAmountKzt,
    BudgetPositionDto Position)
{
    public decimal VisibleReservedAmountKzt { get; init; } = TotalReservedAmountKzt;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ExecutionFactSourceDto? FactSource { get; init; }
}
