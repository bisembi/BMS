namespace Bms.Api.Diagnostics;

/// <summary>
/// Defines the single server-owned trace-identifier boundary used by API
/// responses and redacted operational logs.
/// </summary>
internal static class ApiTraceIdentifier
{
    internal const int MaximumLength = 128;
    internal const string Unavailable = "unavailable";

    internal static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumLength &&
        !value.Any(char.IsControl);

    internal static string Canonicalize(string? value) =>
        IsCanonical(value) ? value! : Unavailable;

    internal static string RequireCanonical(string? value, string parameterName)
    {
        if (!IsCanonical(value))
        {
            throw new ArgumentException(
                "A canonical server trace identifier is required.",
                parameterName);
        }

        return value!;
    }
}
