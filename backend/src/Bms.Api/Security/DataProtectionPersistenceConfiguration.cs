using Microsoft.AspNetCore.DataProtection;

namespace Bms.Api.Security;

/// <summary>Opt-in Windows key persistence shared by successive release directories.</summary>
internal static class DataProtectionPersistenceConfiguration
{
    internal const string KeyPathKey = "BmsSecurity:DataProtection:KeyPath";
    internal const string ApplicationNameKey = "BmsSecurity:DataProtection:ApplicationName";

    internal static void Register(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var path = configuration[KeyPathKey];
        var name = configuration[ApplicationNameKey];
        if (path is null && name is null) return;
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(name)
            || path != path.Trim() || name != name.Trim()
            || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)
            || !Directory.Exists(path))
            throw new InvalidOperationException("Data protection requires an existing absolute local key directory and stable application name.");
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("The configured key protection requires Windows DPAPI.");

        // Directory permissions are provisioned by the operator for this
        // service only. Keys are encrypted at rest, never exported as secrets.
        services.AddDataProtection()
            .SetApplicationName(name)
            .PersistKeysToFileSystem(new DirectoryInfo(path))
            .ProtectKeysWithDpapi(protectToLocalMachine: true);
    }
}
