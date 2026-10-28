using System.Globalization;
using System.Text.Json;
using Bms.Application.Abstractions;

namespace Bms.Application.ExpenseRequests;

/// Match stable line identities; use position only for ambiguous legacy IDs.
internal static partial class ExpenseRequestHistoryProjector
{

    private static void AddLineChanges(
        List<ExpenseRequestHistoryChangeDto> changes,
        IReadOnlyList<AuditLineSnapshot> beforeLines,
        IReadOnlyList<AuditLineSnapshot> afterLines,
        string? beforeCurrency,
        string? afterCurrency,
        ref bool isPartial,
        ExpenseRequestHistoryReferences? references)
    {
        var beforeIdCounts = beforeLines
            .Where(line => !string.IsNullOrWhiteSpace(line.Id))
            .GroupBy(line => line.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var afterIdCounts = afterLines
            .Where(line => !string.IsNullOrWhiteSpace(line.Id))
            .GroupBy(line => line.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var beforeIndexById = beforeLines
            .Select((line, index) => (line, index))
            .Where(item => !string.IsNullOrWhiteSpace(item.line.Id)
                && beforeIdCounts[item.line.Id!] == 1)
            .ToDictionary(item => item.line.Id!, item => item.index, StringComparer.OrdinalIgnoreCase);
        var usedBeforeIndexes = new HashSet<int>();

        for (var afterIndex = 0; afterIndex < afterLines.Count; afterIndex++)
        {
            var afterLine = afterLines[afterIndex];
            var beforeIndex = -1;
            if (!string.IsNullOrWhiteSpace(afterLine.Id)
                && afterIdCounts[afterLine.Id!] == 1
                && beforeIndexById.TryGetValue(afterLine.Id!, out var matchedIndex))
            {
                beforeIndex = matchedIndex;
            }
            else if (afterIndex < beforeLines.Count && !usedBeforeIndexes.Contains(afterIndex)
                && (string.IsNullOrWhiteSpace(afterLine.Id) || afterIdCounts.GetValueOrDefault(afterLine.Id ?? string.Empty) != 1
                    || string.IsNullOrWhiteSpace(beforeLines[afterIndex].Id)
                    || beforeIdCounts.GetValueOrDefault(beforeLines[afterIndex].Id ?? string.Empty) != 1))
            {
                beforeIndex = afterIndex;
                isPartial = true;
            }

            if (beforeIndex >= 0)
            {
                usedBeforeIndexes.Add(beforeIndex);
                AddLineFieldChanges(
                    changes,
                    $"Строка {afterIndex + 1}",
                    beforeLines[beforeIndex],
                    afterLine,
                    beforeCurrency,
                    afterCurrency,
                    ref isPartial,
                    references);
            }
            else
            {
                AddDisplayChange(
                    changes,
                    LinesSection,
                    $"Строка {afterIndex + 1}",
                    null,
                    "Добавлена");
                AddLineFieldChanges(
                    changes,
                    $"Строка {afterIndex + 1}",
                    null,
                    afterLine,
                    beforeCurrency,
                    afterCurrency,
                    ref isPartial,
                    references);
            }
        }

        for (var beforeIndex = 0; beforeIndex < beforeLines.Count; beforeIndex++)
        {
            if (usedBeforeIndexes.Contains(beforeIndex))
            {
                continue;
            }

            AddDisplayChange(
                changes,
                LinesSection,
                $"Строка {beforeIndex + 1}",
                "Была в заявке",
                null);
            AddLineFieldChanges(
                changes,
                $"Строка {beforeIndex + 1}",
                beforeLines[beforeIndex],
                null,
                beforeCurrency,
                afterCurrency,
                ref isPartial,
                references);
        }
    }

    private static void AddLineFieldChanges(
        List<ExpenseRequestHistoryChangeDto> changes,
        string lineLabel,
        AuditLineSnapshot? before,
        AuditLineSnapshot? after,
        string? beforeCurrency,
        string? afterCurrency,
        ref bool isPartial,
        ExpenseRequestHistoryReferences? references)
    {
        AddReferenceChange(changes, LinesSection, $"{lineLabel} — Статья ДДС",
            before?.CashFlowItemId, after?.CashFlowItemId, ref isPartial, references?.CashFlowItems);
        AddReferenceChange(changes, LinesSection, $"{lineLabel} — Подразделение затрат",
            before?.AccrualCostCenterId ?? before?.CostCenterId,
            after?.AccrualCostCenterId ?? after?.CostCenterId, ref isPartial, references?.CostCenters);
        AddChange(changes, LinesSection, $"{lineLabel} — Период начисления",
            before?.AccrualPeriod, after?.AccrualPeriod, FormatMonth);
        AddAmountChange(changes, LinesSection, $"{lineLabel} — Сумма с НДС",
            before?.Amount, after?.Amount, beforeCurrency, afterCurrency);
        AddChange(changes, LinesSection, $"{lineLabel} — Ставка НДС",
            NormalizeText(before?.VatRateCode), NormalizeText(after?.VatRateCode), FormatVatRate);
        AddAmountChange(changes, LinesSection, $"{lineLabel} — Сумма НДС",
            before?.VatAmount, after?.VatAmount, beforeCurrency, afterCurrency);
        AddAmountChange(changes, LinesSection, $"{lineLabel} — Сумма без НДС",
            before?.AmountExcludingVat, after?.AmountExcludingVat, beforeCurrency, afterCurrency);
        AddAmountChange(changes, LinesSection, $"{lineLabel} — Эквивалент KZT",
            before?.AmountKzt, after?.AmountKzt, "KZT", "KZT");
    }
}
