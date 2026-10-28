import type { ReferenceDataItem } from '../types';
import type { BudgetDraftLine } from './budgetInstanceMatrix';
import { canonicalMoneyText } from '../utils/moneyInput';
export type BudgetLineSortColumn = 0 | 1 | 2 | 3 | 4;
export type BudgetLineSort = {
    column: BudgetLineSortColumn;
    direction: 'ascending' | 'descending';
};
const collator = new Intl.Collator('ru-RU', { numeric: true, sensitivity: 'base' });
const identifier = (value: string) => value.toLowerCase();
function cents(value: string): bigint | null {
    const text = canonicalMoneyText(value);
    if (text === null || !/^\d+(?:\.\d{1,2})?$/u.test(text))
        return null;
    const [whole, fraction = ''] = text.split('.');
    return BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
}
/** Pure display projection. Source indices and original array are never changed. */
export function sortBudgetLines<T extends BudgetDraftLine>(lines: readonly T[], items: readonly ReferenceDataItem[], sort: BudgetLineSort | null): {
    line: T;
    sourceIndex: number;
}[] {
    const indexed = lines.map((line, sourceIndex) => ({ line, sourceIndex }));
    if (!sort)
        return indexed;
    const catalog = new Map(items.map(item => [identifier(item.id), item]));
    const value = (line: T): string | bigint | null => {
        switch (sort.column) {
            case 0: return line.comment.trim() || null;
            case 1: {
                if (!line.cashFlowItemId)
                    return null;
                const item = catalog.get(identifier(line.cashFlowItemId));
                if (!item)
                    return 'Выбранная статья ДДС недоступна в справочнике';
                const code = item.code.trim();
                return /^CF-/iu.test(code) || !code ? item.name : `${code} — ${item.name}`;
            }
            case 2: return !line.cashFlowItemId ? null : line.flowDirection === 'Receipt' ? 'Поступление' : 'Выплата';
            case 3: return /^(?!0000)\d{4}-(0[1-9]|1[0-2])-01$/u.test(line.budgetMonth) ? line.budgetMonth : null;
            case 4: return cents(line.limitAmountKzt);
        }
    };
    const values = lines.map(value);
    return indexed.sort((left, right) => {
        const a = values[left.sourceIndex], b = values[right.sourceIndex];
        // Blank/invalid values stay last in both directions; they are not monetary zero.
        if (a === null || b === null)
            return a === b ? left.sourceIndex - right.sourceIndex : a === null ? 1 : -1;
        const compared = typeof a === 'bigint' && typeof b === 'bigint' ? (a < b ? -1 : a > b ? 1 : 0)
            : collator.compare(String(a), String(b));
        return compared ? compared * (sort.direction === 'ascending' ? 1 : -1) : left.sourceIndex - right.sourceIndex;
    });
}
