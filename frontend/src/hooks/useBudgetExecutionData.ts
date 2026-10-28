import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useRef, useState } from 'react';
import { budgetTotalsForItems, getBudgetPositionsForExecution, getCostCenters } from '../api';
import { useCashFlowItemOptions } from './useCashFlowItemOptions';
import type { BudgetPosition, BudgetPositionPage, ReferenceDataItem } from '../types';
export const budgetExecutionErrorMessage = 'Не удалось загрузить исполнение бюджета.';
export type BudgetExecutionQuery = {
    structure?: 'current' | 'approved';
    costCenterId?: string;
    cashFlowItemId?: string;
    budgetMonth?: string;
    skip: number;
    take: number;
    reloadToken: number;
};
export type BudgetExecutionData = {
    costCenters: ReferenceDataItem[];
    cashFlowItems: ReferenceDataItem[];
    page: BudgetPositionPage;
    loading: boolean;
    error: string | null;
    cashFlowItemQuery: string;
    setCashFlowItemQuery: (query: string) => void;
};
/// Loading, an empty area and a failure all start from here, so a stale total
/// can never stay on screen beside rows that are gone.
const emptyPage = (skip: number, take: number): BudgetPositionPage => ({
    items: [],
    skip,
    take,
    total: 0,
    expenseRequestId: null,
    totals: budgetTotalsForItems([]),
});
/// The two catalogues and the position rows have independent generations. A
/// late catalogue response therefore cannot overwrite rows (or the other
/// catalogue), and every reader is invalidated on unmount.
export function useBudgetExecutionData({ structure, costCenterId, cashFlowItemId, budgetMonth, skip, take, reloadToken, }: BudgetExecutionQuery): BudgetExecutionData {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [costCenters, setCostCenters] = useState<ReferenceDataItem[]>([]);
    const [page, setPage] = useState<BudgetPositionPage>(() => emptyPage(skip, take));
    const [costCentersLoading, setCostCentersLoading] = useState(true);
    const [rowsLoading, setRowsLoading] = useState(true);
    const [costCenterError, setCostCenterError] = useState<string | null>(null);
    const [rowsError, setRowsError] = useState<string | null>(null);
    const costCenterSequence = useRef(0);
    const rowsSequence = useRef(0);
    const factCapture = useRef<{
        area: string;
        snapshotId?: string;
    }>({ area: '' });
    const cashFlowItems = useCashFlowItemOptions([
        ...(cashFlowItemId === undefined ? [] : [cashFlowItemId]),
        ...page.items.map((item) => item.cashFlowItemId),
    ], reloadToken);
    useWorkspaceRefreshBusy(costCentersLoading || cashFlowItems.loading || rowsLoading);
    useEffect(() => {
        const requestNumber = costCenterSequence.current + 1;
        costCenterSequence.current = requestNumber;
        setCostCentersLoading(true);
        setCostCenterError(null);
        void getCostCenters()
            .then((catalog) => {
            if (requestNumber === costCenterSequence.current)
                setCostCenters(catalog.items);
        })
            .catch(() => {
            if (requestNumber === costCenterSequence.current) {
                setCostCenters([]);
                setCostCenterError(budgetExecutionErrorMessage);
            }
        })
            .finally(() => {
            if (requestNumber === costCenterSequence.current)
                setCostCentersLoading(false);
        });
        return () => {
            costCenterSequence.current += 1;
        };
    }, [reloadToken, workspaceRefresh]);
    useEffect(() => {
        const requestNumber = rowsSequence.current + 1;
        rowsSequence.current = requestNumber;
        setRowsLoading(true);
        setRowsError(null);
        const area = JSON.stringify([structure, costCenterId, cashFlowItemId, budgetMonth, reloadToken, workspaceRefresh]);
        if (factCapture.current.area !== area)
            factCapture.current = { area };
        const expectedFactSnapshotId = factCapture.current.snapshotId;
        void getBudgetPositionsForExecution({ costCenterId, cashFlowItemId, budgetMonth,
            ...(structure === undefined ? {} : { structure }),
            ...(expectedFactSnapshotId === undefined ? {} : { expectedFactSnapshotId }) }, skip, take)
            .then((nextPage) => {
            if (requestNumber === rowsSequence.current) {
                factCapture.current = { area, snapshotId: nextPage.factSource?.snapshotId };
                setPage(nextPage);
            }
        })
            .catch(() => {
            if (requestNumber === rowsSequence.current) {
                setPage(emptyPage(skip, take));
                setRowsError(budgetExecutionErrorMessage);
            }
        })
            .finally(() => {
            if (requestNumber === rowsSequence.current)
                setRowsLoading(false);
        });
        return () => {
            rowsSequence.current += 1;
        };
    }, [structure, budgetMonth, cashFlowItemId, costCenterId, reloadToken, skip, take, workspaceRefresh]);
    return {
        costCenters,
        cashFlowItems: cashFlowItems.items,
        page,
        loading: costCentersLoading || cashFlowItems.loading || rowsLoading,
        error: costCenterError ?? cashFlowItems.error ?? rowsError,
        cashFlowItemQuery: cashFlowItems.query,
        setCashFlowItemQuery: cashFlowItems.setQuery,
    };
}
export type { BudgetPosition };
