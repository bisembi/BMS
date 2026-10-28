import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useState } from 'react';
import { getCostCenters } from '../api';
import type { ReferenceCatalogErrorReporter } from './useReferenceCatalogError';
import type { ReferenceDataItem } from '../types';
/// Cost centres valid on the planned payment date. An empty date is sent as no
/// date at all, so the server keeps deciding what "current" means.
export function useExpenseRequestCostCenters(plannedPaymentDate: string, reportError: ReferenceCatalogErrorReporter): {
    items: ReferenceDataItem[];
    loading: boolean;
} {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [catalog, setCatalog] = useState<{
        plannedPaymentDate: string;
        items: ReferenceDataItem[];
        loading: boolean;
    }>({ plannedPaymentDate, items: [], loading: true });
    useWorkspaceRefreshBusy(catalog.loading || catalog.plannedPaymentDate !== plannedPaymentDate);
    useEffect(() => {
        let cancelled = false;
        setCatalog({ plannedPaymentDate, items: [], loading: true });
        void getCostCenters(plannedPaymentDate || undefined)
            .then((catalog) => {
            if (!cancelled)
                setCatalog({ plannedPaymentDate, items: catalog.items, loading: false });
        })
            .catch((catalogError) => {
            if (!cancelled) {
                setCatalog((current) => ({ ...current, plannedPaymentDate, loading: false }));
                reportError(catalogError, 'Не удалось загрузить ЦФО.');
            }
        });
        return () => {
            cancelled = true;
        };
    }, [plannedPaymentDate, reportError, workspaceRefresh]);
    return catalog.plannedPaymentDate === plannedPaymentDate
        ? { items: catalog.items, loading: catalog.loading }
        : { items: [], loading: true };
}
