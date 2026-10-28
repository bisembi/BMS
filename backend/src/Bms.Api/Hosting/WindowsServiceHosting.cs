using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging.EventLog;

namespace Bms.Api.Hosting;

/// <summary>
/// Lets the one production host run either from a console or under the
/// Windows Service Control Manager. A console start is left exactly as it
/// was; installing or registering the service is not done here.
/// </summary>
internal static class WindowsServiceHosting
{
    internal const string ContentRootSwitch = "--contentRoot";

    /// <summary>
    /// A service process starts in the system directory, which would move the
    /// content root, and with it appsettings, away from the published files.
    /// The switch is placed first so an explicit operator value that follows
    /// still wins, because the last duplicate command-line key is the one read.
    /// </summary>
    internal static string[] PrepareArguments(string[] args) =>
        PrepareArguments(
            args,
            WindowsServiceHelpers.IsWindowsService(),
            AppContext.BaseDirectory);

    internal static string[] PrepareArguments(
        string[] args,
        bool isWindowsService,
        string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!isWindowsService)
        {
            return args;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        return [ContentRootSwitch, baseDirectory, .. args];
    }

    /// <summary>
    /// Registers the official service lifetime and event log provider. The
    /// framework checks the service context itself, so outside a real service
    /// process this adds nothing and the console lifetime stays in place.
    /// </summary>
    internal static void AddLifetime(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddWindowsService();
        // Provisioned by the protected deployment, not by the service account.
        // All other EventLog categories remain disabled in appsettings.
        if (OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService())
            services.Configure<EventLogSettings>(settings =>
            {
                if (OperatingSystem.IsWindows()) settings.SourceName = "BMS UI Diagnostics";
            });
    }
}
