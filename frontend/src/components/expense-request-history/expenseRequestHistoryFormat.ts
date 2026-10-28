import type { ExpenseRequestHistoryChangeKind, ExpenseRequestHistoryDetailsStatus } from '../../types';
import { formatBusinessTimestamp } from '../../utils/businessTime';
export const historyPageSize = 20;
export const changeKindLabels: Record<ExpenseRequestHistoryChangeKind, string> = {
    added: 'Добавлено',
    changed: 'Изменено',
    removed: 'Удалено',
};
export const detailsStatusLabels: Record<ExpenseRequestHistoryDetailsStatus, string> = {
    complete: 'Полная детализация',
    partial: 'Частичная детализация',
    unavailable: 'Детализация недоступна',
};
export function formatHistoryUtc(value: string): string {
    return `${formatBusinessTimestamp(value)} UTC+5`;
}
