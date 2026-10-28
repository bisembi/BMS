/// Idempotency keys and their sessionStorage recovery record. A pending command
/// keeps its key across retries so a replayed save resolves the original
/// command instead of creating a second one.
///
/// W266: the store is sessionStorage rather than localStorage, and the
/// difference is the point. A key kept in localStorage would outlive the tab
/// and the session it belonged to, so a later visit could resolve a command
/// nobody is still making. Session scope ends the key with the session.
export type PendingCommand = {
    fingerprint: string | null;
    key: string;
};
import { activeExpenseRequestCreateContexts } from './expenseRequestCreateContexts';
type PendingCommandSelection = {
    command: PendingCommand;
    requestedFingerprint: string;
    requiresResolution: boolean;
};
const pendingCommandKeys = new Map<string, PendingCommand>();
const pendingCommandStoragePrefix = 'bms:pending-command:';
const pendingCommandStorageVersion = 1;
export const expenseRequestCopyPolicyVersion = 'copy-policy-v1';
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
async function requestFingerprint(body: unknown): Promise<string> {
    if (!crypto.subtle) {
        throw new Error('Браузер не поддерживает безопасную проверку повторного сохранения. Обновите браузер и повторите попытку.');
    }
    const source = new TextEncoder().encode(JSON.stringify(body));
    const digest = await crypto.subtle.digest('SHA-256', source);
    return [...new Uint8Array(digest)]
        .map((value) => value.toString(16).padStart(2, '0'))
        .join('');
}
export async function selectPendingCommand(scope: string, body: unknown): Promise<PendingCommandSelection> {
    const requestedFingerprint = await requestFingerprint(body);
    const pending = pendingCommandKeys.get(scope) ?? readPendingCommand(scope);
    if (pending) {
        pendingCommandKeys.set(scope, pending);
        return {
            command: pending,
            requestedFingerprint,
            requiresResolution: pending.fingerprint !== requestedFingerprint,
        };
    }
    const command = { fingerprint: requestedFingerprint, key: crypto.randomUUID() };
    if (!writePendingCommand(scope, command)) {
        throw new Error('Не удалось безопасно сохранить ключ операции в текущей сессии. Новая команда не отправлена.');
    }
    pendingCommandKeys.set(scope, command);
    return { command, requestedFingerprint, requiresResolution: false };
}
export function adoptPendingFingerprint(scope: string, command: PendingCommand, fingerprint: string): PendingCommand {
    const updated = { key: command.key, fingerprint };
    if (!writePendingCommand(scope, updated)) {
        throw new Error('Не удалось безопасно обновить состояние повторного сохранения. Новая команда не отправлена.');
    }
    pendingCommandKeys.set(scope, updated);
    return updated;
}
export function clearPendingCommand(scope: string, key: string) {
    if (pendingCommandKeys.get(scope)?.key === key) {
        pendingCommandKeys.delete(scope);
        removePendingCommand(scope);
    }
}
/** Read only: do not allocate a key just to inspect an interrupted submission. */
export function pendingExpenseRequestSubmit(id: string): {
    scope: string;
    command: PendingCommand;
} | null {
    const scope = `expense-request:submit:${id.toLowerCase()}`;
    const command = pendingCommandKeys.get(scope) ?? readPendingCommand(scope);
    if (!command)
        return null;
    pendingCommandKeys.set(scope, command);
    return { scope, command };
}
export function createDraftCommandScope(leaseId?: string): string {
    return 'expense-request:create-draft' + (leaseId ? `:${leaseId}` : '');
}
export function pendingCreateDraftCommand(preferredLeaseId?: string): {
    scope: string;
    command: PendingCommand;
} | null {
    const preferredScope = createDraftCommandScope(preferredLeaseId);
    const scopes = new Set([preferredScope, 'expense-request:create-draft', ...pendingCommandKeys.keys()]);
    // Reload/closed-form recovery: preserve the old key even though its in-memory form is gone.
    for (let index = 0; index < window.sessionStorage.length; index++) {
        const key = window.sessionStorage.key(index);
        if (key?.startsWith(pendingCommandStoragePrefix + 'expense-request:create-draft'))
            scopes.add(key.slice(pendingCommandStoragePrefix.length));
    }
    const ordered = [preferredScope, ...[...scopes].filter((scope) => scope !== preferredScope).sort()];
    for (const scope of ordered) {
        if (scope !== 'expense-request:create-draft' && !scope.startsWith('expense-request:create-draft:'))
            continue;
        if (scope !== preferredScope && activeExpenseRequestCreateContexts.has(scope.slice('expense-request:create-draft:'.length)))
            continue;
        const pending = pendingCommandKeys.get(scope) ?? readPendingCommand(scope);
        if (pending) {
            pendingCommandKeys.set(scope, pending);
            return { scope, command: pending };
        }
    }
    return null;
}
export function movePendingCreateDraftContext(oldScope: string, newScope: string, command: PendingCommand): void {
    if (oldScope === newScope)
        return;
    const existing = pendingCommandKeys.get(newScope) ?? readPendingCommand(newScope);
    if (existing && existing.key !== command.key)
        throw new Error('В этой форме уже есть другая неподтверждённая команда.');
    if (!writePendingCommand(newScope, command))
        throw new Error('Не удалось сохранить исходный ключ операции. Новая команда не отправлена.');
    pendingCommandKeys.set(newScope, command);
    clearPendingCommand(oldScope, command.key);
}
function readPendingCommand(scope: string): PendingCommand | null {
    let stored: string | null;
    try {
        stored = window.sessionStorage.getItem(`${pendingCommandStoragePrefix}${scope}`);
    }
    catch {
        return null;
    }
    if (!stored)
        return null;
    try {
        const parsed: unknown = JSON.parse(stored);
        if (isRecord(parsed)
            && parsed.version === pendingCommandStorageVersion
            && typeof parsed.key === 'string'
            && parsed.key.length > 0
            && parsed.key.length <= 127
            && typeof parsed.fingerprint === 'string'
            && /^[a-f0-9]{64}$/.test(parsed.fingerprint)) {
            return { key: parsed.key, fingerprint: parsed.fingerprint };
        }
        throw new Error('invalid pending command');
    }
    catch (error) {
        // Previous frontend versions stored only the opaque key. It can still be
        // reconciled safely; the current payload must not be assumed to match it.
        if (!stored.startsWith('{') && stored.length <= 127) {
            return { key: stored, fingerprint: null };
        }
        throw new Error('Состояние предыдущего сохранения повреждено. Обновите страницу и обратитесь к администратору; новая команда не отправлена.', { cause: error });
    }
}
function writePendingCommand(scope: string, command: PendingCommand): boolean {
    try {
        const storageKey = `${pendingCommandStoragePrefix}${scope}`;
        const serialized = JSON.stringify({
            version: pendingCommandStorageVersion,
            key: command.key,
            fingerprint: command.fingerprint,
        });
        window.sessionStorage.setItem(storageKey, serialized);
        return window.sessionStorage.getItem(storageKey) === serialized;
    }
    catch {
        return false;
    }
}
function removePendingCommand(scope: string) {
    try {
        window.sessionStorage.removeItem(`${pendingCommandStoragePrefix}${scope}`);
    }
    catch {
        // The successful response is still reflected by clearing the in-memory command.
    }
}
/** Read-only guard: observing an unresolved attempt never adopts, clears or changes its key. */
export function hasPendingExpenseRequestCommand(requestId: string | null): boolean {
    const normalized = requestId?.toLowerCase() ?? null;
    const matches = (scope: string) => normalized === null
        ? scope === 'expense-request:create-draft' || scope.startsWith('expense-request:create-draft:')
        : scope.startsWith('expense-request:') && scope.toLowerCase().split(':').includes(normalized);
    if ([...pendingCommandKeys.keys()].some(matches))
        return true;
    try {
        for (let index = 0; index < window.sessionStorage.length; index += 1) {
            const key = window.sessionStorage.key(index);
            if (key?.startsWith(pendingCommandStoragePrefix) && matches(key.slice(pendingCommandStoragePrefix.length)))
                return true;
        }
        return false;
    }
    catch {
        return true;
    } // Cannot prove that refresh is safe when recovery storage is unavailable.
}
