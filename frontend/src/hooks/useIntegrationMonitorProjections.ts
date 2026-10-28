import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import { getReferenceProjectionInventory } from '../api';
import type { ReferenceProjectionInventory } from '../types';
export type IntegrationMonitorProjections = {
    inventory: ReferenceProjectionInventory | null;
    loading: boolean;
    error: string | null;
    reload: () => Promise<void>;
};
/// Employee and functional-role projection counters. Owns its own request sequence, independent of the other monitor
/// panels, so a slow response here can never overwrite another panel's state.
export function useIntegrationMonitorProjections(): IntegrationMonitorProjections {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [inventory, setInventory] = useState<ReferenceProjectionInventory | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const requestSequence = useRef(0);
    const load = useCallback(async () => {
        const requestNumber = requestSequence.current + 1;
        requestSequence.current = requestNumber;
        setLoading(true);
        setError(null);
        try {
            const result = await getReferenceProjectionInventory();
            if (requestNumber !== requestSequence.current)
                return;
            setInventory(result);
        }
        catch {
            if (requestNumber !== requestSequence.current)
                return;
            setInventory(null);
            setError('Не удалось загрузить инвентаризацию проекций.');
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
    return { inventory, loading, error, reload: load };
}
