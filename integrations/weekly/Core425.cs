using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bms.Infrastructure.Mail;
using MimeKit;

namespace Bms.WeeklyPrimary425;

internal static class Wire425
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal const int MaxOwners = 300, MaxRows = 5000, MaxInputCharacters = 16 * 1024 * 1024;
    internal const string Approval = "BMS425_USER_APPROVED_WEEKLY_CFO_PRIMARY_REMINDERS_MON0900_UTC5";
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(text)));
    internal static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    internal static void Require(bool condition, string code) { if (!condition) throw new Guard425(code); }
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    internal static T Parse<T>(string value)
    {
        using var doc = JsonDocument.Parse(value);
        CheckDuplicateNames(doc.RootElement);
        return JsonSerializer.Deserialize<T>(value, Json) ?? throw new Guard425("EMPTY_JSON");
    }
    private static void CheckDuplicateNames(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in value.EnumerateObject()) { Require(names.Add(p.Name), "DUPLICATE_JSON_PROPERTY"); CheckDuplicateNames(p.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) CheckDuplicateNames(item);
    }
}
internal sealed class Guard425(string code) : Exception(code) { internal string Code { get; } = code; }
internal sealed record Config425(int SchemaVersion, bool Enabled, Guid DeploymentId, Guid LedgerId,
    string LdapServer, string PackageManifestSha256, string Approval);
internal sealed record Batch425(Guid EmployeeId, string Recipient, string OwnerDisplayName, DateOnly AsOfDate, IReadOnlyList<ReminderRow425> Rows)
{
    internal OwnerReminder425 Reminder() => new(EmployeeId, OwnerDisplayName, AsOfDate, Rows);
}
internal sealed record Input425(string Mode, DateTimeOffset AsOfUtc, DateOnly WeekMonday, IReadOnlyList<Batch425> Batches,
    bool SqlLockHeld, bool SourceChecksPassed, int RuntimeServicePid, JsonElement SourceEvidence);
internal sealed record Revalidation425(string Kind, Guid EmployeeId, bool Eligible, DateTimeOffset SourceReadStartedAtUtc,
    DateTimeOffset SourceReadCompletedAtUtc, DateTimeOffset RevalidatedAtUtc, Batch425? Batch);
internal sealed record Prepared425(DateOnly WeekMonday, Guid EmployeeId, string Recipient, string CanonicalBatchSha256,
    string Subject, string Html, string Text, string SubjectSha256, string HtmlSha256, string TextSha256, string MessageId);
internal sealed record Attempt425(int Number, DateTimeOffset AtUtc, string PreparedSha256, string? AuthorizationSha256);
internal sealed record Outcome425(int AttemptNumber, string State, DateTimeOffset AtUtc, bool SendStarted, string PreparedSha256);
internal sealed record RetryAuthorization425(int AttemptNumber, DateOnly WeekMonday, Guid EmployeeId, string PriorOutcomeSha256,
    string PreparedSha256, DateTimeOffset ApprovedAtUtc, string ApprovedBySid, string Approval);
internal sealed record InitialState425(int SchemaVersion, Guid LedgerId, DateTimeOffset CreatedAtUtc);

internal static class Calendar425
{
    internal static DateTimeOffset Local(DateTimeOffset utc) => utc.ToOffset(TimeSpan.FromHours(5));
    internal static DateOnly Date(DateTimeOffset utc) => DateOnly.FromDateTime(Local(utc).DateTime);
    internal static DateOnly Monday(DateTimeOffset utc)
    {
        var date = Date(utc); return date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    }
    internal static bool MaySend(DateTimeOffset now) { var local = Local(now); return local.DayOfWeek == DayOfWeek.Monday && local.Hour >= 9 && local.Hour < 18; }
}

