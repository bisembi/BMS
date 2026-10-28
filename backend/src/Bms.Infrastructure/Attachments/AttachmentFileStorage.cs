using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;
using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Attachments;

/// <summary>Immutable private files. Client names never become filesystem paths.
/// Rollback/abandonment deliberately retain files; no automatic deletion.</summary>
public sealed partial class AttachmentFileStorage(IConfiguration configuration)
{
    public string Root => Path.GetFullPath(configuration["BmsWeb:AttachmentStorageRoot"]
        ?? throw new ExpenseRequestAttachmentUnavailableException());
    public byte[] Read(AttachmentFileLocation location) =>
        string.Equals(location.Root, Root, StringComparison.OrdinalIgnoreCase)
            ? Read(location.Key, location.Size, location.Hash) : throw new ExpenseRequestAttachmentUnavailableException();
    public string Write(Guid id, byte[] content) =>
        WriteWithContentWriter421(id, content, static (stream, bytes) => stream.Write(bytes));

    // The internal seam permits a failed/partial write to be exercised without
    // replacing filesystem, quota, mutex or publication behavior in tests.
    internal string WriteWithContentWriter421(Guid id, byte[] content, Action<FileStream, byte[]> writeContent)
    {
        ArgumentNullException.ThrowIfNull(writeContent);
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var key = $"{id:N}.{hash}.bin";
        var path = Resolve(key);
        // One admission boundary for staged, bound and post-submission writes.
        // The named mutex also serializes separate host processes in the same
        // service session. Retained/orphaned bytes count even without a SQL row.
        var rootIdentity = Path.TrimEndingDirectorySeparator(Root).ToUpperInvariant();
        using var gate = new Mutex(false, "BmsAttachmentStorage-" +
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rootIdentity))));
        var acquired = false;
        try
        {
            try { acquired = gate.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new ExpenseRequestAttachmentUnavailableException();
            if (File.Exists(path))
            {
                _ = Read(key, content.LongLength, SHA256.HashData(content));
                return key;
            }
            EnsureStorageAdmission(content.LongLength);
            // Never expose the deterministic final name until all bytes have
            // been flushed and the writer closed. A failed attempt retains its
            // uniquely named pending file; admission counts it on the next try.
            // It cannot poison a retry of the same staged idempotency identity.
            var pendingPath = Path.Combine(Path.GetDirectoryName(path)!, $".{Guid.NewGuid():N}.pending421");
            using (var stream = new FileStream(pendingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                writeContent(stream, content);
                stream.Flush(flushToDisk: true);
            }
            // Revalidate the private root/final path after the IO. Promotion is
            // within the same directory and must never replace an existing file.
            if (!string.Equals(path, Resolve(key), StringComparison.OrdinalIgnoreCase))
                throw new ExpenseRequestAttachmentUnavailableException();
            File.Move(pendingPath, path, overwrite: false);
            return key;
        }
        catch (IOException exception) { throw new ExpenseRequestAttachmentUnavailableException(exception); }
        catch (UnauthorizedAccessException exception) { throw new ExpenseRequestAttachmentUnavailableException(exception); }
        finally { if (acquired) gate.ReleaseMutex(); }
    }

    private void EnsureStorageAdmission(long incomingBytes)
    {
        var maximumBytes = PositiveLimit("Bms:Attachments:Storage:MaximumRetainedBytes", 10L * 1024 * 1024 * 1024);
        var maximumFiles = PositiveLimit("Bms:Attachments:Storage:MaximumRetainedFiles", 100000);
        var minimumFreeBytes = PositiveLimit("Bms:Attachments:Storage:MinimumFreeBytes", 1024L * 1024 * 1024);
        long retainedBytes = 0, retainedFiles = 0;
        foreach (var file in new DirectoryInfo(Root).EnumerateFiles())
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ExpenseRequestAttachmentUnavailableException();
            retainedFiles++;
            if (retainedFiles >= maximumFiles || file.Length > maximumBytes - retainedBytes)
                throw new Bms.Domain.Common.DomainRuleViolationException("Retained attachment storage quota exceeded.");
            retainedBytes += file.Length;
        }
        if (incomingBytes <= 0 || incomingBytes > maximumBytes - retainedBytes)
            throw new Bms.Domain.Common.DomainRuleViolationException("Retained attachment storage quota exceeded.");
        var availableBytes = new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace;
        if (availableBytes < minimumFreeBytes || incomingBytes > availableBytes - minimumFreeBytes)
            throw new ExpenseRequestAttachmentUnavailableException();
    }

    private long PositiveLimit(string name, long fallback)
    {
        var configured = configuration[name];
        if (configured is null) return fallback;
        if (!long.TryParse(configured, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var limit) || limit <= 0)
            throw new ExpenseRequestAttachmentUnavailableException();
        return limit;
    }

    public byte[] Read(string key, long size, byte[] hash)
    {
        try
        {
            var path = Resolve(key);
            var file = new FileInfo(path);
            if (size <= 0 || size > ExpenseRequestAttachmentUploadPolicy.MaximumFileSizeBytes
                || !file.Exists || file.Length != size || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ExpenseRequestAttachmentUnavailableException();
            var bytes = File.ReadAllBytes(path);
            if (bytes.LongLength != size || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), hash))
                throw new ExpenseRequestAttachmentUnavailableException();
            return bytes;
        }
        catch (IOException exception) { throw new ExpenseRequestAttachmentUnavailableException(exception); }
        catch (UnauthorizedAccessException exception) { throw new ExpenseRequestAttachmentUnavailableException(exception); }
    }

    public async Task<Stream> OpenVerifiedRead415Async(AttachmentFileLocation location, CancellationToken token)
    {
        FileStream? stream = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!string.Equals(location.Root, Root, StringComparison.OrdinalIgnoreCase)
                || location.Size <= 0 || location.Size > ExpenseRequestAttachmentUploadPolicy.MaximumFileSizeBytes
                || location.Hash.Length != 32) throw new ExpenseRequestAttachmentUnavailableException();
            var path = Resolve(location.Key);
            // The same verified handle is sent. Deny writers/deletion until the
            // response is finished, not just while hashing the original file.
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length != location.Size || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new ExpenseRequestAttachmentUnavailableException();
            var actual = await SHA256.HashDataAsync(stream, token);
            if (!CryptographicOperations.FixedTimeEquals(actual, location.Hash))
                throw new ExpenseRequestAttachmentUnavailableException();
            token.ThrowIfCancellationRequested();
            stream.Position = 0;
            var owned = stream; stream = null;
            return owned;
        }
        catch (IOException exception) { throw new ExpenseRequestAttachmentUnavailableException(exception); }
        catch (UnauthorizedAccessException exception) { throw new ExpenseRequestAttachmentUnavailableException(exception); }
        finally { if (stream is not null) await stream.DisposeAsync(); }
    }

    private string Resolve(string key)
    {
        var root = configuration["BmsWeb:AttachmentStorageRoot"];
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)
            || !StorageKeyPattern().IsMatch(key)) throw new ExpenseRequestAttachmentUnavailableException();
        var directory = new DirectoryInfo(Path.GetFullPath(root));
        if (!directory.Exists) throw new ExpenseRequestAttachmentUnavailableException();
        for (DirectoryInfo? node = directory; node is not null; node = node.Parent)
            if ((node.Attributes & FileAttributes.ReparsePoint) != 0) throw new ExpenseRequestAttachmentUnavailableException();
        var path = Path.Combine(directory.FullName, key);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ExpenseRequestAttachmentUnavailableException();
        return path;
    }

    [GeneratedRegex("\\A[0-9a-f]{32}\\.[0-9a-f]{64}\\.bin\\z", RegexOptions.CultureInvariant)]
    private static partial Regex StorageKeyPattern();
}
public sealed record AttachmentFileLocation(string Root, string Key, long Size, byte[] Hash);
