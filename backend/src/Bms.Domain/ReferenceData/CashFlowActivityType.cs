namespace Bms.Domain.ReferenceData;

/// <summary>
/// The three business classifications available for a cash-flow item.
/// Stable values are persisted; the UI supplies the localized labels.
/// </summary>
public static class CashFlowActivityType
{
    public const string Operating = "Operating";
    public const string Investing = "Investing";
    public const string Financing = "Financing";

    public static bool IsDefined(string? value) => value is Operating or Investing or Financing;
}
