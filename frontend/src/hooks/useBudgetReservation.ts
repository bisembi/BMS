import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useRef, useState } from 'react';
import { getBudgetPositionReservations, type BudgetPositionKey } from '../api';
import type { BudgetReservationPage } from '../types';
export const budgetReservationErrorMessage = 'Не удалось загрузить состав резерва.';
export type BudgetReservationState = {
    page: BudgetReservationPage | null;
    /** The position itself no longer exists, which is not a failure. */
    missing: boolean;
    loading: boolean;
    error: string | null;
};
const idle: BudgetReservationState = {
    page: null,
    missing: false,
    loading: false,
    error: null,
};
/// Reads the composition of one budget position's reserve.
///
/// Every read carries a generation. A response is applied only while its
/// generation is still the current one, so a slow answer for a position the
/// user has already closed - or for a page they have already left - can never
/// overwrite what is on screen. Unmounting invalidates the generation as well,
/// so nothing is set on a component that is gone.
export function useBudgetReservation(key: BudgetPositionKey | null, skip: number, take: number, reloadToken: number): BudgetReservationState {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [state, setState] = useState<BudgetReservationState>(idle);
    const generation = useRef(0);
    const costCenterId = key?.costCenterId ?? null;
    const cashFlowItemId = key?.cashFlowItemId ?? null;
    const budgetMonth = key?.budgetMonth ?? null;
    const expectedFactSnapshotId = key?.expectedFactSnapshotId;
    useWorkspaceRefreshBusy(state.loading);
    useEffect(() => {
        const requestNumber = generation.current + 1;
        generation.current = requestNumber;
        if (costCenterId === null || cashFlowItemId === null || budgetMonth === null) {
            // Nothing is open: drop whatever the previous position left behind.
            setState(idle);
            return () => {
                generation.current += 1;
            };
        }
        setState({ page: null, missing: false, loading: true, error: null });
        void getBudgetPositionReservations({ costCenterId, cashFlowItemId, budgetMonth,
            ...(expectedFactSnapshotId === undefined ? {} : { expectedFactSnapshotId }) }, skip, take)
            .then((page) => {
            if (requestNumber !== generation.current)
                return;
            setState({
                page,
                missing: page === null,
                loading: false,
                error: null,
            });
        })
            .catch(() => {
            if (requestNumber !== generation.current)
                return;
            setState({
                page: null,
                missing: false,
                loading: false,
                error: budgetReservationErrorMessage,
            });
        });
        return () => {
            generation.current += 1;
        };
    }, [budgetMonth, cashFlowItemId, costCenterId, expectedFactSnapshotId, reloadToken, skip, take, workspaceRefresh]);
    return state;
}
