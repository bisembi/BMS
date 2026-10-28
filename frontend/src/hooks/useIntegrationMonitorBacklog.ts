import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import { getOutboxBacklogSummary } from '../api';
import type { OutboxBacklogSummary } from '../types';
export type IntegrationMonitorBacklog = {
    summary: OutboxBacklogSummary | null;
    loading: boolean;
    error: string | null;
    reload: () => Promise<void>;
};
/// Aggregated Outbox backlog counters. Owns its own request sequence, independent of the other monitor
/// panels, so a slow response here can never overwrite another panel's state.
export function useIntegrationMonitorBacklog(): IntegrationMonitorBacklog {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [summary, setSummary] = useState<OutboxBacklogSummary | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const requestSequence = useRef(0);
    const load = useCallback(async () => {
        const requestNumber = requestSequence.current + 1;
        requestSequence.current = requestNumber;
        setLoading(true);
        setError(null);
        try {
            const result = await getOutboxBacklogSummary();
            if (requestNumber !== requestSequence.current)
                return;
            setSummary(result);
        }
        catch {
            if (requestNumber !== requestSequence.current)
                return;
            setSummary(null);
            setError('Не удалось загрузить сводку Outbox.');
        }
        finally {
            if (requestNumber === requestSequence.current)
                setLoading(false);
        }
    }, []);
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        void load();
        return () => {
            requestSequence.current += 1;
        };
    }, [load, workspaceRefresh]);
    return { summary, loading, error, reload: load };
}
