import { useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useState } from 'react';
import type { BudgetInstanceListItem } from '../api/budgetInstances';
import { getBudgetReview } from '../api/cfoWorkflow';
export const budgetApprovalRoleLabels: Readonly<Record<string, string>> = {
    BUDGET_OWNER: 'Владелец ЦФО', FIN_CONTROLLER: 'Финансовый контролёр',
    FIN_DIRECTOR: 'Финансовый директор', GENERAL_DIRECTOR: 'Генеральный директор',
};
const roleLabels = budgetApprovalRoleLabels;
/** Display-only summary reads; no permissions or document fields are changed. */
export function useBudgetApprovalListStatuses(items: readonly BudgetInstanceListItem[], enabled: boolean) {
    const [loading, setLoading] = useState(false);
    useWorkspaceRefreshBusy(loading);
    const [snapshot, setSnapshot] = useState<{
        items: typeof items;
        labels: Record<string, string>;
    }>({ items, labels: {} });
    useEffect(() => {
        const abort = new AbortController();
        setSnapshot({ items, labels: {} });
        if (!enabled) {
            setLoading(false);
            return () => abort.abort();
        }
        setLoading(true);
        const pending = items.filter(item => item.status === 'Draft' && item.approvalState === 'Pending');
        let next = 0;
        async function read() {
            while (!abort.signal.aborted && next < pending.length) {
                const item = pending[next++];
                try {
                    const review = await getBudgetReview(item.id, abort.signal);
                    if (abort.signal.aborted)
                        return;
                    const role = review?.currentRoleCode;
                    if (review?.budgetInstanceId !== item.id || review.state !== 'Pending'
                        || typeof role !== 'string' || !Object.hasOwn(roleLabels, role))
                        continue;
                    setSnapshot(previous => ({ items, labels: {
                            ...(previous.items === items ? previous.labels : {}), [item.id]: roleLabels[role],
                        } }));
                }
                catch { /* Keep the confirmed list status; never guess a stage on failure. */ }
            }
        }
        // Bound the extra GETs, and refresh every accepted page load, not by the
        // document stamp: advancing an approval changes only the review stamp.
        void Promise.all(Array.from({ length: Math.min(4, pending.length) }, () => read()))
            .finally(() => {
            if (!abort.signal.aborted)
                setLoading(false);
        });
        return () => abort.abort();
    }, [items, enabled]);
    return enabled && snapshot.items === items ? snapshot.labels : {};
}
export function budgetInstanceListStatus(item: Pick<BudgetInstanceListItem, 'status' | 'approvalState'>, roleLabel?: string): string {
    const parts = budgetInstanceListStatusParts(item, roleLabel);
    return parts.label + parts.supplement;
}
export function budgetInstanceListStatusParts(item: Pick<BudgetInstanceListItem, 'status' | 'approvalState'>, roleLabel?: string) {
    if (item.status === 'Approved' || item.approvalState === 'Approved')
        return { label: 'Утвержденный', supplement: '' };
    if (item.approvalState === 'Pending')
        return { label: 'На согласовании', supplement: roleLabel ? ' — ' + roleLabel : '' };
    if (item.approvalState === 'Returned')
        return { label: 'Возвращён', supplement: '' };
    if (item.approvalState === 'Rejected')
        return { label: 'Отклонён', supplement: '' };
    return { label: 'Черновик', supplement: '' };
}
/** Presentation only; use the same precedence as the confirmed status text. */
export function budgetInstanceListStatusClass(item: BudgetInstanceListItem): string {
    if (item.status === 'Approved' || item.approvalState === 'Approved')
        return 'budget-status-approved';
    if (item.approvalState === 'Pending')
        return 'budget-status-pending';
    if (item.approvalState === 'Returned')
        return 'budget-status-returned';
    return '';
}
