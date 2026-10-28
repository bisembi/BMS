namespace Bms.Domain.Common;

/// <summary>
/// Canonical BMS monetary storage contract: decimal(18,2).
/// Values are validated, never rounded implicitly.
/// </summary>
public static class MoneyAmountPolicy
{
    public const int Precision = 18;
    public const int Scale = 2;
    public const decimal Maximum = 9_999_999_999_999_999.99m;

    public static bool IsRepresentable(decimal amount) =>
        amount >= -Maximum &&
        amount <= Maximum &&
        decimal.Round(amount, Scale, MidpointRounding.ToEven) == amount;

    public static void EnsurePositive(decimal amount, string fieldName)
    {
        if (amount <= 0m)
        {
            throw new DomainRuleViolationException($"{fieldName} must be greater than zero.");
        }

        EnsureRepresentable(amount, fieldName);
    }

    public static void EnsureNonNegative(decimal? amount, string fieldName)
    {
        if (!amount.HasValue)
        {
            return;
        }

        if (amount.Value < 0m)
        {
            throw new DomainRuleViolationException($"{fieldName} cannot be negative.");
        }

        EnsureRepresentable(amount.Value, fieldName);
    }

    public static void EnsureRepresentable(decimal amount, string fieldName)
    {
        if (decimal.Round(amount, Scale, MidpointRounding.ToEven) != amount)
        {
            throw new DomainRuleViolationException(
                $"{fieldName} cannot have more than two decimal places.");
        }

        if (amount < -Maximum || amount > Maximum)
        {
            throw new DomainRuleViolationException(
                $"{fieldName} exceeds decimal(18,2) storage range.");
        }
    }
}
