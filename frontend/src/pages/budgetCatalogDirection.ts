import type { ReferenceDataItem } from '../types';
import type { BudgetDraftLine } from './budgetInstanceMatrix';
import { BudgetMatrixInputError, signedBudgetCents, replaceBudgetMatrixCell, budgetMatrixAmount } from './budgetInstanceMatrix';
import { canonicalMoneyText } from '../utils/moneyInput';
export function selectedBudgetDirection(items: readonly ReferenceDataItem[], id: string) {
    const item = items.find(row => row.id.toLowerCase() === id.toLowerCase());
    return item?.flowDirection === 'Payment' || item?.flowDirection === 'Receipt' ? item.flowDirection : null;
}
/** Catalog defaults apply to manual creation, never to old rows or copied actuals. */
export function manualBudgetCellText(lines: readonly BudgetDraftLine[], item: ReferenceDataItem, month: string, text: string, origin: 'manual' | 'paste' = 'manual') {
    const canonical = canonicalMoneyText(text);
    if (canonical === null || signedBudgetCents(text) === null)
        throw new BudgetMatrixInputError('invalidAmount');
    const rows = lines.filter(row => row.cashFlowItemId.toLowerCase() === item.id.toLowerCase() && row.budgetMonth === month);
    const directions = new Set(rows.map(row => row.flowDirection));
    // Retain unchanged manual gross operations. A replacement of mixed rows has
    // no unique existing direction: use the catalogue, never the entered sign.
    if (directions.size > 1 && origin === 'manual'
        && budgetMatrixAmount(rows, [item.id], [month]).cents === signedBudgetCents(text))
        return text;
    const direction = directions.size > 1 ? item.flowDirection : rows[0]?.flowDirection ?? item.flowDirection;
    if (direction !== 'Payment' && direction !== 'Receipt')
        throw new BudgetMatrixInputError('catalogDirectionMissing');
    const magnitude = canonical.replace(/^-/u, '');
    return direction === 'Payment' ? `-${magnitude}` : magnitude;
}
export function replaceManualBudgetCell(next: readonly BudgetDraftLine[], source: readonly BudgetDraftLine[], item: ReferenceDataItem, month: string, text: string, captured = false, origin: 'manual' | 'paste' = 'manual'): BudgetDraftLine[] {
    // A value already entered/previewed in the UI has its chosen sign frozen.
    // Refreshing the directory must not reinterpret that pending operation.
    const signed = captured ? text : manualBudgetCellText(source, item, month, text, origin);
    if (signedBudgetCents(signed) === null)
        throw new BudgetMatrixInputError('invalidAmount');
    const replaced = replaceBudgetMatrixCell(next, item.id, month, signed, origin === 'paste' ? 'replace' : 'manualReplace');
    if (signedBudgetCents(signed) !== 0)
        return replaced;
    const rows = source.filter(row => row.cashFlowItemId.toLowerCase() === item.id.toLowerCase() && row.budgetMonth === month);
    const mixed = new Set(rows.map(row => row.flowDirection)).size > 1;
    if (mixed && replaced.filter(row => row.cashFlowItemId.toLowerCase() === item.id.toLowerCase() && row.budgetMonth === month).length > 1)
        return replaced;
    const direction = (mixed ? null : rows[0]?.flowDirection) ?? (captured ? (signed.startsWith('-') ? 'Payment' : 'Receipt') : item.flowDirection);
    return replaced.map(row => row.cashFlowItemId.toLowerCase() === item.id.toLowerCase() && row.budgetMonth === month
        ? { ...row, flowDirection: direction! } : row);
}
