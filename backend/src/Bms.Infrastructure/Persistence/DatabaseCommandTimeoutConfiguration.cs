using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Persistence;

/// Resolves the bounded command timeout used by every EF SQL Server operation.
/// A connection timeout controls opening a socket; this setting bounds the time
/// spent executing a query or command after the connection is already open.
public static class DatabaseCommandTimeoutConfiguration
{
    public const string ConfigurationKey = "Bms:Database:CommandTimeoutSeconds";
    public const int DefaultSeconds = 30;
    public const int MinimumSeconds = 1;
    public const int MaximumSeconds = 300;

    public static int Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var rawValue = configuration[ConfigurationKey];
        if (rawValue is null)
        {
            return DefaultSeconds;
        }

        if (!int.TryParse(
                rawValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seconds)
            || seconds is < MinimumSeconds or > MaximumSeconds)
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} must be an integer from {MinimumSeconds} to {MaximumSeconds} seconds.");
        }

        return seconds;
    }
}
