import { useCallback, useEffect, useRef } from 'react';
import { sendPresence } from '../api/activeSessions';
import { hasPendingApiWrites } from '../api/http';
import { useMockApi } from '../api/runtime';
// Independent of CSRF session reads and document command state. Telemetry failure
// must not sign the user out, retry a command, or interfere with a role switch.
export function useSessionPresence(enabled: boolean, scope: string | null): () => void {
    const id = useRef<string | null>(null);
    const sequence = useRef(0);
    useEffect(() => {
        if (!enabled || !scope || useMockApi)
            return;
        try {
            id.current ??= crypto.randomUUID();
        }
        catch {
            return;
        }
        const controller = new AbortController();
        let timer: ReturnType<typeof setTimeout>;
        const pulse = async () => {
            const request = new AbortController();
            const abort = () => request.abort();
            controller.signal.addEventListener('abort', abort, { once: true });
            const deadline = setTimeout(abort, 10000);
            try {
                if (!hasPendingApiWrites())
                    await sendPresence(id.current!, ++sequence.current, request.signal);
            }
            catch { /* Best-effort presence only; no authentication/CSRF recovery. */ }
            finally {
                clearTimeout(deadline);
                controller.signal.removeEventListener('abort', abort);
                if (!controller.signal.aborted)
                    timer = setTimeout(() => { void pulse(); }, 30000);
            }
        };
        void pulse();
        return () => { controller.abort(); clearTimeout(timer); };
    }, [enabled, scope]);
    return useCallback(() => {
        if (!id.current || useMockApi)
            return;
        const controller = new AbortController();
        const deadline = setTimeout(() => controller.abort(), 10000);
        // Capture the selected role and CSRF pair before App clears its workspace.
        void sendPresence(id.current, ++sequence.current, controller.signal, true).catch(() => { }).finally(() => clearTimeout(deadline));
    }, []);
}
