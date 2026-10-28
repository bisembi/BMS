using System.Globalization;
using System.Text;
using Bms.Domain.Common;

namespace Bms.Domain.Budgeting;

/// <summary>One permanent, non-yearly sequence per cost center; allocation is transactional.</summary>
public static class BudgetDocumentNumber
{
    public static string CanonicalizePrefix(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new DomainRuleViolationException("A budget abbreviation is required.");
        }

        string canonical;
        try
        {
            canonical = value.Normalize(NormalizationForm.FormC).ToUpperInvariant().Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            throw new DomainRuleViolationException("A budget abbreviation must contain valid Unicode letters.");
        }

        var letters = 0;
        foreach (var rune in canonical.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune))
            {
                throw new DomainRuleViolationException("A budget abbreviation must contain only letters.");
            }
            letters++;
        }

        if (letters is < 2 or > 3 || canonical.Length > 6)
        {
            throw new DomainRuleViolationException("A budget abbreviation must contain two or three letters.");
        }

        return canonical;
    }

    public static void RequireCanonicalPrefix(string value)
    {
        if (!string.Equals(value, CanonicalizePrefix(value), StringComparison.Ordinal))
        {
            // Stored values must be inspected, not silently repaired while allocating.
            throw new DomainRuleViolationException("The saved budget abbreviation is not canonical.");
        }
    }

    public static bool UsesPrefix(string historicalNumber, string canonicalPrefix)
    {
        RequireCanonicalPrefix(canonicalPrefix);
        var separator = historicalNumber.IndexOf('-', StringComparison.Ordinal);
        if (separator < 1) { return false; }
        var prefix = historicalNumber[..separator];
        if (prefix.Length == 6 && prefix.All(character => character is >= '0' and <= '9'))
        {
            // Date-first numbers reserve their second segment. The historical
            // suffix may remain opaque; prefix ownership must not be weakened
            // merely because a legacy number is malformed.
            var suffixSeparator = historicalNumber.IndexOf('-', separator + 1);
            if (suffixSeparator < 0) { return false; }
            prefix = historicalNumber[(separator + 1)..suffixSeparator];
        }
        try
        {
            // Presence reserves the prefix even with an opaque/malformed suffix.
            // It never establishes an ordinal or rewrites the historic number.
            return string.Equals(CanonicalizePrefix(prefix), canonicalPrefix, StringComparison.Ordinal);
        }
        catch (DomainRuleViolationException) { return false; }
    }

    public static string Format(DateOnly documentDate, string canonicalPrefix, long ordinal)
    {
        RequireCanonicalPrefix(canonicalPrefix);
        if (documentDate == default || ordinal <= 0)
        {
            throw new DomainRuleViolationException("A document date and positive budget ordinal are required.");
        }
        // D2 is a minimum width, not a two-digit limit: 99 advances to 100.
        return $"{documentDate.ToString("ddMMyy", CultureInfo.InvariantCulture)}-{canonicalPrefix}-{ordinal.ToString("D2", CultureInfo.InvariantCulture)}";
    }

    public static DateOnly CreationDate(DateTimeOffset createdAtUtc)
    {
        if (createdAtUtc == default || createdAtUtc.Offset != TimeSpan.Zero)
            throw new DomainRuleViolationException("The budget creation timestamp must be UTC.");
        return DateOnly.FromDateTime(createdAtUtc.ToOffset(TimeSpan.FromHours(5)).DateTime);
    }

    public static (string Number, long Ordinal) Next(DateOnly documentDate, string canonicalPrefix, long lastIssuedOrdinal)
    {
        var next = Next(canonicalPrefix, lastIssuedOrdinal).Ordinal;
        return (Format(documentDate, canonicalPrefix, next), next);
    }

    public static (string Number, long Ordinal) Next(string canonicalPrefix, long lastIssuedOrdinal)
    {
        RequireCanonicalPrefix(canonicalPrefix);
        if (lastIssuedOrdinal < 0 || lastIssuedOrdinal == long.MaxValue)
        {
            throw new DomainRuleViolationException("The budget number sequence cannot advance.");
        }

        var next = checked(lastIssuedOrdinal + 1);
        return ($"{canonicalPrefix}-{next.ToString("D4", CultureInfo.InvariantCulture)}", next);
    }
}
