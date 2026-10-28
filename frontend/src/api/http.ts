import { getCsrfSessionIdentity, getCsrfToken } from './csrfToken';
import { devActorId, isDevelopment } from './runtime';
import type { SwitchableBmsAccessProfileCode } from '../types';
const invalidApiBaseUrlMessage = 'Некорректный VITE_API_BASE_URL: укажите URL или относительный путь без query/hash.';
export const safeTransportErrorMessage = 'Не удалось подключиться к API.';
let activeProfileOverride: SwitchableBmsAccessProfileCode | null = null;
let activeRoleOverride: string | null = null;
let activeSubstitutionOverride471: string | null = null;
let roleSwitchPending = false;
let pendingUnsafeRequests = 0;
export function setActiveBmsProfileOverride(profile: SwitchableBmsAccessProfileCode | null): void {
    activeProfileOverride = profile;
}
export function setActiveBmsRoleOverride(role: string | null, substitutionId?: string): void { activeRoleOverride = role; activeSubstitutionOverride471 = substitutionId ?? null; }
/** Own the complete probe/recovery/render handoff, not merely its network call. */
export function beginBmsRoleSwitch(): () => void {
    if (roleSwitchPending || pendingUnsafeRequests > 0)
        throw new Error('Дождитесь завершения отправки изменений.');
    roleSwitchPending = true;
    let released = false;
    return () => {
        if (!released) {
            released = true;
            roleSwitchPending = false;
        }
    };
}
export function hasPendingApiWrites(): boolean {
    return roleSwitchPending || pendingUnsafeRequests > 0;
}
function activeBmsProfileHeaders(): Record<string, string> {
    return { ...(activeProfileOverride === null ? {} : { 'X-Bms-Active-Profile': activeProfileOverride }),
        ...(activeRoleOverride === null ? {} : { 'X-Bms-Active-Role': activeRoleOverride }),
        ...(activeSubstitutionOverride471 === null ? {} : { 'X-Bms-Substitution-Id': activeSubstitutionOverride471 }) };
}
/**
 * Prefixes a protected API route with the optional deployment base. With no
 * setting the existing same-origin `/api` route is preserved, which keeps the
 * Vite proxy and a reverse-proxy deployment identical.
 */
export function apiUrl(path: string): string {
    if (!path.startsWith('/api/')) {
        throw new Error('Некорректный API-маршрут.');
    }
    const configured = import.meta.env.VITE_API_BASE_URL?.trim() ?? '';
    if (configured === '')
        return path;
    if (configured.startsWith('/') && !configured.startsWith('//')) {
        if (configured.includes('?') || configured.includes('#')) {
            throw new Error(invalidApiBaseUrlMessage);
        }
        const relativeBase = configured.replace(/\/+$/u, '');
        return `${relativeBase}${path}`;
    }
    let parsed: URL;
    try {
        parsed = new URL(configured);
    }
    catch {
        throw new Error(invalidApiBaseUrlMessage);
    }
    if ((parsed.protocol !== 'http:' && parsed.protocol !== 'https:')
        || parsed.search !== ''
        || parsed.hash !== ''
        || parsed.username !== ''
        || parsed.password !== '') {
        throw new Error(invalidApiBaseUrlMessage);
    }
    const origin = parsed.toString().replace(/\/+$/u, '');
    return `${origin}${path}`;
}
/// The development-only actor header. Outside a development build this is an
/// empty object, so the header is absent rather than blank.
export function devActorHeaders(): Record<string, string> {
    return isDevelopment ? { 'X-Bms-Actor-Id': devActorId } : {};
}
/// The single request shape for every protected same-origin GET of this API.
///
/// W87 preserved three different shapes inherited from the monolith, three of
/// which pinned no cache mode at all. W90A collapses them: a protected read is
/// never served from a cache and never leaves the origin, so a signed-out or
/// switched actor can never be answered from a previous actor's response.
///
/// Callers that need a method or extra headers spread this first and override
/// afterwards; they must keep the development actor header in their own header
/// object, because a nested spread replaces `headers` wholesale.
export function secureReadInit(): RequestInit & {
    headers: Record<string, string>;
} {
    return {
        cache: 'no-store',
        credentials: 'same-origin',
        headers: {
            ...devActorHeaders(),
            ...activeBmsProfileHeaders(),
        },
    };
}
/// The antiforgery header, or an empty object while no token is held.
///
/// A header carrying the token rather than a form field is what lets the same
/// boundary cover multipart bodies, DELETE and a POST with no body at all:
/// none of the three can carry a hidden field, and none of them needs to.
export function csrfHeaders(): Record<string, string> {
    const token = getCsrfToken();
    return token === null ? {} : { 'X-CSRF-Token': token };
}
/// The same protected-request boundary for commands. Writes still use the
/// read boundary's no-store/same-origin policy; only method, command headers,
/// and an optional body are added by the caller.
///
/// The antiforgery header is added here rather than at each call site, so a
/// write added later is covered by construction. Caller headers are spread
/// afterwards and still win, and neither the body, the idempotency key nor
/// the cancellation signal is touched.
export function secureWriteInit(method: 'POST' | 'PUT' | 'DELETE', headers: Record<string, string>, body?: BodyInit): RequestInit & {
    headers: Record<string, string>;
} {
    const base = secureReadInit();
    if (roleSwitchPending)
        throw new Error('Дождитесь подтверждения выбранной роли.');
    return {
        ...base,
        method,
        headers: {
            ...base.headers,
            ...csrfHeaders(),
            ...headers,
        },
        ...(body === undefined ? {} : { body }),
    };
}
/**
 * The browser's fetch rejection is not a public API contract: test doubles,
 * service workers and platform implementations can put arbitrary text in it.
 * Normalize every non-cancellation rejection at the one network boundary so
 * a diagnostic can never travel from the transport into a facade or UI.
 * Abort remains observable because callers use it to cancel stale requests.
 */
