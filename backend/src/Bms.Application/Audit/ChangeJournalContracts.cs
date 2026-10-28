namespace Bms.Application.Audit;

// Raw SQL audit snapshots are server-only; they are never the HTTP contract.
public sealed record ChangeJournalRecord(
    DateTimeOffset OccurredAtUtc, string? AuthorName, string ObjectType,
    string Action, string? AfterJson);

public sealed record ChangeJournalReadPage(IReadOnlyList<ChangeJournalRecord> Items, int Total);

public sealed record ChangeJournalItemDto(
    DateTimeOffset OccurredAtUtc, string Author, string Event, string Object, string Description);

public sealed record ChangeJournalPageDto(
    IReadOnlyList<ChangeJournalItemDto> Items, int Skip, int Take, int Total);
