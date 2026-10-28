import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useRef, useState } from 'react';
import { getBudgetPositionsForRequest } from '../api';
import type { BudgetPosition, BudgetReservationSummary } from '../types';
import { enrichBudgetReservation, sameOpaqueIdentifier } from '../utils/expenseRequestDraft';
type Props = {
    requestId: string | null;
    requestVersion: string | null;
    reservation: BudgetReservationSummary | null;
    enabled: boolean;
};
type Capture = {
    source: BudgetReservationSummary;
    requestId: string;
    requestVersion: string;
    value: BudgetReservationSummary | null;
    positions: readonly BudgetPosition[];
};
type BudgetRefresh = {
    reservation: BudgetReservationSummary | null;
    positions: readonly BudgetPosition[] | null;
};
/** A version and a raw snapshot identify a capture. Enrichment is derived state:
 * it never replaces its source or starts a read loop, and failure never repeats a command. */
export function useExpenseRequestBudgetRefresh({ requestId, requestVersion, reservation, enabled, }: Props): BudgetRefresh {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const identity = useRef({ requestId, requestVersion, reservation, enabled });
    identity.current = { requestId, requestVersion, reservation, enabled };
    const [capture, setCapture] = useState<Capture | null>(null);
    const [loading, setLoading] = useState(false);
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        if (!enabled || requestId === null || requestVersion === null || reservation === null) {
            setLoading(false);
            return;
        }
        let cancelled = false;
        setCapture(null);
        setLoading(true);
        const source = reservation;
        void (async () => {
            try {
                const page = await getBudgetPositionsForRequest(requestId, requestVersion);
                const latest = identity.current;
                if (cancelled || !latest.enabled || latest.reservation !== source
                    || !sameOpaqueIdentifier(latest.requestId ?? '', requestId)
                    || !sameOpaqueIdentifier(latest.requestVersion ?? '', requestVersion)
                    || !sameOpaqueIdentifier(page.expenseRequestId ?? '', requestId)
                    || !sameOpaqueIdentifier(page.expenseRequestVersion ?? '', requestVersion))
                    return;
                setCapture({ source, requestId, requestVersion, value: enrichBudgetReservation(source, page.items), positions: page.items });
            }
            catch {
                // Saved data remains usable; money is explicitly unknown until a verified capture.
            }
            finally {
                if (!cancelled)
                    setLoading(false);
            }
        })();
        return () => { cancelled = true; };
    }, [requestId, requestVersion, reservation, enabled, workspaceRefresh]);
    if (enabled && capture !== null && capture.source === reservation
        && sameOpaqueIdentifier(capture.requestId, requestId ?? '')
        && sameOpaqueIdentifier(capture.requestVersion, requestVersion ?? ''))
        return { reservation: capture.value, positions: capture.positions };
    // Never present an aggregate belonging to an old identity, even if an older adapter
    // put finite aggregate values in its raw snapshot.
    if (reservation === null || !reservation.positions.some(p => p.limitAmountKzt != null || p.availableAmountKzt != null))
        return { reservation, positions: null };
    return { reservation: { ...reservation, positions: reservation.positions.map(p => ({ ...p, limitAmountKzt: null, availableAmountKzt: null })) }, positions: null };
}
