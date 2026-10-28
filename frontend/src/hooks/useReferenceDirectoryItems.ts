import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import { getCashFlowItems, getContracts, getCostCenters, getCounterparties, getCurrencies, getExpenseItems, getOrganizations } from '../api';
import { loadErrorMessages } from '../components/reference-directory/referenceDirectoryText';
import type { ReferenceDirectoryId } from '../navigation';
import type { ReferenceDataItem } from '../types';
function loadDirectory(directoryId: ReferenceDirectoryId): Promise<{
    items: ReferenceDataItem[];
}> {
    switch (directoryId) {
        case 'organizations':
            return getOrganizations();
        case 'cost-centers':
            return getCostCenters();
        case 'cash-flow-items':
            return getCashFlowItems();
        case 'expense-items':
            return getExpenseItems();
        case 'counterparties':
            return getCounterparties();
        case 'currencies':
            return getCurrencies();
        case 'contracts':
            throw new Error('Contracts require an explicit organization and counterparty scope.');
        default: {
            const exhaustive: never = directoryId;
            throw new Error(`Unsupported reference directory: ${String(exhaustive)}`);
        }
    }
}
export type ReferenceDirectoryItems = {
    items: ReferenceDataItem[];
    loading: boolean;
    error: string | null;
    reload: () => Promise<void>;
};
export type ReferenceDirectoryItemsScope = {
    directoryId: ReferenceDirectoryId;
    requiresScope: boolean;
    selectedOrganization: ReferenceDataItem | undefined;
    selectedCounterparty: ReferenceDataItem | undefined;
    scopeComplete: boolean;
};
/// Directory rows with their own request sequence, independent of the scope
/// reader. Contracts are requested only once both parts of the scope are chosen.
export function useReferenceDirectoryItems({ directoryId, requiresScope, selectedOrganization, selectedCounterparty, scopeComplete, }: ReferenceDirectoryItemsScope): ReferenceDirectoryItems {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const directoryRefresh = requiresScope ? 0 : workspaceRefresh;
    const [items, setItems] = useState<ReferenceDataItem[]>([]);
    const [loading, setLoading] = useState(!requiresScope);
    const [error, setError] = useState<string | null>(null);
    const requestSequence = useRef(0);
    useWorkspaceRefreshBusy(loading);
    const reload = useCallback(async () => {
        const requestNumber = requestSequence.current + 1;
        requestSequence.current = requestNumber;
        setLoading(true);
        setError(null);
        try {
            let catalog: {
                items: ReferenceDataItem[];
            };
            if (requiresScope) {
                const organization = selectedOrganization;
                const counterparty = selectedCounterparty;
                if (!organization || !counterparty) {
                    setItems([]);
                    setLoading(false);
                    return;
                }
                catalog = await getContracts(counterparty.id, organization.id);
            }
            else {
                catalog = await loadDirectory(directoryId);
            }
            if (requestNumber !== requestSequence.current)
                return;
            setItems(catalog.items);
        }
        catch {
            if (requestNumber !== requestSequence.current)
                return;
            setItems([]);
            setError(loadErrorMessages[directoryId]);
        }
        finally {
            if (requestNumber === requestSequence.current)
                setLoading(false);
        }
    }, [directoryId, requiresScope, selectedCounterparty, selectedOrganization]);
    useEffect(() => {
        if (requiresScope) {
            if (!scopeComplete) {
                setItems([]);
                setError(null);
                setLoading(false);
                return () => {
                    requestSequence.current += 1;
                };
            }
        }
        void reload();
        return () => {
            requestSequence.current += 1;
        };
    }, [requiresScope, reload, scopeComplete, directoryRefresh]);
    return { items, loading, error, reload };
}
