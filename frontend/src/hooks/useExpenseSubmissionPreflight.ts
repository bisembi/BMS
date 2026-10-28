import { useEffect, useRef, useState } from 'react';
import { getExpenseRequestSubmissionPreflight } from '../api';
import { useWorkspaceRefreshRevision } from '../components/WorkspaceRefreshContext';
import type { SubmissionPreflight } from '../types';
import { sameOpaqueIdentifier } from '../utils/expenseRequestDraft';
import { expenseRequestPreflightErrorMessage } from '../utils/uiErrorMessages';
const checkDelayMs = 150;
const timeoutMs = 10000;
const refreshMs = 60000;
// Read-only background verification. Its result belongs to exactly one clean
// saved version and actor. Invalidation is synchronous in render, not deferred
// until effect cleanup, so a late response cannot briefly enable submission.
export function useExpenseSubmissionPreflight(input: {
    id: string | null;
    version: string | null;
    actor: string | null;
    enabled: boolean;
    revision: number;
}) {
    const { id, version, actor, enabled, revision } = input;
    const workspaceRevision = useWorkspaceRefreshRevision();
    const [refresh, setRefresh] = useState(0);
    const lastStarted = useRef(0);
    const key = enabled && id && version
        ? JSON.stringify([id.toLowerCase(), version.toLowerCase(), actor, revision, workspaceRevision, refresh]) : null;
    const live = useRef<{
        key: string | null;
        generation: number;
    }>({ key: null, generation: 0 });
    if (live.current.key !== key)
        live.current = { key, generation: live.current.generation + 1 };
    const generation = live.current.generation;
    const [capture, setCapture] = useState<{
        generation: number;
        result: SubmissionPreflight | null;
        error: string | null;
    } | null>(null);
    useEffect(() => {
        if (!enabled || !id || !version)
            return;
        const update = () => {
            if (document.visibilityState !== 'hidden' && Date.now() - lastStarted.current >= 5000)
                setRefresh(value => value + 1);
        };
        const interval = window.setInterval(update, refreshMs);
        window.addEventListener('focus', update);
        document.addEventListener('visibilitychange', update);
        return () => {
            window.clearInterval(interval);
            window.removeEventListener('focus', update);
            document.removeEventListener('visibilitychange', update);
        };
    }, [enabled, id, version]);
    useEffect(() => {
        if (!key || !id || !version)
            return;
        let cancelled = false;
        const controller = new AbortController();
        let timeout: ReturnType<typeof window.setTimeout> | undefined;
        const timer = window.setTimeout(() => {
            lastStarted.current = Date.now();
            const expired = new Promise<never>((_, reject) => {
                timeout = window.setTimeout(() => {
                    reject(new Error('Проверка готовности временно недоступна. Повторите попытку позднее.'));
                    controller.abort();
                }, timeoutMs);
            });
            void Promise.race([Promise.resolve().then(() => getExpenseRequestSubmissionPreflight(id, controller.signal)), expired])
                .then(result => {
                if (cancelled || live.current.generation !== generation)
                    return;
                if (!sameOpaqueIdentifier(result.requestId, id) || !sameOpaqueIdentifier(result.requestVersion, version)) {
                    setCapture({ generation, result: null,
                        error: 'Сервер вернул проверку для другой или устаревшей версии заявки. Обновите данные и повторите попытку.' });
                    return;
                }
                setCapture({ generation, result, error: null });
            })
                .catch((error: unknown) => {
                if (!cancelled && live.current.generation === generation)
                    setCapture({ generation, result: null, error: expenseRequestPreflightErrorMessage(error) });
            })
                .finally(() => {
                if (timeout !== undefined)
                    window.clearTimeout(timeout);
            });
        }, checkDelayMs);
        return () => {
            cancelled = true;
            controller.abort();
            window.clearTimeout(timer);
            if (timeout !== undefined)
                window.clearTimeout(timeout);
        };
    }, [key, generation, id, version]);
    const current = key && capture?.generation === generation ? capture : null;
    return { result: current?.result ?? null, error: current?.error ?? null,
        checking: Boolean(key && !current) };
}
