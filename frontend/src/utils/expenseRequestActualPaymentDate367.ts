import type { ExpenseRequestListItem } from '../types';
import { isCanonicalExpenseRequestDate } from './expenseRequestListDate';
/** Confirmed full-settlement calendar day; not a planned or browser-local date. */
export function formatActualPaymentDate(item: ExpenseRequestListItem): string {
    if (!item.canOpen || (item.status !== 'PAID' && item.status !== 'CLOSED'))
        return '—';
    const value = item.businessColumns?.fullyPaidDate;
    if (!value || !isCanonicalExpenseRequestDate(value))
        return '—';
    return `${value.slice(8, 10)}.${value.slice(5, 7)}.${value.slice(2, 4)}`;
}
