using System.Text;

namespace Bms.Infrastructure.Health;

internal static class SqlServerDatabaseReadinessCommand
{
    internal const string ResourceName =
        "Bms.Infrastructure.Health.SqlServerDatabaseReadinessProbe.sql";

    internal static string Text { get; } = Load();

    private static string Load()
    {
        var assembly = typeof(SqlServerDatabaseReadinessCommand).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded database readiness command is missing: {ResourceName}.");
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        var resourceText = reader.ReadToEnd().ReplaceLineEndings("\n");
        if (!resourceText.EndsWith('\n')
            || resourceText.EndsWith("\n\n", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Embedded database readiness command has an invalid ending: {ResourceName}.");
        }

        var commandText = resourceText[..^1];
        if (string.IsNullOrWhiteSpace(commandText))
        {
            throw new InvalidOperationException(
                $"Embedded database readiness command is empty: {ResourceName}.");
        }

        return commandText;
    }
}
