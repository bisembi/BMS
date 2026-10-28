import type { ExpenseRequestPage } from '../types';
export type ExpenseRequestListSnapshot = {
    context: string;
    hideDrafts: boolean;
    page: ExpenseRequestPage;
};
export function expenseRequestListView(current: ExpenseRequestListSnapshot | null, firstPages: readonly ExpenseRequestListSnapshot[], context: string, skip: number, take: number, hideDrafts: boolean): {
    page: ExpenseRequestPage;
    totalKnown: boolean;
} {
    const candidates = [current, ...firstPages].filter((entry): entry is ExpenseRequestListSnapshot => entry !== null && entry.context === context && entry.page.skip === skip && entry.page.take === take);
    const exact = candidates.find(entry => entry.hideDrafts === hideDrafts);
    if (exact)
        return { page: exact.page, totalKnown: true };
    // The checkbox resets to page one. Never relabel a later page as the first,
    // or reuse rows from another role, actor, filter, sort or refresh revision.
    const source = skip === 0 ? candidates[0] : undefined;
    const items = source?.page.items.filter(item => !hideDrafts
        || !['DRAFT', 'CANCELLED', 'DELETED'].includes(item.status)) ?? [];
    const totalKnown = !!source && hideDrafts && !source.hideDrafts
        && source.page.items.length === source.page.total;
    return {
        page: { items, skip, take, total: totalKnown ? items.length : 0,
            ...(source?.page.budgetIndicatorsUnavailable ? { budgetIndicatorsUnavailable: true } : {}) },
        totalKnown,
    };
}