internal static class Content425
{
    internal static string CanonicalHash(Batch425 b)
    {
        var normalized = b with { Rows = b.Rows.OrderBy(r => r.DocumentId).Select(r => r with {
            DocumentKey = Guid.Parse(r.DocumentKey).ToString("D"),
            CostCenterOwnerIds = r.CostCenterOwnerIds.OrderBy(x => x).ToArray(),
            CashFlowArticles = r.CashFlowArticles.OrderBy(x => x.Id).ThenBy(x => x.Name, StringComparer.Ordinal).ToArray()
        }).ToArray() };
        return Wire425.Hash(Wire425.Serialize(normalized));
    }
    internal static Prepared425? Prepare(Batch425 batch, DateOnly week)
    {
        Wire425.Require(batch.EmployeeId != Guid.Empty && TaskMailDelivery.ValidAddress(batch.Recipient)
            && batch.Recipient == batch.Recipient.Trim(), "RECIPIENT_INVALID");
        var body = PrimaryDocumentsTemplate425.Render(batch.Reminder());
        if (body is null) return null;
        Wire425.Require(body.Subject.Length is > 0 and <= 256 && !body.Subject.Any(char.IsControl)
            && Wire425.Utf8.GetByteCount(body.Subject) <= 1024, "SUBJECT_BOUND");
        foreach (var text in new[] { body.Html, body.Text }) Wire425.Require(Wire425.Utf8.GetByteCount(text) is > 0 and <= 131072
            && !text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')), "BODY_BOUND");
        var key = week.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" + batch.EmployeeId.ToString("N");
        return new(week, batch.EmployeeId, batch.Recipient, CanonicalHash(batch), body.Subject, body.Html, body.Text,
            Wire425.Hash(body.Subject), Wire425.Hash(body.Html), Wire425.Hash(body.Text), Wire425.Hash("bms425|" + key).ToLowerInvariant() + "@bms.example.invalid");
    }
    internal static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);
    internal static MimeMessage Build(Prepared425 p, string from)
    {
        Wire425.Require(TaskMailDelivery.ValidAddress(from) && TaskMailDelivery.ValidAddress(p.Recipient), "ADDRESS_INVALID");
        Wire425.Require(Wire425.Hash(p.Subject) == p.SubjectSha256 && Wire425.Hash(p.Html) == p.HtmlSha256 && Wire425.Hash(p.Text) == p.TextSha256, "BODY_HASH_DRIFT");
        var message = new MimeMessage { Subject = p.Subject, MessageId = p.MessageId };
        message.From.Add(new MailboxAddress("BMS — первичные документы", from)); message.To.Add(MailboxAddress.Parse(p.Recipient));
        message.Body = new MultipartAlternative { new TextPart("plain") { Text = p.Text }, new TextPart("html") { Text = p.Html } };
        Wire425.Require(message.To.Count == 1 && message.Cc.Count == 0 && message.Bcc.Count == 0 && message.From.Count == 1
            && !message.Attachments.Any() && message.Subject == p.Subject
            && NormalizeNewlines(message.TextBody ?? "") == NormalizeNewlines(p.Text)
            && NormalizeNewlines(message.HtmlBody ?? "") == NormalizeNewlines(p.Html), "MIME_ROUNDTRIP_DRIFT");
        return message;
    }
    internal static bool RevalidationMatches(Prepared425 prepared, Revalidation425 fresh, DateTimeOffset now)
    {
        if (fresh.Kind != "revalidation" || fresh.EmployeeId != prepared.EmployeeId || !fresh.Eligible || fresh.Batch is null
            || fresh.Batch.EmployeeId != prepared.EmployeeId || fresh.Batch.AsOfDate != Calendar425.Date(now)
            || fresh.SourceReadStartedAtUtc > fresh.SourceReadCompletedAtUtc || fresh.SourceReadCompletedAtUtc > fresh.RevalidatedAtUtc
            || fresh.RevalidatedAtUtc - fresh.SourceReadStartedAtUtc > TimeSpan.FromSeconds(30)
            || now - fresh.RevalidatedAtUtc > TimeSpan.FromSeconds(10) || fresh.RevalidatedAtUtc - now > TimeSpan.FromSeconds(2)) return false;
        var current = Prepare(fresh.Batch, prepared.WeekMonday);
        return current is not null && current.CanonicalBatchSha256 == prepared.CanonicalBatchSha256
            && current.Recipient == prepared.Recipient && current.SubjectSha256 == prepared.SubjectSha256
            && current.HtmlSha256 == prepared.HtmlSha256 && current.TextSha256 == prepared.TextSha256;
    }
}

