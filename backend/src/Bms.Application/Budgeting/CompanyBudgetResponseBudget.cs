using System.Text;
using System.Text.Json;
using Bms.Domain.Common;

namespace Bms.Application.Budgeting;

public static class CompanyBudgetResponseBudget
{
    public const long MaximumUtf8Bytes = 32L * 1024 * 1024;
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static DomainRuleViolationException Exceeded() =>
        new("Ответ состава версии превышает технический предел 32 МиБ; сократите состав.");

    // Count the actual escaped Web JSON bytes without allocating the JSON
    // string. The stream stops at the first over-budget write. Used before
    // response serialization and before audit/outbox/idempotency persistence.
    public static long Validate<T>(T response, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        using var counter = new CountingStream(token);
        JsonSerializer.Serialize(counter, response, WebJson);
        return counter.Length;
    }
    public static void ValidateStoredJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumUtf8Bytes) throw Exceeded();
    }

    private sealed class CountingStream(CancellationToken token) : Stream
    {
        private long count;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => count;
        public override long Position { get => count; set => throw new NotSupportedException(); }
        public override void Flush() => token.ThrowIfCancellationRequested();
        public override int Read(byte[] buffer, int offset, int length) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int length) => Write(buffer.AsSpan(offset, length));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            token.ThrowIfCancellationRequested();
            if (buffer.Length > MaximumUtf8Bytes - count) throw Exceeded();
            count += buffer.Length;
        }
    }
}
