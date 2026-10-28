import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useRef, useState } from 'react';
import { getActiveAccruals, getContracts, getCostCenters, getCounterparties, getOrganizations, type ActiveAccrualFilters, type ActiveAccrualPage } from '../api';
import type { ReferenceDataItem } from '../types';
export const activeAccrualLoadError = 'Не удалось загрузить активные управленческие начисления.';
type Query = {
    filters: ActiveAccrualFilters;
    catalogFilters?: Pick<ActiveAccrualFilters, 'organizationId' | 'counterpartyId'>;
    skip: number;
    take: number;
    enabled: boolean;
    reloadToken: number;
};
export type ActiveAccrualReportData = {
    organizations: ReferenceDataItem[];
    costCenters: ReferenceDataItem[];
    counterparties: ReferenceDataItem[];
    contracts: ReferenceDataItem[];
    contractsLoading: boolean;
    page: ActiveAccrualPageState;
    loading: boolean;
    error: string | null;
};
type ActiveAccrualPageState = Omit<ActiveAccrualPage, 'aging'> & {
    aging: ActiveAccrualPage['aging'] | null;
};
const emptyPage = (skip: number, take: number): ActiveAccrualPageState => ({
    items: [],
    skip,
    take,
    total: 0,
    aging: null,
    totals: {
        originalAmountKzt: 0,
        confirmedClosedAmountKzt: 0,
        remainingAmountKzt: 0,
    },
});
export function useActiveAccrualReportData({ filters, catalogFilters = filters, skip, take, enabled, reloadToken, }: Query): ActiveAccrualReportData {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [organizations, setOrganizations] = useState<ReferenceDataItem[]>([]);
    const [costCenters, setCostCenters] = useState<ReferenceDataItem[]>([]);
    const [counterparties, setCounterparties] = useState<ReferenceDataItem[]>([]);
    const [contracts, setContracts] = useState<ReferenceDataItem[]>([]);
    const [page, setPage] = useState<ActiveAccrualPageState>(() => emptyPage(skip, take));
    const [catalogsLoading, setCatalogsLoading] = useState(true);
    const [rowsLoading, setRowsLoading] = useState(enabled);
    const [contractsLoading, setContractsLoading] = useState(false);
    const [catalogError, setCatalogError] = useState<string | null>(null);
    const [rowsError, setRowsError] = useState<string | null>(null);
    const [contractsError, setContractsError] = useState<string | null>(null);
    const catalogSequence = useRef(0);
    const rowsSequence = useRef(0);
    const contractsSequence = useRef(0);
    useWorkspaceRefreshBusy(catalogsLoading || contractsLoading || rowsLoading);
    useEffect(() => {
        const sequence = catalogSequence.current + 1;
        catalogSequence.current = sequence;
        setCatalogsLoading(true);
        setCatalogError(null);
        void Promise.all([getOrganizations(), getCostCenters(), getCounterparties()])
            .then(([organizationCatalog, costCenterCatalog, counterpartyCatalog]) => {
            if (sequence !== catalogSequence.current)
                return;
            setOrganizations(organizationCatalog.items);
            setCostCenters(costCenterCatalog.items);
            setCounterparties(counterpartyCatalog.items);
        })
            .catch(() => {
            if (sequence !== catalogSequence.current)
                return;
            setOrganizations([]);
            setCostCenters([]);
            setCounterparties([]);
            setCatalogError(activeAccrualLoadError);
        })
            .finally(() => {
            if (sequence === catalogSequence.current)
                setCatalogsLoading(false);
        });
        return () => {
            catalogSequence.current += 1;
        };
    }, [reloadToken, workspaceRefresh]);
    useEffect(() => {
        const sequence = contractsSequence.current + 1;
        contractsSequence.current = sequence;
        setContractsError(null);
        if (catalogFilters.organizationId === undefined || catalogFilters.counterpartyId === undefined) {
            setContracts([]);
            setContractsLoading(false);
            return undefined;
        }
        setContractsLoading(true);
        void getContracts(catalogFilters.counterpartyId, catalogFilters.organizationId)
            .then((catalog) => {
            if (sequence === contractsSequence.current)
                setContracts(catalog.items);
        })
            .catch(() => {
            if (sequence !== contractsSequence.current)
                return;
            setContracts([]);
            setContractsError(activeAccrualLoadError);
        })
            .finally(() => {
            if (sequence === contractsSequence.current)
                setContractsLoading(false);
        });
        return () => {
            contractsSequence.current += 1;
        };
    }, [catalogFilters.counterpartyId, catalogFilters.organizationId, reloadToken, workspaceRefresh]);
    useEffect(() => {
        const sequence = rowsSequence.current + 1;
        rowsSequence.current = sequence;
        setPage(emptyPage(skip, take));
        setRowsError(null);
        if (!enabled) {
            setRowsLoading(false);
            return undefined;
        }
        const controller = new AbortController();
        setRowsLoading(true);
        void getActiveAccruals(filters, skip, take, controller.signal)
            .then((nextPage) => {
            if (sequence === rowsSequence.current)
                setPage(nextPage);
        })
            .catch(() => {
            if (sequence === rowsSequence.current) {
                setPage(emptyPage(skip, take));
                setRowsError(activeAccrualLoadError);
            }
        })
            .finally(() => {
            if (sequence === rowsSequence.current)
                setRowsLoading(false);
        });
        return () => {
            controller.abort();
            rowsSequence.current += 1;
        };
    }, [
        workspaceRefresh,
        enabled,
        filters.accrualPeriodFrom,
        filters.accrualPeriodThrough,
        filters.costCenterId,
        filters.counterpartyId,
        filters.contractId,
        filters.detailLevel,
        filters.groupBy,
        filters.organizationId,
        reloadToken,
        skip,
        take,
    ]);
    return {
        organizations,
        costCenters,
        counterparties,
        contracts,
        contractsLoading,
        page,
        loading: catalogsLoading || contractsLoading || rowsLoading,
        error: catalogError ?? contractsError ?? rowsError,
    };
}
