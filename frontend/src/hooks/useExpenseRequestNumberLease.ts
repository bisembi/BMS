import { useCallback, useEffect, useRef, useState } from 'react';
import { changeExpenseRequestNumberLease, type ExpenseRequestNumberLease } from '../api/expenseRequestNumberLease';
import { activeExpenseRequestCreateContexts } from '../api/expenseRequestCreateContexts';
import { numberReservationErrorMessage } from '../utils/uiErrorMessages';
export function useExpenseRequestNumberLease(cfo: string, enabled: boolean, actor: string | null) {
    const [lease, setLease] = useState<ExpenseRequestNumberLease | null>(null);
    const [error, setError] = useState<string | null>(null);
    const current = useRef<{
        cfo: string;
        id: string;
        actor: string | null;
    } | null>(null);
    const inFlight = useRef<Promise<ExpenseRequestNumberLease | null> | null>(null);
    const renew = useCallback(async () => {
        const slot = current.current;
        if (!slot)
            throw new Error('Выберите ЦФО для присвоения номера.');
        if (inFlight.current)
            return inFlight.current;
        const attempt = changeExpenseRequestNumberLease(slot.cfo, slot.id);
        inFlight.current = attempt;
        try {
            const result = await attempt;
            if (current.current === slot) {
                setLease(result);
                setError(null);
            }
            return result;
        }
        catch (failure) {
            if (current.current === slot) {
                setLease(null);
                setError(numberReservationErrorMessage(failure));
            }
            throw failure;
        }
        finally {
            if (inFlight.current === attempt)
                inFlight.current = null;
        }
    }, []);
    useEffect(() => {
        setLease(null);
        setError(null);
        if (!enabled || !cfo || !actor) {
            current.current = null;
            return;
        }
        const slot = { cfo, id: crypto.randomUUID(), actor };
        activeExpenseRequestCreateContexts.add(slot.id);
        current.current = slot;
        inFlight.current = null;
        void renew().catch(() => undefined);
        const timer = window.setInterval(() => { void renew().catch(() => undefined); }, 60000);
        const onPageHide = () => { void changeExpenseRequestNumberLease(slot.cfo, slot.id, true).catch(() => undefined); };
        window.addEventListener('pagehide', onPageHide);
        return () => {
            window.clearInterval(timer);
            window.removeEventListener('pagehide', onPageHide);
            activeExpenseRequestCreateContexts.delete(slot.id);
            if (current.current === slot)
                current.current = null;
            // Order release after a dispatched renewal. No stale completion may keep the slot occupied.
            void (inFlight.current ?? Promise.resolve()).catch(() => undefined)
                .then(() => changeExpenseRequestNumberLease(slot.cfo, slot.id, true)).catch(() => undefined);
        };
    }, [cfo, enabled, actor, renew]);
    return { lease: lease?.costCenterId.toLowerCase() === cfo.toLowerCase() ? lease : null, error, renew,
        currentLeaseId: () => current.current?.id };
}
