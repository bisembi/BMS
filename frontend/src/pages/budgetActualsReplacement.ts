import type { BudgetActualsPreview } from '../api/budgetActuals';
import type { BudgetFlowDirection } from '../api/budgetInstances';
export type BudgetActualsDraftLine = {
    cashFlowItemId: string;
    budgetMonth: string;
    limitAmountKzt: string;
    comment: string;
    flowDirection: BudgetFlowDirection;
};
/** Pure form-only operation. It never saves, approves, changes dates or adds fact to previous amounts. */
export function replaceBudgetActualsSlice(lines: readonly BudgetActualsDraftLine[], preview: BudgetActualsPreview): {
    lines: BudgetActualsDraftLine[];
    zeroedRows: number;
} {
    const selected = new Set(preview.cashFlowItemIds.map(id => id.toLowerCase()));
    const key = (id: string, month: string) => `${id.toLowerCase()}\u0000${month}`;
    const facts = new Map(preview.lines.map(row => [key(row.cashFlowItemId, row.budgetMonth), row]));
    const handled = new Set<string>();
    let zeroedRows = 0;
    const result = lines.map(line => {
        if (!selected.has(line.cashFlowItemId.toLowerCase()) || line.budgetMonth < preview.fromMonth || line.budgetMonth > preview.toMonth)
            return { ...line };
        const fact = facts.get(key(line.cashFlowItemId, line.budgetMonth));
        if (!fact)
            throw new Error('Неполный выбранный факт. Ввод не изменён.');
        const amount = line.flowDirection === fact.flowDirection && !handled.has(key(line.cashFlowItemId, line.budgetMonth))
            ? fact.limitAmountKzt : 0;
        if (amount === 0 && Number(line.limitAmountKzt.replace(/\s/gu, '').replace(',', '.')) > 0)
            zeroedRows += 1;
        if (line.flowDirection === fact.flowDirection || fact.limitAmountKzt === 0)
            handled.add(key(line.cashFlowItemId, line.budgetMonth));
        return { ...line, limitAmountKzt: amount.toFixed(2) };
    });
    for (const fact of preview.lines)
        if (!handled.has(key(fact.cashFlowItemId, fact.budgetMonth)))
            result.push({
                cashFlowItemId: fact.cashFlowItemId, budgetMonth: fact.budgetMonth,
                limitAmountKzt: fact.limitAmountKzt.toFixed(2), comment: '', flowDirection: fact.flowDirection,
            });
    // Sort the complete form, not just the API appendices. Keep duplicate operations
    // stable within a month; incomplete manual rows are retained verbatim at the end.
    const canonicalMonth = (value: string) => /^(?!0000)\d{4}-(0[1-9]|1[0-2])-01$/u.test(value);
    result.sort((left, right) => {
        const leftValid = canonicalMonth(left.budgetMonth), rightValid = canonicalMonth(right.budgetMonth);
        if (leftValid !== rightValid)
            return leftValid ? -1 : 1;
        return leftValid ? left.budgetMonth.localeCompare(right.budgetMonth) : 0;
    });
    return { lines: result, zeroedRows };
}
