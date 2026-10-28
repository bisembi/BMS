namespace Bms.Application.ExpenseRequests;

public sealed record ExpenseRequestHistoryChangeDto(
    string Section,
    string Field,
    string ChangeKind,
    string? Before,
    string? After);

public sealed record ExpenseRequestHistoryEventDto(
    DateTimeOffset OccurredAtUtc,
    string Kind,
    string Title,
    string ActorLabel,
    string DetailsStatus,
    IReadOnlyList<ExpenseRequestHistoryChangeDto> Changes,
    int OmittedChanges);

public sealed record ExpenseRequestHistoryPageDto(
    IReadOnlyList<ExpenseRequestHistoryEventDto> Items,
    int Skip,
    int Take,
    int Total);
