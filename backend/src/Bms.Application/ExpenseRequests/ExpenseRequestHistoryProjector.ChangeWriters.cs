using System.Globalization;
using System.Text.Json;
using Bms.Application.Abstractions;

namespace Bms.Application.ExpenseRequests;

/// The writers that append one change. A sensitive field reports that it
/// changed without publishing either value.
internal static partial class ExpenseRequestHistoryProjector
{

    private static void AddReferenceChange(
        List<ExpenseRequestHistoryChangeDto> changes,
        string section,
        string field,
        string? before,
        string? after,
        ref bool isPartial,
        IReadOnlyDictionary<Guid, string>? names = null)
    {
        before = NormalizeText(before);
        after = NormalizeText(after);
        if (string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (names is not null)
        {
            var beforeLabel = ReferenceName(before, names, out var beforeKnown);
            var afterLabel = ReferenceName(after, names, out var afterKnown);
            isPartial |= !beforeKnown || !afterKnown;
            // Two distinct records can legitimately have the same name.
            // Keep the change rather than accidentally suppressing it.
            if (before is not null && after is not null && beforeLabel == afterLabel)
            {
                beforeLabel += " (прежняя запись)";
                afterLabel += " (другая запись)";
            }
            AddDisplayChange(changes, section, field, beforeLabel, afterLabel);
            return;
        }

        isPartial = true;
        AddDisplayChange(
            changes,
            section,
            field,
            before is null ? null : "Выбрано",
            after is null ? null : before is null ? "Выбрано" : "Другое значение");
    }

    private static string? ReferenceName(string? id, IReadOnlyDictionary<Guid, string> names, out bool known)
    {
        known = id is null;
        if (id is null) return null;
        if (Guid.TryParse(id, out var key) && names.TryGetValue(key, out var name)
            && IsHistoryText(name, 3_900))
        {
            known = true;
            return name;
        }
        return "Наименование недоступно: запись не найдена в справочнике";
    }

    private static void AddSensitiveChange(
        List<ExpenseRequestHistoryChangeDto> changes,
        string section,
        string field,
        string? before,
        string? after,
        ref bool isPartial)
    {
        before = NormalizeText(before);
        after = NormalizeText(after);
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return;
        }

        isPartial = true;
        AddDisplayChange(
            changes,
            section,
            field,
            before is null ? null : "Значение задано",
            after is null ? null : before is null ? "Значение задано" : "Значение изменено");
    }

    private static void AddAmountChange(
        List<ExpenseRequestHistoryChangeDto> changes,
        string section,
        string field,
        decimal? before,
        decimal? after,
        string? beforeCurrency,
        string? afterCurrency)
    {
        if (!before.HasValue && !after.HasValue)
        {
            return;
        }

        if (before == after
            && string.Equals(
                NormalizeText(beforeCurrency),
                NormalizeText(afterCurrency),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AddDisplayChange(
            changes,
            section,
            field,
            FormatAmount(before, beforeCurrency),
            FormatAmount(after, afterCurrency));
    }

    private static void AddChange<T>(
        List<ExpenseRequestHistoryChangeDto> changes,
        string section,
        string field,
        T before,
        T after,
        Func<T, string?> format)
    {
        if (EqualityComparer<T>.Default.Equals(before, after))
        {
            return;
        }

        AddDisplayChange(changes, section, field, format(before), format(after));
    }

    private static void AddDisplayChange(
        List<ExpenseRequestHistoryChangeDto> changes,
        string section,
        string field,
        string? before,
        string? after)
    {
        var changeKind = before is null
            ? "added"
            : after is null
                ? "removed"
                : "changed";
        changes.Add(new ExpenseRequestHistoryChangeDto(
            section,
            field,
            changeKind,
            before,
            after));
    }
}
