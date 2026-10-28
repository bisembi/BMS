import { useEffect, useState } from 'react';
import { getExpenseApprovalRoute } from '../api/expenseApprovalRoute';
import { expenseRequestRoleStage, expenseRequestStatusAtStageLabel } from '../expenseRequestStatus';
import { useWorkspaceRefreshRevision } from '../components/WorkspaceRefreshContext';
import type { ExpenseRequestStatus } from '../types';
/** A read-only, version-bound stage label. Never infer the assignee from the viewer's role. */
export function useExpenseRequestStatusLabel(status: ExpenseRequestStatus | null, id: string | null, version: string | null, actor: string | null, fallback: string) {
    const refresh = useWorkspaceRefreshRevision();
    const key = status === 'IN_APPROVAL' && id && version ? JSON.stringify([id, version, actor, refresh]) : null;
    const [capture, setCapture] = useState<{
        key: string;
        label: string;
    } | null>(null);
    useEffect(() => {
        if (!key || !id || !version)
            return;
        const controller = new AbortController();
        void getExpenseApprovalRoute({ id, version }, controller.signal).then(route => {
            if (controller.signal.aborted || route.requestId.toLowerCase() !== id.toLowerCase() || route.requestVersion.toLowerCase() !== version.toLowerCase() || !route.available || route.previewOnly || route.statusCode !== 'IN_APPROVAL')
                return;
            const stage = route.stages.find(s => s.stageNumber === route.currentStageNumber);
            const role = stage && expenseRequestRoleStage(stage.roleCode);
            if (role)
                setCapture({ key, label: expenseRequestStatusAtStageLabel('IN_APPROVAL', role) });
        }).catch(() => { });
        return () => controller.abort();
    }, [key, id, version]);
    return key && capture?.key === key ? capture.label : fallback;
}