export async function fetchWithSafeTransport(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
    const method = (init?.method ?? (input instanceof Request ? input.method : 'GET')).toUpperCase();
    const isUnsafe = method !== 'GET' && method !== 'HEAD' && method !== 'OPTIONS';
    if (isUnsafe && roleSwitchPending)
        throw new Error('Дождитесь подтверждения выбранной роли.');
    if (isUnsafe)
        pendingUnsafeRequests += 1;
    try {
        return await fetch(input, init);
    }
    catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
            throw error;
        }
        if (error instanceof Error && error.name === 'AbortError') {
            throw error;
        }
        throw new Error(safeTransportErrorMessage);
    }
    finally {
        if (isUnsafe)
            pendingUnsafeRequests -= 1;
    }
}
/** This single telemetry route never changes business data. It carries the
 * ordinary selected-role/CSRF boundary but does not acquire a document-command
 * lock: a background presence request must not block a human role switch. */
export async function fetchSessionPresence(tabId: string, sequence: number, signal: AbortSignal, closed = false): Promise<Response> {
    const init = { ...secureWriteInit('POST', { 'Content-Type': 'application/json' }, JSON.stringify({ tabId, sequence, ...(closed ? { closed: true } : {}) })), signal, ...(closed ? { keepalive: true } : {}) };
    try {
        return await fetch(apiUrl('/api/v1/session/presence'), init);
    }
    catch (error) {
        if (error instanceof Error && error.name === 'AbortError')
            throw error;
        throw new Error(safeTransportErrorMessage);
    }
}
/**
 * Runs one explicitly supplied command request and, only when its caller has
 * positively identified a stale CSRF response, refreshes the session and
 * replays that same request once. Keeping the request factory at the caller
 * means a refreshed in-memory token is read when the retry is built; this
 * helper itself never decides which response is an authorization refusal.
 */
export async function fetchWithOneTimeRefresh(request: () => Promise<Response>, shouldRefresh: (response: Response) => Promise<boolean>, refresh: () => Promise<unknown>): Promise<Response> {
    const originalIdentity = getCsrfSessionIdentity();
    const originalProfile = activeProfileOverride;
    const originalRole = activeRoleOverride;
    const originalSubstitution471 = activeSubstitutionOverride471;
    // A logical command includes the refusal inspection and session refresh,
    // not just its two network writes. Never expose a profile-switch gap.
    pendingUnsafeRequests += 1;
    try {
        const response = await request();
        if (!(await shouldRefresh(response)))
            return response;
        await refresh();
        const refreshedIdentity = getCsrfSessionIdentity();
        if (originalIdentity === null || refreshedIdentity === null
            || originalIdentity.actorExternalId !== refreshedIdentity.actorExternalId
            || originalIdentity.roleCode !== refreshedIdentity.roleCode
            || originalIdentity.substitutionId471 !== refreshedIdentity.substitutionId471
            || (originalIdentity.profileCode !== null
                && originalIdentity.profileCode !== refreshedIdentity.profileCode)
            || originalProfile !== activeProfileOverride || originalRole !== activeRoleOverride
            || originalSubstitution471 !== activeSubstitutionOverride471) {
            throw new Error('Сеанс или профиль изменился. Команда не отправлена повторно.');
        }
        return await request();
    }
    finally {
        pendingUnsafeRequests -= 1;
    }
}
