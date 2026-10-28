using System.Diagnostics;
using Bms.Application.Abstractions;

namespace Bms.Infrastructure.Attachments;

/// <summary>
/// Scans one exact temporary copy with Microsoft Defender and never persists
/// the file when the scanner is missing, times out or returns a non-zero code.
/// </summary>
internal sealed class WindowsDefenderAttachmentContentScanner(
    string commandPath,
    TimeSpan timeout) : IExpenseRequestAttachmentContentScanner
{
    // MpCmdRun's default exit code 2 combines an unremediated threat with a
    // scanner failure. -ReturnHR preserves the actual result so an unavailable
    // provider is not exposed as a content-policy rejection. These are the
    // documented Defender results that still identify unsafe content or an
    // action required before it can be accepted. Every other non-zero result
    // is an unavailable scanner and remains fail-closed.
    private const int RemoveFailed = unchecked((int)0x80508017u);
    private const int QuarantineFailed = unchecked((int)0x80508021u);
    private const int RebootRequired = unchecked((int)0x80508022u);
    private const int FullScanRequired = unchecked((int)0x80508024u);
    private const int ManualStepsRequired = unchecked((int)0x80508025u);
    private const int RemoveNotSupported = unchecked((int)0x80508026u);
    private const int LowMediumRemovalDisabled = unchecked((int)0x80508027u);
    private const int RescanRequired = unchecked((int)0x80508029u);
    private const int OfflineScanRequired = unchecked((int)0x80508030u);

    private readonly string commandPath = RequireCommandPath(commandPath);
    private readonly TimeSpan timeout = timeout > TimeSpan.Zero
        ? timeout
        : throw new ArgumentOutOfRangeException(nameof(timeout));

    public async Task<ExpenseRequestAttachmentScanVerdict> ScanAsync(
        string fileName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName) || content.IsEmpty)
        {
            return ExpenseRequestAttachmentScanVerdict.Rejected;
        }

        var scanDirectory = Path.Combine(Path.GetTempPath(), "bms-web-attachment-scan");
        Directory.CreateDirectory(scanDirectory);
        var extension = Path.GetExtension(fileName);
        var scanPath = Path.Combine(scanDirectory, $"{Guid.NewGuid():N}{extension}");
        Process? process = null;
        var verdict = ExpenseRequestAttachmentScanVerdict.Unavailable;
        var cleanupSucceeded = false;
        try
        {
            await File.WriteAllBytesAsync(scanPath, content.ToArray(), cancellationToken);
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = commandPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.StartInfo.ArgumentList.Add("-Scan");
            process.StartInfo.ArgumentList.Add("-ScanType");
            process.StartInfo.ArgumentList.Add("3");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(scanPath);
            process.StartInfo.ArgumentList.Add("-DisableRemediation");
            process.StartInfo.ArgumentList.Add("-ReturnHR");

            if (process.Start())
            {
                var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var error = process.StandardError.ReadToEndAsync(cancellationToken);
                try
                {
                    await process
                        .WaitForExitAsync(cancellationToken)
                        .WaitAsync(timeout, cancellationToken);
                    _ = await output;
                    _ = await error;
                    verdict = ClassifyExitCode(process.ExitCode);
                }
                catch (TimeoutException)
                {
                    TryKill(process);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ExpenseRequestAttachmentScanVerdict.Unavailable;
        }
        finally
        {
            if (process is not null)
            {
                TryKill(process);
                process.Dispose();
            }

            cleanupSucceeded = TryDeleteTemporaryFile(scanPath);
        }

        // A successful scan is not enough if the temporary business document
        // could not be removed. Keep persistence fail-closed in that case.
        return cleanupSucceeded
            ? verdict
            : ExpenseRequestAttachmentScanVerdict.Unavailable;
    }

    internal static ExpenseRequestAttachmentScanVerdict ClassifyExitCode(int exitCode) =>
        exitCode switch
        {
            0 => ExpenseRequestAttachmentScanVerdict.Accepted,
            RemoveFailed
                or QuarantineFailed
                or RebootRequired
                or FullScanRequired
                or ManualStepsRequired
                or RemoveNotSupported
                or LowMediumRemovalDisabled
                or RescanRequired
                or OfflineScanRequired => ExpenseRequestAttachmentScanVerdict.Rejected,
            _ => ExpenseRequestAttachmentScanVerdict.Unavailable
        };

    private static string RequireCommandPath(string value)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value ?? string.Empty);
        if (!Path.IsPathFullyQualified(expanded) || !File.Exists(expanded))
        {
            throw new InvalidOperationException(
                "The configured attachment scanner command does not exist.");
        }

        return expanded;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The caller already receives an unavailable verdict.
        }
    }

    private static bool TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
            return !File.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
