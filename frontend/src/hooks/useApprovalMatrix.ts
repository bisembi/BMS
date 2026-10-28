import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useState } from 'react';
import { getApprovalMatrix } from '../api';
import type { ApprovalMatrix } from '../api/approvalMatrixPublicResponse';
type MatrixState = {
    status: 'loading';
} | {
    status: 'failed';
} | {
    status: 'ready';
    data: ApprovalMatrix;
};
export function useApprovalMatrix(version = 1) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [state, setState] = useState<MatrixState>({ status: 'loading' });
    const [generation, setGeneration] = useState(0);
    useWorkspaceRefreshBusy(state.status === 'loading');
    useEffect(() => {
        const controller = new AbortController();
        setState({ status: 'loading' });
        void getApprovalMatrix(controller.signal, version)
            .then((data) => {
            if (!controller.signal.aborted)
                setState({ status: 'ready', data });
        })
            .catch(() => {
            if (!controller.signal.aborted)
                setState({ status: 'failed' });
        });
        return () => controller.abort();
    }, [generation, version, workspaceRefresh]);
    return { state, refresh: () => {
            setState({ status: 'loading' });
            setGeneration((value) => value + 1);
        } };
}
