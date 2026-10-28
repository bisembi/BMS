namespace Bms.Domain.Accruals;

public enum AccrualAgeZone
{
    None,
    Yellow,
    Orange,
    Red
}

/// <summary>Immutable configured boundaries; the algorithm has no default ages.</summary>
public sealed record AccrualAgeThresholds
{
    public AccrualAgeThresholds(int yellowFromDays, int orangeFromDays, int redFromDays)
    {
        if (yellowFromDays <= 0
            || orangeFromDays <= yellowFromDays
            || redFromDays <= orangeFromDays)
        {
            throw new ArgumentException("Accrual age thresholds must be positive and strictly increasing.");
        }

        YellowFromDays = yellowFromDays;
        OrangeFromDays = orangeFromDays;
        RedFromDays = redFromDays;
    }

    public int YellowFromDays { get; }

    public int OrangeFromDays { get; }

    public int RedFromDays { get; }

    public AccrualAgeZone Classify(int ageDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ageDays);
        return ageDays >= RedFromDays ? AccrualAgeZone.Red
            : ageDays >= OrangeFromDays ? AccrualAgeZone.Orange
            : ageDays >= YellowFromDays ? AccrualAgeZone.Yellow
            : AccrualAgeZone.None;
    }
}
