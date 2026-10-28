import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import { getCounterparties, getOrganizations } from '../api';
import { scopeErrorMessage } from '../components/reference-directory/referenceDirectoryText';
import type { ReferenceDataItem } from '../types';
type ScopeState = {
    organizations: ReferenceDataItem[];
    counterparties: ReferenceDataItem[];
};
function sameReferenceId(left: string, right: string): boolean {
    return left.toLowerCase() === right.toLowerCase();
}
export type ReferenceDirectoryScope = {
    organizations: ReferenceDataItem[];
    counterparties: ReferenceDataItem[];
    organizationIndex: number;
    counterpartyIndex: number;
    setOrganizationIndex: (index: number) => void;
    setCounterpartyIndex: (index: number) => void;
    selectedOrganization: ReferenceDataItem | undefined;
    selectedCounterparty: ReferenceDataItem | undefined;
    scopeComplete: boolean;
    loading: boolean;
    error: string | null;
    reload: () => Promise<void>;
};
/// Organisation and counterparty scope for the scoped directory. Selection is
/// held as opaque identities in memory and exposed as indexes into the loaded
/// arrays, so no identifier reaches the DOM. Owns its own request sequence,
/// separate from the directory items.
export function useReferenceDirectoryScope(enabled: boolean): ReferenceDirectoryScope {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [scope, setScope] = useState<ScopeState>({ organizations: [], counterparties: [] });
    const [loading, setLoading] = useState(enabled);
    const [error, setError] = useState<string | null>(null);
    const scopeSequence = useRef(0);
    const [organizationId, setOrganizationId] = useState<string | null>(null);
    const [counterpartyId, setCounterpartyId] = useState<string | null>(null);
    const organizationIndex = organizationId === null
        ? -1
        : scope.organizations.findIndex((item) => sameReferenceId(item.id, organizationId));
    const counterpartyIndex = counterpartyId === null
        ? -1
        : scope.counterparties.findIndex((item) => sameReferenceId(item.id, counterpartyId));
    const selectedOrganization = organizationIndex < 0 ? undefined : scope.organizations[organizationIndex];
    const selectedCounterparty = counterpartyIndex < 0 ? undefined : scope.counterparties[counterpartyIndex];
    const scopeComplete = selectedOrganization !== undefined && selectedCounterparty !== undefined;
    const setOrganizationIndex = useCallback((index: number) => {
        setOrganizationId(scope.organizations[index]?.id ?? null);
    }, [scope.organizations]);
    const setCounterpartyIndex = useCallback((index: number) => {
        setCounterpartyId(scope.counterparties[index]?.id ?? null);
    }, [scope.counterparties]);
    useWorkspaceRefreshBusy(loading);
    const reload = useCallback(async () => {
        const requestNumber = scopeSequence.current + 1;
        scopeSequence.current = requestNumber;
        setLoading(true);
        setError(null);
        try {
            const [organizations, counterparties] = await Promise.all([
                getOrganizations(),
                getCounterparties(),
            ]);
            if (requestNumber !== scopeSequence.current)
                return;
            setScope({ organizations: organizations.items, counterparties: counterparties.items });
            setOrganizationId((current) => current !== null
                && organizations.items.some((item) => sameReferenceId(item.id, current)) ? current : null);
            setCounterpartyId((current) => current !== null
                && counterparties.items.some((item) => sameReferenceId(item.id, current)) ? current : null);
        }
        catch {
            if (requestNumber !== scopeSequence.current)
                return;
            setScope({ organizations: [], counterparties: [] });
            setOrganizationId(null);
            setCounterpartyId(null);
            setError(scopeErrorMessage);
        }
        finally {
            if (requestNumber === scopeSequence.current)
                setLoading(false);
        }
    }, []);
    useEffect(() => {
        if (!enabled) {
            scopeSequence.current += 1;
            setScope({ organizations: [], counterparties: [] });
            setOrganizationId(null);
            setCounterpartyId(null);
            setError(null);
            setLoading(false);
            return undefined;
        }
        void reload();
        return () => {
            // Invalidate a response that resolves after the scoped directory has
            // been hidden or unmounted; the sequence is independent of the rows
            // reader and must be invalidated at the same lifecycle boundary.
            scopeSequence.current += 1;
        };
    }, [enabled, reload, workspaceRefresh]);
    return {
        organizations: scope.organizations,
        counterparties: scope.counterparties,
        organizationIndex,
        counterpartyIndex,
        setOrganizationIndex,
        setCounterpartyIndex,
        selectedOrganization,
        selectedCounterparty,
        scopeComplete,
        loading,
        error,
        reload,
    };
}
