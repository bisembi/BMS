import { useEffect, useState } from 'react';
import { listReturnedRequestNotices377 } from '../api/returnedRequestNotices377';
import { listAdjustmentNotices448 } from '../api/adjustmentNotices448';
import type { ExpenseRequestTaskSummary } from './useExpenseRequestTaskSummary';
export function useReturnedNoticeSummary377(context: string | null, revision: string): ExpenseRequestTaskSummary {
    return useNoticeSummary448(context, revision, listReturnedRequestNotices377);
}
export function useAdjustmentNoticeSummary448(context: string | null, revision: string): ExpenseRequestTaskSummary {
    return useNoticeSummary448(context, revision, listAdjustmentNotices448);
}
export function useNoticeSummary448(context: string | null, revision: string, load: (skip: number, take: number, signal: AbortSignal) => Promise<{
    total: number;
}>): ExpenseRequestTaskSummary {
    const [result, setResult] = useState<{
        context: string | null;
        value: ExpenseRequestTaskSummary;
    }>({ context: null, value: { status: 'unavailable' } });
    useEffect(() => {
        if (!context) {
            setResult({ context, value: { status: 'unavailable' } });
            return;
        }
        let stopped = false;
        let request: AbortController | undefined;
        const refresh = () => {
            request?.abort();
            const current = new AbortController();
            request = current;
            void load(0, 1, current.signal).then(page => {
                if (!stopped && !current.signal.aborted)
                    setResult({ context, value: { status: 'ready', count: page.total } });
            }).catch(() => {
                if (!stopped && !current.signal.aborted)
                    setResult({ context, value: { status: 'failed' } });
            });
        };
        setResult({ context, value: { status: 'loading' } });
        refresh();
        const tick = window.setInterval(refresh, 60000);
        const visible = () => {
            if (document.visibilityState === 'visible')
                refresh();
        };
        document.addEventListener('visibilitychange', visible);
        return () => { stopped = true; request?.abort(); window.clearInterval(tick); document.removeEventListener('visibilitychange', visible); };
    }, [context, revision, load]);
    return !context ? { status: 'unavailable' } : result.context === context ? result.value : { status: 'loading' };
}
