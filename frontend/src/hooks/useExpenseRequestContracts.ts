import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useState } from 'react';
import { getContracts } from '../api';
import type { ReferenceCatalogErrorReporter } from './useReferenceCatalogError';
import type { ReferenceDataItem } from '../types';
export type ExpenseRequestContractScope = {
    organizationId: string;
    counterpartyId: string;
    paymentBasisCode: string;
    isNewCounterparty: boolean;
    isNewContract: boolean;
    currencyCode?: string;
};
/// Contracts of exactly one organisation and counterparty pair. The list is
/// cleared before every attempt and the request is skipped entirely while the
/// scope is incomplete or the form describes a counterparty or contract that
/// does not exist yet.
export function useExpenseRequestContracts(scope: ExpenseRequestContractScope, reportError: ReferenceCatalogErrorReporter): ReferenceDataItem[] {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [contracts, setContracts] = useState<ReferenceDataItem[]>([]);
    const [loading, setLoading] = useState(false);
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        let cancelled = false;
        setContracts([]);
        const canLoadContracts = scope.paymentBasisCode === 'Contract'
            && !scope.isNewCounterparty
            && !scope.isNewContract
            && scope.counterpartyId.trim() !== ''
            && scope.organizationId.trim() !== '';
        if (!canLoadContracts) {
            setLoading(false);
            return () => { cancelled = true; };
        }
        setLoading(true);
        void getContracts(scope.counterpartyId, scope.organizationId)
            .then((catalog) => {
            if (!cancelled)
                setContracts(catalog.items.filter(item => !scope.currencyCode || item.currencyCode === scope.currencyCode));
        })
            .catch((catalogError) => {
            if (!cancelled)
                reportError(catalogError, 'Не удалось загрузить договоры.');
        }).finally(() => {
            if (!cancelled)
                setLoading(false);
        });
        return () => {
            cancelled = true;
        };
    }, [
        workspaceRefresh,
        reportError,
        scope.counterpartyId,
        scope.isNewContract,
        scope.isNewCounterparty,
        scope.organizationId,
        scope.paymentBasisCode,
        scope.currencyCode,
    ]);
    return contracts;
}
