import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useMemo, useState } from 'react';
import { getCostCenters, getCounterparties, getCurrencies, getOrganizations } from '../api/referenceData';
import { listExpenseRequests } from '../api/expenseRequestReaders';
import type { ExpenseRequestPage, ExpenseRequestStatus, ReferenceDataItem } from '../types';
export type FinanceFilters = {
    organizationId: string;
    costCenterId: string;
    counterpartyId: string;
    status: ExpenseRequestStatus | '';
    plannedDate: string;
    urgency: '' | 'urgent' | 'regular';
};
export const financePageSize = 25;
export const emptyFinanceFilters: FinanceFilters = {
    organizationId: '', costCenterId: '', counterpartyId: '', status: '', plannedDate: '', urgency: '',
};
export type FinanceCatalogs = {
    organizations: ReferenceDataItem[];
    costCenters: ReferenceDataItem[];
    counterparties: ReferenceDataItem[];
    currencies: ReferenceDataItem[];
};
const emptyCatalogs: FinanceCatalogs = { organizations: [], costCenters: [], counterparties: [], currencies: [] };
export function useFinanceCatalogs(includeCurrencies: boolean) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [state, setState] = useState({ items: emptyCatalogs, loading: true, error: false });
    const [generation, setGeneration] = useState(0);
    useWorkspaceRefreshBusy(state.loading);
    useEffect(() => {
        let active = true;
        setState({ items: emptyCatalogs, loading: true, error: false });
        // The cash-flow catalogue is intentionally not fetched: these workspaces
        // must not reintroduce the full high-volume catalogue into a native select.
        void Promise.all([getOrganizations(), getCostCenters(), getCounterparties(),
            includeCurrencies ? getCurrencies() : Promise.resolve({ items: emptyCatalogs.currencies })])
            .then(([organizations, costCenters, counterparties, currencies]) => {
            if (active)
                setState({ items: { organizations: organizations.items, costCenters: costCenters.items,
                        counterparties: counterparties.items, currencies: currencies.items }, loading: false, error: false });
        }).catch(() => {
            if (active)
                setState({ items: emptyCatalogs, loading: false, error: true });
        });
        return () => { active = false; };
    }, [generation, workspaceRefresh, includeCurrencies]);
    return { ...state, retry: () => setGeneration(value => value + 1) };
}
export function useFinanceRequests(filters: FinanceFilters, skip: number, generation: number, businessColumns: boolean, enabled = true) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const key = JSON.stringify([filters, skip, generation, businessColumns]);
    const query = useMemo(() => ({ ...filters }), [key]); // The complete applied query is the request identity.
    const [state, setState] = useState<{
        key: string;
        page?: ExpenseRequestPage;
        failed?: true;
        loading?: boolean;
    } | null>(null);
    useEffect(() => {
        if (!enabled)
            return;
        const controller = new AbortController();
        setState(previous => previous?.key === key ? { ...previous, failed: undefined, loading: true } : { key, loading: true });
        void listExpenseRequests(skip, financePageSize, query.urgency === '' ? undefined : query.urgency === 'urgent', query.plannedDate || undefined, query.organizationId || undefined, query.status || undefined, query.costCenterId || undefined, query.counterpartyId || undefined, undefined, undefined, undefined, undefined, controller.signal, businessColumns)
            .then(page => {
            if (!controller.signal.aborted)
                setState({ key, page });
        })
            .catch(() => {
            if (!controller.signal.aborted)
                setState({ key, failed: true });
        });
        return () => controller.abort();
    }, [key, query, skip, businessColumns, workspaceRefresh, enabled]);
    // Hide an earlier query synchronously, including the render before cleanup.
    const current = state?.key === key ? state : undefined;
    useWorkspaceRefreshBusy(enabled && (current?.loading === true || (!current?.page && !current?.failed)));
    return { page: current?.page, failed: current?.failed === true, loading: current?.loading === true || (!current?.page && !current?.failed), key };
}
