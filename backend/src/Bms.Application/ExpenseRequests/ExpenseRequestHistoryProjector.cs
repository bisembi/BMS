using System.Globalization;
using System.Text.Json;
using Bms.Application.Abstractions;

namespace Bms.Application.ExpenseRequests;

/// Projects an audit snapshot row onto the safe history event the API returns.
///
/// Reading the snapshot lives here; the header changes, the line changes, the
/// change writers and the display formatting live in the sibling partial
/// files. The order of events and of changes inside an event is part of the
/// contract and is not re-sorted anywhere in this type.

internal static partial class ExpenseRequestHistoryProjector
{
    private const int MaximumChangesPerEvent = 200;
    private const string MainSection = "Основное";
    private const string CounterpartySection = "Контрагент и основание оплаты";
    private const string LinesSection = "Расходы";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    // Project only the saved business fields; the requested identity binds new envelopes to this card.
    public static ExpenseRequestHistoryEventDto Project(
        ExpenseRequestAuditSnapshotRow row,
        Guid? expectedRequestId = null)
    {
        var projected = ProjectCore(row, expectedRequestId);
        // Null is the legacy non-history caller contract (technical audit).
        // Card history always supplies a resolved name or an explicit fallback.
        return row.ActorName is null ? projected : projected with
        {
            ActorLabel = PublicActorName(row.ActorName)
        };
    }

    private static string PublicActorName(string value) =>
        value.Length is > 0 and <= 500 && value.Trim() == value
        && value.All(character => char.IsLetter(character) || character is ' ' or '-' or '\'' or '’' or '.')
        && value.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: >= 2 } parts
        && parts.All(part => part.Any(char.IsLetter))
            ? value : "Автор не определён";

    private static ExpenseRequestHistoryEventDto ProjectCore(
        ExpenseRequestAuditSnapshotRow row, Guid? expectedRequestId)
    {
        var businessEvent = ProjectBusinessEvent(row, expectedRequestId);
        if (businessEvent is not null)
        {
            return businessEvent;
        }

        var eventDefinition = row.Action switch
        {
            "DraftCreated" => (Kind: "created", Title: "Черновик создан", NeedsBefore: false),
            "DraftCopied" => (Kind: "created", Title: "Черновик создан копированием", NeedsBefore: false),
            "DraftUpdated" => (Kind: "updated", Title: "Черновик изменён", NeedsBefore: true),
            "ApprovalDetailsUpdated" => (Kind: "updated", Title: "Реквизиты изменены при согласовании", NeedsBefore: true),
            _ => (Kind: "unsupported", Title: "Событие заявки", NeedsBefore: false)
        };

        if (eventDefinition.Kind == "unsupported")
        {
            return Unavailable(row.OccurredAtUtc, eventDefinition.Kind, eventDefinition.Title);
        }

        if (!TryDeserialize(row.AfterJson, out var after)
            || after is null
            || !HasRequiredSnapshotShape(after)
            || (eventDefinition.NeedsBefore && !TryDeserialize(row.BeforeJson, out _)))
        {
            return Unavailable(row.OccurredAtUtc, eventDefinition.Kind, eventDefinition.Title);
        }

        AuditSnapshot? before = null;
        if (eventDefinition.NeedsBefore)
        {
            _ = TryDeserialize(row.BeforeJson, out before);
        }

        var changes = BuildChanges(before, after, out var isPartial, row.References);
        var omittedChanges = Math.Max(changes.Count - MaximumChangesPerEvent, 0);
        var visibleChanges = changes.Take(MaximumChangesPerEvent).ToArray();
        var initiatorExternalId = after.InitiatorExternalId ?? before?.InitiatorExternalId;
        var actorLabel = !string.IsNullOrWhiteSpace(initiatorExternalId)
            && string.Equals(row.ActorExternalId, initiatorExternalId, StringComparison.Ordinal)
                ? "Инициатор"
                : "Пользователь BMS";

        return new ExpenseRequestHistoryEventDto(
            row.OccurredAtUtc,
            eventDefinition.Kind,
            eventDefinition.Title,
            actorLabel,
            isPartial || omittedChanges > 0 ? "partial" : "complete",
            visibleChanges,
            omittedChanges);
    }

    private static ExpenseRequestHistoryEventDto Unavailable(
        DateTimeOffset occurredAtUtc,
        string kind,
        string title) => new(
            occurredAtUtc,
            kind,
            title,
            "Пользователь BMS",
            "unavailable",
            [],
            0);

    private static bool HasRequiredSnapshotShape(AuditSnapshot? snapshot) =>
        snapshot is not null
        && !string.IsNullOrWhiteSpace(snapshot.Status)
        && !string.IsNullOrWhiteSpace(snapshot.InitiatorExternalId);

    private static bool TryDeserialize(string? json, out AuditSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            snapshot = JsonSerializer.Deserialize<AuditSnapshot>(json, JsonOptions);
            return snapshot is not null;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
