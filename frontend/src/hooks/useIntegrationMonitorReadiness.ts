import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import { getDatabaseReadinessMonitor } from '../api';
import type { DatabaseReadinessMonitor } from '../types';
export type IntegrationMonitorReadiness = {
    readiness: DatabaseReadinessMonitor | null;
    loading: boolean;
    error: string | null;
    reload: () => Promise<void>;
};
/// Allowlisted database readiness state. Owns its own request sequence, independent of the other monitor
/// panels, so a slow response here can never overwrite another panel's state.
export function useIntegrationMonitorReadiness(): IntegrationMonitorReadiness {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [readiness, setReadiness] = useState<DatabaseReadinessMonitor | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const requestSequence = useRef(0);
    const load = useCallback(async () => {
        const requestNumber = requestSequence.current + 1;
        requestSequence.current = requestNumber;
        setLoading(true);
        setError(null);
        try {
            const result = await getDatabaseReadinessMonitor();
            if (requestNumber !== requestSequence.current)
                return;
            setReadiness(result);
        }
        catch {
            if (requestNumber !== requestSequence.current)
                return;
            setReadiness(null);
            setError('Не удалось проверить готовность базы данных.');
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
    return { readiness, loading, error, reload: load };
}