internal sealed class Ledger425(string root)
{
    internal string Root { get; } = Path.GetFullPath(root);
    internal FileStream Lock() => new(Path.Combine(Root, "process.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    internal static void WriteNew(string path, string value)
    {
        var bytes = Wire425.Utf8.GetBytes(value);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes); file.Flush(true);
    }
    internal static void AtomicNew(string path, string value)
    {
        // An orphan temporary is harmless. Existing final paths are NEVER overwritten.
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        WriteNew(temporary, value); File.Move(temporary, path, false);
    }
    internal string DirectoryFor(DateOnly week, Guid employee)
    {
        Wire425.Require(week.DayOfWeek == DayOfWeek.Monday && employee != Guid.Empty, "LEDGER_KEY_INVALID");
        return Path.Combine(Root, "weeks", week.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), employee.ToString("N"));
    }
    internal bool AutomaticRunExists(DateOnly week) => File.Exists(Path.Combine(Root, "weeks", week.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "automatic-run.marker"));
    internal void ClaimAutomaticRun(DateOnly week)
    {
        var directory = Path.Combine(Root, "weeks", week.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); Directory.CreateDirectory(directory);
        WriteNew(Path.Combine(directory, "automatic-run.marker"), Wire425.Serialize(new { startedAtUtc = DateTimeOffset.UtcNow }));
    }
    internal void ValidatePrepared(Prepared425 value)
    {
        var path = Path.Combine(DirectoryFor(value.WeekMonday, value.EmployeeId), "prepared.json");
        if (File.Exists(path)) Wire425.Require(File.ReadAllText(path, Wire425.Utf8) == Wire425.Serialize(value), "PREPARED_CHANGED_MANUAL_REVIEW_REQUIRED");
    }
    internal string StorePrepared(Prepared425 value)
    {
        var directory = DirectoryFor(value.WeekMonday, value.EmployeeId); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "prepared.json"); var json = Wire425.Serialize(value);
        if (!File.Exists(path)) AtomicNew(path, json);
        Wire425.Require(File.ReadAllText(path, Wire425.Utf8) == json, "PREPARED_DRIFT"); return Wire425.Hash(json);
    }
    internal (int Number, string? AuthorizationSha, string State) NextAttempt(Prepared425 p, string mode)
    {
        var directory = DirectoryFor(p.WeekMonday, p.EmployeeId);
        var first = Path.Combine(directory, "attempt-001.marker");
        if (!File.Exists(first)) return Directory.Exists(directory) && Directory.EnumerateFiles(directory, "attempt-*").Any()
            ? (0, null, "Uncertain") : (1, null, "Ready");
        if (!File.Exists(Path.Combine(directory, "prepared.json"))) return (0, null, "Uncertain");
        var outcomes = new[] { 2, 1 };
        foreach (var attempt in outcomes)
        {
            var marker = Path.Combine(directory, $"attempt-{attempt:000}.marker"); if (!File.Exists(marker)) continue;
            var resultPath = Path.Combine(directory, $"attempt-{attempt:000}.result.json");
            if (!File.Exists(resultPath)) return (0, null, "Uncertain");
            Outcome425 result;
            try { result = Wire425.Parse<Outcome425>(File.ReadAllText(resultPath, Wire425.Utf8)); }
            catch { return (0, null, "Uncertain"); }
            if (result.PreparedSha256 != Wire425.FileHash(Path.Combine(directory, "prepared.json"))) return (0, null, "Uncertain");
            if (result.State == "Accepted") return (0, null, "Accepted");
            if (result.State != "DefinitePreSendFailure" || result.SendStarted || attempt == 2 || mode != "ManualResume") return (0, null, result.State);
            var authorization = Path.Combine(directory, "manual-002.authorization.json"); if (!File.Exists(authorization)) return (0, null, "ManualAuthorizationRequired");
            var a = Wire425.Parse<RetryAuthorization425>(File.ReadAllText(authorization, Wire425.Utf8));
            Wire425.Require(a.AttemptNumber == 2 && a.WeekMonday == p.WeekMonday && a.EmployeeId == p.EmployeeId
                && a.PriorOutcomeSha256 == Wire425.FileHash(resultPath) && a.PreparedSha256 == Wire425.Hash(Wire425.Serialize(p))
                && a.Approval == "BMS425_MANUAL_RETRY_DEFINITE_PRESEND_ONLY" && a.ApprovedAtUtc >= result.AtUtc
                && !string.IsNullOrWhiteSpace(a.ApprovedBySid), "MANUAL_AUTHORIZATION_INVALID");
            return (2, Wire425.FileHash(authorization), "Ready");
        }
        return (0, null, "Uncertain");
    }
    internal void ClaimAttempt(Prepared425 p, int number, string hash, string? authorization)
    {
        Wire425.Require(number is 1 or 2, "ATTEMPT_BOUND");
        WriteNew(Path.Combine(DirectoryFor(p.WeekMonday, p.EmployeeId), $"attempt-{number:000}.marker"), Wire425.Serialize(new Attempt425(number, DateTimeOffset.UtcNow, hash, authorization)));
    }
    internal void Complete(Prepared425 p, int number, string state, bool sendStarted, string hash) =>
        AtomicNew(Path.Combine(DirectoryFor(p.WeekMonday, p.EmployeeId), $"attempt-{number:000}.result.json"), Wire425.Serialize(new Outcome425(number, state, DateTimeOffset.UtcNow, sendStarted, hash)));
}
