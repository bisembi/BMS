using Bms.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Attachments;

internal static class AttachmentScannerConfiguration
{
    private const string ModeKey = "Bms:Attachments:Scanner:Mode";
    private const string CommandPathKey = "Bms:Attachments:Scanner:CommandPath";
    private const string TimeoutSecondsKey = "Bms:Attachments:Scanner:TimeoutSeconds";

    internal static IExpenseRequestAttachmentContentScanner Resolve(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var mode = configuration[ModeKey]?.Trim();
        if (string.IsNullOrEmpty(mode)
            || string.Equals(mode, "Unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return UnavailableAttachmentContentScanner.Instance;
        }

        if (!string.Equals(mode, "WindowsDefender", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Bms attachment scanner mode is invalid.");
        }

        var path = configuration[CommandPathKey];
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                "Windows Defender attachment scanning requires CommandPath.");
        }

        var timeoutSeconds = 120;
        var configuredTimeout = configuration[TimeoutSecondsKey];
        if (!string.IsNullOrWhiteSpace(configuredTimeout)
            && (!int.TryParse(configuredTimeout, out timeoutSeconds)
                || timeoutSeconds is < 1 or > 600))
        {
            throw new InvalidOperationException(
                "Attachment scanner timeout must be between 1 and 600 seconds.");
        }

        return new BoundedAttachmentContentScanner412(new WindowsDefenderAttachmentContentScanner(
            path,
            TimeSpan.FromSeconds(timeoutSeconds)));
    }
}
