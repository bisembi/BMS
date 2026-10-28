using System.Globalization;
using System.Text;
using Bms.Domain.Common;

namespace Bms.Domain.ExpenseRequests;

/// <summary>
/// Immutable business number for a расходная заявка. The ordinal is permanent
/// for a CFO after the surrounding business transaction commits; a rolled-back
/// transaction does not consume a number.
/// </summary>
public static class ExpenseRequestDocumentNumber
{
    public static string CanonicalizePrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleViolationException("A CFO code is required for an expense-request number.");
        }

        string canonical;
        try
        {
            canonical = value.Trim().Normalize(NormalizationForm.FormC)
                .ToUpperInvariant().Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            throw new DomainRuleViolationException("A CFO code must contain valid Unicode characters.");
        }

        var previousWasSeparator = false;
        Rune? firstRune = null;
        var runeCount = 0;
        foreach (var rune in canonical.EnumerateRunes())
        {
            runeCount++;
            firstRune ??= rune;
            if (Rune.IsLetterOrDigit(rune))
            {
                previousWasSeparator = false;
                continue;
            }

            if (rune.Value == '-' && runeCount > 1 && !previousWasSeparator)
            {
                previousWasSeparator = true;
                continue;
            }

            throw new DomainRuleViolationException(
                "A CFO code must contain only letters, digits and single separators '-'.");
        }

        if (canonical.Length > 100 || runeCount == 0 || previousWasSeparator
            || firstRune is null
            || !Rune.IsLetter(firstRune.Value))
        {
            throw new DomainRuleViolationException("A CFO code is invalid for an expense-request number.");
        }

        return canonical;
    }

    public static void RequireCanonicalPrefix(string value)
    {
        if (!string.Equals(value, CanonicalizePrefix(value), StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException("The saved CFO code is not canonical.");
        }
    }

    public static (string Number, long Ordinal) Next(string canonicalPrefix, long lastIssuedOrdinal)
    {
        RequireCanonicalPrefix(canonicalPrefix);
        if (lastIssuedOrdinal < 0 || lastIssuedOrdinal == long.MaxValue)
        {
            throw new DomainRuleViolationException("The expense-request number sequence cannot advance.");
        }

        var next = checked(lastIssuedOrdinal + 1);
        return ($"{canonicalPrefix}-{next.ToString("D5", CultureInfo.InvariantCulture)}", next);
    }
}
