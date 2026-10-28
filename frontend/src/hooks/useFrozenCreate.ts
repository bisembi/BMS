import { useRef, useState } from 'react';
import { useCommandLock } from '../components/WorkspaceCommandLockContext';
import { createOperationKey } from '../utils/operationKey';
export function useFrozenCreate<T extends object>(entity: string, isUncertain: (error: unknown) => boolean) {
    const command = useCommandLock(entity);
    const attempt = useRef<{
        input: T;
        key: string;
    } | null>(null);
    const unknownRef = useRef(false);
    const [busy, setBusy] = useState(false), [unknown, setUnknown] = useState(false);
    async function run<R>(input: T, write: (input: T, key: string) => Promise<R>) {
        const ticket = command.acquire();
        if (!ticket)
            return { kind: 'inactive' as const };
        let frozen = attempt.current;
        try {
            frozen ??= { input: Object.freeze({ ...input }), key: createOperationKey() };
        }
        catch (error) {
            command.release(ticket);
            return { kind: 'refused' as const, error };
        }
        attempt.current = frozen;
        setBusy(true);
        try {
            const value = await write(frozen.input, frozen.key);
            if (!command.isCurrent(ticket) || attempt.current !== frozen)
                return { kind: 'inactive' as const };
            attempt.current = null;
            unknownRef.current = false;
            setUnknown(false);
            setBusy(false);
            command.release(ticket);
            return { kind: 'confirmed' as const, value };
        }
        catch (error) {
            if (!command.isCurrent(ticket) || attempt.current !== frozen)
                return { kind: 'inactive' as const };
            const retained = unknownRef.current || isUncertain(error);
            unknownRef.current = retained;
            setUnknown(retained);
            setBusy(false);
            if (retained)
                command.retain(ticket);
            else {
                attempt.current = null;
                command.release(ticket);
            }
            return { kind: retained ? 'uncertain' as const : 'refused' as const, error };
        }
    }
    return { run, busy, unknown, blocked: busy || unknown };
}
