import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useRef, useState } from 'react';
import { getContracts } from '../api';
import type { ReferenceDataItem } from '../types';
export type ExpenseRequestNsiContractScope = {
    /** The organisation of the request that is waiting, as the card loaded it. */
    organizationId: string;
    /**
     * The counterparty the contract must belong to: the imported one an NSI
     * specialist has just chosen, or the counterparty the request already has
     * when only its contract is new.
     */
    counterpartyId: string;
    /** Whether a contract has to be chosen at all. */
    enabled: boolean;
};
export type ExpenseRequestNsiContracts = {
    contracts: ReferenceDataItem[];
    loading: boolean;
    error: string | null;
};
export const nsiContractsUnavailableMessage = 'Не удалось загрузить договоры НСИ.';
/**
 * W562-R. The imported contracts an NSI specialist may link a waiting request
 * to.
 *
 * The draft form deliberately loads no contracts while a request claims a new
 * counterparty or a new contract - there is nothing to choose from for an
 * object that does not exist yet - and that rule stays exactly as it is. The
 * confirmation is the opposite situation: the object now exists in the
 * reference data, and this is the moment the request is pointed at it. So the
 * panel reads its own narrow list, for one organisation and one counterparty,
 * through the same reference endpoint the form uses.
 */
export function useExpenseRequestNsiContracts(scope: ExpenseRequestNsiContractScope): ExpenseRequestNsiContracts {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [contracts, setContracts] = useState<ReferenceDataItem[]>([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    // A scope that changes faster than the network answers must not be described
    // by the answer to the previous question.
    const sequence = useRef(0);
    const { organizationId, counterpartyId, enabled } = scope;
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        const current = sequence.current + 1;
        sequence.current = current;
        setContracts([]);
        setError(null);
        if (!enabled
            || organizationId.trim() === ''
            || counterpartyId.trim() === '') {
            setLoading(false);
            return;
        }
        setLoading(true);
        let cancelled = false;
        void getContracts(counterpartyId, organizationId)
            .then((catalog) => {
            if (cancelled || sequence.current !== current)
                return;
            setContracts(catalog.items);
            setLoading(false);
        })
            .catch(() => {
            // The reason is dropped on purpose: a server failure text never
            // reaches the screen, and an outage is not an empty catalogue.
            if (cancelled || sequence.current !== current)
                return;
            setContracts([]);
            setError(nsiContractsUnavailableMessage);
            setLoading(false);
        });
        return () => {
            cancelled = true;
        };
    }, [counterpartyId, enabled, organizationId, workspaceRefresh]);
    return { contracts, loading, error };
}
