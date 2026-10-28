import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useState } from 'react';
import { getExpenseRequestPaymentDetails, paymentDetailsErrorMessage } from '../api/expenseRequestPaymentDetails';
import type { ExpenseRequestPaymentDetails } from '../api/expenseRequestPaymentDetailsPublicResponse';
type State = {
    key: string;
    data?: ExpenseRequestPaymentDetails;
    error?: string;
};
export function useExpenseRequestPaymentDetails(requestId: string | null) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const id = requestId?.toLowerCase() ?? null;
    const [openedFor, setOpenedFor] = useState<string | null>(null);
    const [skip, setSkip] = useState(0);
    const [generation, setGeneration] = useState(0);
    const [state, setState] = useState<State | null>(null);
    const expanded = id !== null && openedFor === id;
    const key = `${id}/${skip}/${generation}`;
    useEffect(() => {
        if (!expanded || !id)
            return undefined;
        const controller = new AbortController();
        setState({ key });
        void getExpenseRequestPaymentDetails(id, skip, 25, controller.signal).then(data => {
            if (!controller.signal.aborted)
                setState({ key, data });
        }).catch(failure => {
            if (!controller.signal.aborted)
                setState({ key, error: paymentDetailsErrorMessage(failure) });
        });
        return () => controller.abort();
    }, [expanded, id, skip, generation, key, workspaceRefresh]);
    const current = expanded && state?.key === key ? state : null;
    const loading = expanded && !current?.data && !current?.error;
    useWorkspaceRefreshBusy(loading);
    return { expanded, loading, data: current?.data, error: current?.error,
        toggle: () => { setOpenedFor(expanded ? null : id); setSkip(0); setGeneration(x => x + 1); },
        reload: () => setGeneration(x => x + 1),
        previous: () => setSkip(x => Math.max(0, x - 25)),
        next: () => {
            if (current?.data && skip + current.data.take < current.data.total)
                setSkip(x => x + 25);
        },
    };
}
