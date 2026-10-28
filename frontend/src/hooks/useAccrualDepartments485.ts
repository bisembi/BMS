import { useCallback, useEffect, useState } from 'react';
import { getAccrualCostCenters } from '../api/referenceData';
import { useWorkspaceRefreshBusy, useWorkspaceRefreshRevision } from '../components/WorkspaceRefreshContext';
import type { ReferenceDataItem } from '../types';
export type AccrualDepartmentStatus485 = 'idle' | 'loading' | 'ready' | 'error';
export type AccrualDepartmentCatalog485 = {
    items: ReferenceDataItem[];
    status: AccrualDepartmentStatus485;
    retry: () => void;
};
export const accrualDepartmentDeadline485 = 15000;
// This catalogue is intentionally separate from the employee's assigned CFOs.
// Failure, an empty response, or an old document response must never substitute
// that narrower list or silently change a saved accrual department.
export function useAccrualDepartments485(scope: string, enabled = true): AccrualDepartmentCatalog485 {
    const refresh = useWorkspaceRefreshRevision(), [attempt, setAttempt] = useState(0);
    const key = JSON.stringify([scope, enabled, refresh, attempt]);
    const [state, setState] = useState<{
        key: string;
        items: ReferenceDataItem[];
        status: AccrualDepartmentStatus485;
    }>({ key: '', items: [], status: 'idle' });
    const current = state.key === key ? state : { key, items: [], status: enabled ? 'loading' as const : 'idle' as const };
    useWorkspaceRefreshBusy(current.status === 'loading');
    useEffect(() => {
        if (!enabled) {
            setState({ key, items: [], status: 'idle' });
            return;
        }
        let active = true, settled = false;
        const controller = new AbortController();
        setState({ key, items: [], status: 'loading' });
        const deadline = setTimeout(() => {
            if (active && !settled) {
                settled = true;
                controller.abort();
                setState({ key, items: [], status: 'error' });
            }
        }, accrualDepartmentDeadline485);
        void getAccrualCostCenters(controller.signal)
            .then(catalog => {
            if (active && !settled) {
                settled = true;
                clearTimeout(deadline);
                setState({ key, items: catalog.items, status: 'ready' });
            }
        })
            .catch(() => {
            if (active && !settled) {
                settled = true;
                clearTimeout(deadline);
                setState({ key, items: [], status: 'error' });
            }
        });
        return () => { active = false; clearTimeout(deadline); controller.abort(); };
    }, [key, enabled]);
    const retry = useCallback(() => setAttempt(value => value + 1), []);
    return { items: current.items, status: current.status, retry };
}
