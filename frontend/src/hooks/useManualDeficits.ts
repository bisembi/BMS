import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import { executeManualDeficit, getManualDeficit, getManualDeficits, isUncertainManualDeficitResult, manualDeficitErrorMessage, manualDeficitReadErrorMessage } from '../api/manualDeficits';
import type { ManualDeficitAttempt, ManualDeficitDetail, ManualDeficitPage, ManualDeficitQuery } from '../api/manualDeficitTypes';
export type ManualDeficitLoad<T> = {
    kind: 'loading';
} | {
    kind: 'error';
    text: string;
} | {
    kind: 'ready';
    data: T;
};
export function useManualDeficitList(query: ManualDeficitQuery, revision: number): ManualDeficitLoad<ManualDeficitPage> {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const key = JSON.stringify([query, revision]);
    const [snapshot, setSnapshot] = useState<{
        key: string;
        state: ManualDeficitLoad<ManualDeficitPage>;
    } | null>(null);
    useEffect(() => {
        const controller = new AbortController();
        setSnapshot({ key, state: { kind: 'loading' } });
        void getManualDeficits(query, controller.signal).then((data) => {
            if (!controller.signal.aborted)
                setSnapshot({ key, state: { kind: 'ready', data } });
        }).catch((error: unknown) => {
            if (!controller.signal.aborted)
                setSnapshot({ key, state: { kind: 'error', text: manualDeficitReadErrorMessage(error) } });
        });
        return () => controller.abort();
    }, [key, query, workspaceRefresh]);
    useWorkspaceRefreshBusy(snapshot?.key !== key || snapshot?.state.kind === 'loading');
    return snapshot?.key === key ? snapshot.state : { kind: 'loading' };
}
export function useManualDeficitDetail(id: string | null, skip: number, revision: number): ManualDeficitLoad<ManualDeficitDetail> {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const key = JSON.stringify([id?.toLowerCase(), skip, revision]);
    const [snapshot, setSnapshot] = useState<{
        key: string;
        state: ManualDeficitLoad<ManualDeficitDetail>;
    } | null>(null);
    useEffect(() => {
        const controller = new AbortController();
        setSnapshot({ key, state: { kind: 'loading' } });
        if (id)
            void getManualDeficit(id, skip, 25, controller.signal).then((data) => {
                if (!controller.signal.aborted)
                    setSnapshot({ key, state: { kind: 'ready', data } });
            }).catch((error: unknown) => {
                if (!controller.signal.aborted)
                    setSnapshot({ key, state: { kind: 'error', text: manualDeficitReadErrorMessage(error) } });
            });
        return () => controller.abort();
    }, [id, key, skip, workspaceRefresh]);
    useWorkspaceRefreshBusy(id !== null && (snapshot?.key !== key || snapshot?.state.kind === 'loading'));
    return snapshot?.key === key ? snapshot.state : { kind: 'loading' };
}
export function useManualDeficitCommand(onCompleted: () => void) {
    const [busy, setBusy] = useState(false);
    const [pending, setPending] = useState<ManualDeficitAttempt | null>(null);
    const [notice, setNotice] = useState<{
        error: boolean;
        text: string;
    } | null>(null);
    const executing = useRef(false);
    const unresolved = useRef<ManualDeficitAttempt | null>(null);
    const alive = useRef(true);
    useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
    const run = useCallback(async (requested: ManualDeficitAttempt) => {
        if (executing.current)
            return;
        executing.current = true;
        const attempt = unresolved.current ?? { ...requested };
        setBusy(true);
        setNotice(null);
        try {
            const result = await executeManualDeficit(attempt);
            if (!alive.current)
                return;
            unresolved.current = null;
            setPending(null);
            const text = result.recovered
                ? 'Восстановлен результат прежней команды. Более поздние изменения не отправлялись.'
                : result.correction.state === 'PendingApproval'
                    ? 'Предложение создано. До решения финансового директора дефицит не изменён.'
                    : result.correction.state === 'ApprovedAndApplied'
                        ? 'Корректировка подтверждена и применена.'
                        : result.correction.state === 'Rejected'
                            ? 'Корректировка отклонена. Дефицит не изменён.'
                            : 'Предложение аннулировано: остаток изменился. Корректировка не применялась.';
            setNotice({ error: false, text });
            onCompleted();
        }
        catch (error: unknown) {
            if (!alive.current)
                return;
            unresolved.current = isUncertainManualDeficitResult(error) ? attempt : null;
            setPending(unresolved.current);
            setNotice({ error: true, text: manualDeficitErrorMessage(error) });
        }
        finally {
            executing.current = false;
            if (alive.current)
                setBusy(false);
        }
    }, [onCompleted]);
    return { busy, pending, notice, run, clearNotice: () => setNotice(null) };
}
