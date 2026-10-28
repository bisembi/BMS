namespace Bms.Application.Budgeting;

/// <summary>The first imported year is a historical lower bound, not an end date.
/// Keep old payments in the capture: dropping them at New Year can reverse paid links.
/// Cardinality limits remain independent from the calendar year.</summary>
public static class AccountingPeriod467
{
    public const int FirstYear = 2026;
    public const int MaximumMonths = 900;
    public const int MaximumCells = 240_000;
    public static bool IsSourceYear(int year) => year is >= FirstYear and <= 2100;
    public static bool IsReportYear(int year) => year == 2025 || IsSourceYear(year);
    public static bool IsMonth(DateOnly month) => month.Day == 1 && IsSourceYear(month.Year);
    public static IReadOnlyList<DateOnly> CoveredMonths(DateOnly cutoff)
    {
        if (cutoff <= new DateOnly(FirstYear, 1, 1) || cutoff > new DateOnly(2101, 1, 1))
            throw new CashFlowActualsSourceInvalidException();
        var result = new List<DateOnly>();
        for (var month = new DateOnly(FirstYear, 1, 1); month < cutoff; month = month.AddMonths(1))
        {
            if (result.Count >= MaximumMonths) throw new CashFlowActualsSourceInvalidException();
            result.Add(month);
        }
        return result;
    }
}
