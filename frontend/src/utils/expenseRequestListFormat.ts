import { invalidExpenseRequestDateMessage, isCanonicalExpenseRequestDate } from './expenseRequestListDate';
import { expenseRequestStatusLabel, expenseRequestStatusAtStageLabel, isExpenseRequestStatus } from '../expenseRequestStatus';
import { formatBusinessDay } from './businessTime';
/// Presentation helpers of the expense-request list, moved out of the page
/// unchanged. No request, no state, no identifier is handled here.
export function formatDate(value: string | null | undefined): string {
    return formatBusinessDay(value);
}
/** Planned calendar day in the register only; never change the source date. */
export function formatPlannedPaymentDate(value: string | null | undefined): string {
    if (!value || !isCanonicalExpenseRequestDate(value))
        return '—';
    return `${value.slice(8, 10)}.${value.slice(5, 7)}.${value.slice(2, 4)}`;
}
export function statusLabel(value: string): string {
    return isExpenseRequestStatus(value) ? expenseRequestStatusLabel(value) : 'Статус недоступен';
}
export function statusWithStageLabel(value: string, currentStage: string | null | undefined): string {
    return isExpenseRequestStatus(value) ? expenseRequestStatusAtStageLabel(value, currentStage) : 'Статус недоступен';
}
export function utcDayStart(value: string): string | undefined {
    if (!value)
        return undefined;
    if (!isCanonicalExpenseRequestDate(value)) {
        throw new Error(invalidExpenseRequestDateMessage);
    }
    return new Date(`${value}T00:00:00+05:00`).toISOString();
}
export function utcDayAfter(value: string): string | undefined {
    if (!value)
        return undefined;
    if (!isCanonicalExpenseRequestDate(value)) {
        throw new Error(invalidExpenseRequestDateMessage);
    }
    const boundary = new Date(`${value}T00:00:00+05:00`);
    boundary.setUTCDate(boundary.getUTCDate() + 1);
    return boundary.toISOString();
}
