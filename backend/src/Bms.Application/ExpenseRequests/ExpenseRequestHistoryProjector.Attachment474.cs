using System.Text.Json.Serialization;
using Bms.Application.Abstractions;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

internal static partial class ExpenseRequestHistoryProjector
{
    private static ExpenseRequestHistoryEventDto ProjectAttachment474(
        ExpenseRequestAuditSnapshotRow row, Guid? expectedRequestId, bool removed)
    {
        const string kind = "attachment";
        var title = removed ? "Файл удалён" : "Файл прикреплён";
        var payload = removed ? row.BeforeJson : row.AfterJson;
        if ((!removed && row.BeforeJson is not null)
            || !TryReadBusinessSnapshot<AttachmentSnapshot474>(payload, out var snapshot)
            || !MatchesHistoryRequest(snapshot.ExpenseRequestId, expectedRequestId)
            || !MatchesHistoryRequest(snapshot.AttachmentId, null)
            || !MatchesHistoryRequest(snapshot.RequestVersion, null)
            || !ExpenseRequestAttachmentKindCode.TryParse(snapshot.Kind, out var fileKind)
            || !IsHistoryText(snapshot.FileName, ExpenseRequestAttachmentUploadPolicy.MaximumFileNameLength)
            || !ExpenseRequestAttachmentUploadPolicy.Evaluate(snapshot.FileName, snapshot.FileSizeBytes).IsAccepted
            || (fileKind == ExpenseRequestAttachmentKind.AdditionalDocument) != (snapshot.Comment is not null)
            || (snapshot.Comment is not null && !IsHistoryText(snapshot.Comment, 1_000)))
            return Unavailable(row.OccurredAtUtc, kind, title);

        if (removed && (snapshot.IsPostSubmission
            || !TryReadBusinessSnapshot<AttachmentRemoval474>(row.AfterJson, out var response)
            || response.Attachment is not null
            || !SameHistoryIdentifier(snapshot.RequestVersion, response.RequestVersion)))
            return Unavailable(row.OccurredAtUtc, kind, title);

        var changes = new List<ExpenseRequestHistoryChangeDto>();
        void Add(string field, string value) => changes.Add(new("Вложения", field,
            removed ? "removed" : "added", removed ? value : null, removed ? null : value));
        Add("Файл", snapshot.FileName!);
        Add("Тип документа", fileKind switch
        {
            ExpenseRequestAttachmentKind.SupportingDocument => "Подтверждающий документ",
            ExpenseRequestAttachmentKind.NewContractDocument => "Новый договор",
            _ => "Дополнительный документ"
        });
        Add("Размер файла", snapshot.FileSizeBytes.ToString("N0", RussianCulture) + " байт");
        if (snapshot.Comment is not null) Add("Комментарий", snapshot.Comment);
        if (snapshot.IsPostSubmission) Add("После подачи на согласование", "Да");
        // Only four public metadata fields and the upload stage leave this projection.
        // Never return storage paths, identifiers, hashes, versions or document content.
        return CompleteBusinessEvent(row, kind, title, changes);
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class AttachmentSnapshot474
    {
        [JsonRequired] public string? ExpenseRequestId { get; init; }
        [JsonRequired] public string? AttachmentId { get; init; }
        [JsonRequired] public string? Kind { get; init; }
        [JsonRequired] public string? FileName { get; init; }
        [JsonRequired] public long FileSizeBytes { get; init; }
        [JsonRequired] public string? Comment { get; init; }
        [JsonRequired] public bool IsPostSubmission { get; init; }
        [JsonRequired] public string? RequestVersion { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class AttachmentRemoval474
    {
        [JsonRequired] public string? RequestVersion { get; init; }
        [JsonRequired] public object? Attachment { get; init; }
    }
}
