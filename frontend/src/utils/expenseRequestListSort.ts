export const expenseRequestSortKeys = ['requestNumber', 'requestDate', 'plannedPaymentDate', 'organization',
    'costCenter', 'counterparty', 'initiator', 'status', 'urgency', 'amount', 'amountKzt', 'budget'] as const;
export type ExpenseRequestSortKey = typeof expenseRequestSortKeys[number];
export type ExpenseRequestListSort = {
    by: ExpenseRequestSortKey;
    direction: 'asc' | 'desc';
};
export function assertExpenseRequestListSort(sort: ExpenseRequestListSort | undefined): void {
    if (sort !== undefined && (!expenseRequestSortKeys.includes(sort.by)
        || (sort.direction !== 'asc' && sort.direction !== 'desc'))) {
        throw new Error('Некорректная сортировка расходных заявок.');
    }
}
