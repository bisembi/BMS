import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import { getOutboxMonitor } from '../api';
import { pageSize } from '../components/integration-monitor/integrationMonitorFormat';
import type { OutboxMonitorItem } from '../types';
export type IntegrationMonitorOutbox = {
    items: OutboxMonitorItem[];
    hasMore: boolean;
    loading: boolean;
    error: string | null;
    skip: number;
    setSkip: (skip: number) => void;
    reload: () => Promise<void>;
};
/// Outbox page state with its own request sequence. Deliberately separate from
/// the backlog, readiness and projection hooks so no stale-response guard is
/// shared between independent panels.
export function useIntegrationMonitorOutbox(): IntegrationMonitorOutbox {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [skip, setSkip] = useState(0);
    const [items, setItems] = useState<OutboxMonitorItem[]>([]);
    const [hasMore, setHasMore] = useState(false);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const requestSequence = useRef(0);
    const load = useCallback(async () => {
        const requestNumber = requestSequence.current + 1;
        requestSequence.current = requestNumber;
        setLoading(true);
        setError(null);
        try {
            const page = await getOutboxMonitor(skip, pageSize);
            if (requestNumber !== requestSequence.current)
                return;
            setItems(page.items);
            setHasMore(page.hasMore);
        }
        catch {
            if (requestNumber !== requestSequence.current)
                return;
            setItems([]);
            setHasMore(false);
            setError('Не удалось загрузить сервисный монитор Outbox.');
        }
        finally {
            if (requestNumber === requestSequence.current)
                setLoading(false);
        }
    }, [skip]);
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        void load();
        return () => {
            requestSequence.current += 1;
        };
    }, [load, workspaceRefresh]);
    return { items, hasMore, loading, error, skip, setSkip, reload: load };
}
