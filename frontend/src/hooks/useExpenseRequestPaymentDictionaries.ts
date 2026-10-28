import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useState } from 'react';
import { getCounterpartyResidencies, getPaymentBases } from '../api';
import type { ReferenceCatalogErrorReporter } from './useReferenceCatalogError';
import type { ReferenceCodeItem } from '../types';
export type ExpenseRequestPaymentDictionaries = {
    paymentBases: ReferenceCodeItem[];
    counterpartyResidencies: ReferenceCodeItem[];
    loading: boolean;
};
/// The two code dictionaries that drive the payment basis fields. They are read
/// together on purpose: the form only makes sense with both, so a single failure
/// leaves both empty instead of a half-applied combination.
export function useExpenseRequestPaymentDictionaries(reportError: ReferenceCatalogErrorReporter): ExpenseRequestPaymentDictionaries {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [paymentBases, setPaymentBases] = useState<ReferenceCodeItem[]>([]);
    const [counterpartyResidencies, setCounterpartyResidencies] = useState<ReferenceCodeItem[]>([]);
    const [loading, setLoading] = useState(true);
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        let cancelled = false;
        setLoading(true);
        void Promise.all([getPaymentBases(), getCounterpartyResidencies()])
            .then(([basisCatalog, residencyCatalog]) => {
            if (cancelled)
                return;
            setPaymentBases(basisCatalog.items);
            setCounterpartyResidencies(residencyCatalog.items);
            setLoading(false);
        })
            .catch((catalogError) => {
            if (!cancelled) {
                setLoading(false);
                reportError(catalogError, 'Не удалось загрузить виды основания и резидентности.');
            }
        });
        return () => {
            cancelled = true;
        };
    }, [reportError, workspaceRefresh]);
    return { paymentBases, counterpartyResidencies, loading };
}
