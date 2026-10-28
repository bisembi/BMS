import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useState } from 'react';
import type { ExpenseLineDraft, ExpenseRequestDraft } from '../types';
import { previewExpenseDraftBudget, type ExpenseDraftBudgetInput, type ExpenseDraftBudgetPosition } from '../api/expenseDraftBudgetPreview';
import { isPublicExpenseRequestUuid as uuid } from '../api/expenseRequestPublicResponse';
import type { ExpenseLineBudgetControl } from '../utils/expenseLineBudgetControl';
import { isValidPositiveMoneyInput } from '../api/expenseRequestCalculations';
const validLine = (line: ExpenseLineDraft, cfo: string) => uuid(line.cashFlowItemId) && isValidPositiveMoneyInput(line.amount)
    && (line.costCenterId.trim() === '' || line.costCenterId.toLowerCase() === cfo.toLowerCase());
export function expenseDraftBudgetInput(draft: ExpenseRequestDraft, requestId: string | null, version: string | null): ExpenseDraftBudgetInput | null {
    const date = new Date(`${draft.plannedPaymentDate}T00:00:00Z`);
    if (!uuid(draft.defaultCostCenterId) || !/^\d{4}-\d{2}-\d{2}$/u.test(draft.plannedPaymentDate)
        || Number.isNaN(date.getTime()) || date.toISOString().slice(0, 10) !== draft.plannedPaymentDate)
        return null;
    const lines = draft.lines.filter(line => validLine(line, draft.defaultCostCenterId))
        .map(line => ({ cashFlowItemId: line.cashFlowItemId, costCenterId: line.costCenterId || null,
        amount: Number(line.amount), vatRateCode: line.vatRateCode }));
    if (lines.length === 0 || (requestId !== null && !uuid(version)))
        return null;
    return { costCenterId: draft.defaultCostCenterId, plannedPaymentDate: draft.plannedPaymentDate,
        currencyCode: draft.currencyCode, lines, ...(requestId ? { expenseRequestId: requestId, expectedVersion: version! } : {}) };
}
export function useExpenseDraftBudgetPreview(draft: ExpenseRequestDraft, requestId: string | null, version: string | null, enabled: boolean): readonly ExpenseLineBudgetControl[] {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const input = enabled ? expenseDraftBudgetInput(draft, requestId, version) : null;
    const key = input === null ? '' : JSON.stringify(input);
    const [capture, setCapture] = useState<{
        key: string;
        positions: ExpenseDraftBudgetPosition[] | null;
    } | null>(null);
    const requestKey = `${key}/${workspaceRefresh}`;
    useWorkspaceRefreshBusy(input !== null && capture?.key !== requestKey);
    useEffect(() => {
        if (!key)
            return;
        setCapture(null);
        const controller = new AbortController();
        const timer = setTimeout(() => {
            void previewExpenseDraftBudget(JSON.parse(key) as ExpenseDraftBudgetInput, controller.signal)
                .then(positions => {
                if (!controller.signal.aborted)
                    setCapture({ key: requestKey, positions });
            })
                .catch(() => {
                if (!controller.signal.aborted)
                    setCapture({ key: requestKey, positions: null });
            });
        }, 200);
        return () => { clearTimeout(timer); controller.abort(); };
    }, [key, requestKey]);
    const positions = new Map(capture?.positions?.map(row => [row.cashFlowItemId.toLowerCase(), row]));
    return draft.lines.map(line => {
        const empty = (state: 'pending' | 'unavailable'): ExpenseLineBudgetControl => ({ state, availableAmountKzt: null, positiveDeficitKzt: null });
        if (!uuid(draft.defaultCostCenterId))
            return { ...empty('unavailable'), reason: 'Выберите ЦФО на вкладке «Основное».' };
        if (!uuid(line.cashFlowItemId))
            return { ...empty('unavailable'), reason: 'Выберите статью ДДС.' };
        if (!isValidPositiveMoneyInput(line.amount))
            return { ...empty('unavailable'), reason: line.amount.trim() ? 'Исправьте формат суммы.' : 'Введите сумму для расчёта остатка.' };
        if (line.costCenterId.trim() && line.costCenterId.toLowerCase() !== draft.defaultCostCenterId.toLowerCase())
            return { ...empty('unavailable'), reason: 'Подразделение строки должно совпадать с ЦФО заявки.' };
        if (input === null)
            return { ...empty('unavailable'), reason: 'Проверьте дату оплаты на вкладке «Основное».' };
        if (capture?.key !== requestKey)
            return empty('pending');
        const position = positions.get(line.cashFlowItemId.toLowerCase());
        return position ? { state: 'preview', availableAmountKzt: position.availableAmountKzt, positiveDeficitKzt: position.positiveDeficitKzt,
            availableBeforeRequestKzt: (Math.round(position.availableAmountKzt * 100) + Math.round(position.requestedAmountKzt * 100)) / 100 }
            : { ...empty('unavailable'), reason: 'Не удалось получить остаток бюджета. Повторите обновление.' };
    });
}
