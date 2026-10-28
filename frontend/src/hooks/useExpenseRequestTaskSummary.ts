import { useEffect, useState } from 'react';
import { listExpenseRequests } from '../api';
export type ExpenseRequestTaskSummary = {
    status: 'unavailable';
} | {
    status: 'loading';
} | {
    status: 'ready';
    count: number;
} | {
    status: 'failed';
};
const unavailableTaskSummary: ExpenseRequestTaskSummary = { status: 'unavailable' };
function isAbortError(error: unknown): boolean {
    return error instanceof Error && error.name === 'AbortError';
}
async function readTaskCount(signal: AbortSignal): Promise<number> {
    const page = await listExpenseRequests(0, 1, undefined, undefined, undefined, undefined, undefined, undefined, undefined, undefined, undefined, true, signal);
    return page.total;
}
export function useExpenseRequestTaskSummary(enabled: boolean, refreshToken: number, pageRefreshToken = 0): ExpenseRequestTaskSummary {
    const [summary, setSummary] = useState<ExpenseRequestTaskSummary>(unavailableTaskSummary);
    useEffect(() => {
        if (!enabled) {
            setSummary(unavailableTaskSummary);
            return undefined;
        }
        const controller = new AbortController();
        setSummary({ status: 'loading' });
        void readTaskCount(controller.signal)
            .then((count) => {
            if (!controller.signal.aborted)
                setSummary({ status: 'ready', count });
        })
            .catch((error: unknown) => {
            if (!controller.signal.aborted && !isAbortError(error)) {
                // The task counter is optional navigation metadata. Keep the error
                // private and let the user open the authoritative list itself.
                setSummary({ status: 'failed' });
            }
        });
        return () => controller.abort();
    }, [enabled, refreshToken, pageRefreshToken]);
    return summary;
}
