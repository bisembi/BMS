import { useEffect, useState } from 'react';
import type { ExpenseRequestDraft } from '../types';
import { isPublicExpenseRequestUuid as uuid } from '../api/expenseRequestPublicResponse';
import { previewExpenseBudgetLimits, type ExpenseDraftBudgetInput, type ExpenseDraftBudgetPosition } from '../api/expenseDraftBudgetPreview';
import { useWorkspaceRefreshRevision } from '../components/WorkspaceRefreshContext';
export type ExpenseBudgetLimit = {
    amountKzt: number | null;
    state: 'loading' | 'ready' | 'unavailable';
};
export function expenseBudgetLimitsInput(draft: ExpenseRequestDraft): ExpenseDraftBudgetInput | null {
    const date = new Date(`${draft.plannedPaymentDate}T00:00:00Z`);
    if (!uuid(draft.defaultCostCenterId) || !/^\d{4}-\d{2}-\d{2}$/u.test(draft.plannedPaymentDate)
        || !Number.isFinite(date.getTime()) || date.toISOString().slice(0, 10) !== draft.plannedPaymentDate)
        return null;
    const ids = [...new Set(draft.lines.filter(line => uuid(line.cashFlowItemId))
            .map(line => line.cashFlowItemId.toLowerCase()))].sort();
    return ids.length ? { costCenterId: draft.defaultCostCenterId.toLowerCase(), plannedPaymentDate: draft.plannedPaymentDate,
        currencyCode: 'KZT', limitsOnly: true,
        lines: ids.map(id => ({ cashFlowItemId: id, costCenterId: null, amount: 0, vatRateCode: 'WithoutVat' })) } : null;
}
export function useExpenseBudgetLimits(draft: ExpenseRequestDraft, enabled: boolean): readonly ExpenseBudgetLimit[] {
    const revision = useWorkspaceRefreshRevision();
    const input = enabled ? expenseBudgetLimitsInput(draft) : null;
    const key = input ? JSON.stringify(input) : '';
    const requestKey = `${key}/${revision}`;
    const [capture, setCapture] = useState<{
        key: string;
        positions: ExpenseDraftBudgetPosition[] | null;
    } | null>(null);
    useEffect(() => {
        if (!key)
            return;
        const controller = new AbortController();
        const timer = setTimeout(() => {
            void previewExpenseBudgetLimits(JSON.parse(key) as ExpenseDraftBudgetInput, controller.signal)
                .then(positions => {
                if (!controller.signal.aborted)
                    setCapture({ key: requestKey, positions });
            })
                .catch(() => {
                if (!controller.signal.aborted)
                    setCapture({ key: requestKey, positions: null });
            });
        }, 150);
        return () => { clearTimeout(timer); controller.abort(); };
    }, [key, requestKey]);
    const positions = new Map(capture?.positions?.map(row => [row.cashFlowItemId.toLowerCase(), row]));
    return draft.lines.map(line => {
        if (!input || !uuid(line.cashFlowItemId))
            return { state: 'unavailable', amountKzt: null };
        if (capture?.key !== requestKey)
            return { state: 'loading', amountKzt: null };
        const position = positions.get(line.cashFlowItemId.toLowerCase());
        return position ? { state: 'ready', amountKzt: position.limitAmountKzt } : { state: 'unavailable', amountKzt: null };
    });
}
