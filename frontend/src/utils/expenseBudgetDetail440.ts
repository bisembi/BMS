import type { ExpenseRequestDraft, ReferenceDataItem } from '../types';
import { expenseBudgetLimitsInput, type ExpenseBudgetLimit } from '../hooks/useExpenseBudgetLimits';
import { isPublicExpenseRequestUuid } from '../api/expenseRequestPublicResponse';
import type { BudgetPositionKey } from '../api/budgetPositions';
import { expenseViewReference } from '../components/expense-request-form/ExpenseRequestReadView';
export type ExpenseBudgetDetailContext440 = {
    key: BudgetPositionKey;
    article: string;
    costCenter: string;
};
export function expenseBudgetDetail440(draft: ExpenseRequestDraft, index: number, limits: readonly ExpenseBudgetLimit[], centers: readonly ReferenceDataItem[], articles: readonly ReferenceDataItem[]): ExpenseBudgetDetailContext440 | null {
    if (draft.paymentOperation?.kind === 'PayrollAdvance' || !Number.isSafeInteger(index) || index < 0)
        return null;
    const input = expenseBudgetLimitsInput(draft), line = draft.lines[index], limit = limits[index];
    if (!input || !line || !isPublicExpenseRequestUuid(line.cashFlowItemId)
        || limit?.state !== 'ready' || limit.amountKzt === null)
        return null;
    return { key: { costCenterId: input.costCenterId, cashFlowItemId: line.cashFlowItemId.toLowerCase(),
            budgetMonth: input.plannedPaymentDate.slice(0, 7) + '-01' },
        article: expenseViewReference(articles, line.cashFlowItemId, 'Статья ДДС недоступна', true),
        costCenter: expenseViewReference(centers, input.costCenterId, 'ЦФО недоступен') };
}
