import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import type { RoleSelection } from '../utils/activeRole';
import { clearCsrfToken, rememberCsrfToken } from './csrfToken';
import type { CurrentSession } from '../types';
import { publicCurrentSession } from './sessionPublicResponse';
const developmentCurrentSession: CurrentSession = {
    actorExternalId: 'dev-user',
    activeProfileCode: 'BUSINESS',
    availableProfileCodes: ['BUSINESS'],
    isBmsAdministrator: false,
    isBmsIntegrationAdministrator: false,
    canAccessBusinessData: true,
    hasFullBusinessDataAccess: false,
    canCreateExpenseRequest: true,
    canViewBusinessAuditMonitor: false,
};
/**
 * Which session read is the current one.
 *
 * Reads can be requested at any time, and an older one must not have the last
 * word: neither by writing a token a newer read has already replaced, nor by
 * erasing one because the older read failed. Only the newest read touches the
 * token store, and the ordinal is taken at the moment of the call so that
 * ownership follows invocation order rather than completion order.
 */
let latestRead = 0;
/**
 * Real session reads run one at a time.
 *
 * The native witness showed why. Two cold reads leave a page at once, the
 * server issues each of them its own antiforgery pair, and nothing binds the
 * order the browser applies `Set-Cookie` in to the order this client records
 * tokens in: the browser kept the cookie of the last response to arrive while
 * the client kept the token of the newest read it had started, and every write
 * that followed was refused with a complete mismatch.
 *
 * Serializing removes the race instead of picking a winner after two pairs
 * already exist. `IAntiforgery.GetAndStoreTokens` sets a response cookie only
 * "if there is no valid cookie associated with the request", so once the first
 * read has finished, the second one carries that cookie and the server keeps
 * it: one cookie, one newest token, and no ordering left to lose.
 *
 * Two levels, because a page is not the only tab. The Web Locks resource is
 * shared by every same-origin agent, so two tabs starting cold serialize
 * against each other; the promise tail below is the fallback where that API is
 * absent, and it serializes only this realm. THE FALLBACK DOES NOT GUARANTEE
 * THE TWO-TAB CASE - it narrows the exposure to what this client already had.
 *
 * Each caller still gets its own result. Nothing is coalesced: two calls make
 * two requests, and one call's failure is never handed to another caller.
 */
const sessionReadLockName = 'bms-session-read';
/**
 * How long one session read may take, counted from the call rather than from
 * the moment it reaches the network, because waiting behind another read is
 * part of what a caller waits for.
 *
 * A technical bound on this client's own waiting, not a policy about sessions:
 * without it, one hung read would hold the lock for every tab of the origin
 * with nothing able to end it.
 *
 * The Windows/AD challenge may wait for a person to enter credentials. Allow
 * five minutes instead of aborting that request while its browser dialog is
 * still being used. This bounds the request, not the authenticated session.
 */
export const sessionReadDeadlineMs = 300000;
const sessionReadDeadlineMessage = 'Не удалось проверить текущий сеанс (истекло время ожидания).';
/**
 * This client giving up on its own read - never someone else's cancellation.
 *
 * A distinct type rather than a message, because the difference decides
 * whether the token is dropped: an outside `AbortError` says nothing about the
 * session and must leave the token alone, while a deadline is a session this
 * client can no longer prove.
 */
class SessionReadDeadlineError extends Error {
    constructor() {
        super(sessionReadDeadlineMessage);
        this.name = 'SessionReadDeadlineError';
    }
}
interface SessionReadDeadline {
    /** Aborted when the deadline passes, and given to the real fetch. */
    readonly signal: AbortSignal;
    /** Whether this call's deadline has already passed. */
    expired(): boolean;
    /** Clears the timer. Called once, however the read ended. */
    dispose(): void;
}
function startSessionReadDeadline(): SessionReadDeadline {
    const controller = new AbortController();
    // Monotonic elapsed time, so an operating-system clock adjustment can
    // neither expire a healthy read nor extend one past its bound.
    const expiresAt = performance.now() + sessionReadDeadlineMs;
    const timer = setTimeout(() => controller.abort(), sessionReadDeadlineMs);
    return {
        signal: controller.signal,
        // The elapsed time, not merely "the timer callback has run". Those differ:
        // when the read ahead ends because its own deadline aborted it, the queue
        // hands over in the microtasks that follow, which is before the waiting
        // call's own timer callback gets a turn. A flag set only by that callback
        // would let a call whose deadline has genuinely passed start a request and
        // store the token from it - which is the one outcome this bound exists to
        // prevent. Asked at the start of the work and again before the token is
        // remembered, so neither window is open.
        expired: () => controller.signal.aborted || performance.now() >= expiresAt,
        dispose: () => clearTimeout(timer),
    };
}
/**
 * The fallback queue: the next read starts only once the previous one has
 * fully settled.
 *
 * Advanced through failure as well as success, so a rejected, malformed,
 * refused or cancelled read cannot leave the queue stuck behind it. The tail
 * holds no result, so nobody can observe another caller's outcome through it.
 */
let tabLocalTail: Promise<void> = Promise.resolve();
function enqueueTabLocal<T>(work: () => Promise<T>, deadline: SessionReadDeadline): Promise<T> {
    const result = tabLocalTail.then(work, work);
    // The tail keeps following the REAL operation even when the caller below
    // stops waiting for it: the next read must still start after this one has
    // genuinely ended, and a live network read is never cut loose early.
    tabLocalTail = result.then(() => undefined, () => undefined);
    return withDeadline(result, deadline);
}
/**
 * Settles the caller within its own bound while the work continues to own the
 * queue.
 *
 * A caller waiting behind a read that is still settling its own abort would
 * otherwise wait past its deadline, because its turn has not come. It stops
 * waiting; the operation ahead of it is not disturbed, and its own work still
 * refuses to start once expired, so nothing reaches the network afterwards.
 *
 * The Web Locks path needs it too, and for a sharper reason. The request's
 * `signal` rejects a request that is still PENDING, but the specification
 * ignores that signal once the lock has been granted: from then on the request
 * promise settles only when the callback's promise does. A fetch or a body that
 * does not honour an abort would therefore leave its caller waiting with no
 * bound at all - the one thing this deadline exists to prevent. Wrapping the
 * outer request keeps the caller bounded without touching the lock: the
 * callback is never raced, and the real lock stays held until the work settles.
 */
function withDeadline<T>(result: Promise<T>, deadline: SessionReadDeadline): Promise<T> {
    if (deadline.expired())
        return Promise.reject(new SessionReadDeadlineError());
    return new Promise<T>((resolve, reject) => {
        const onDeadline = (): void => reject(new SessionReadDeadlineError());
        deadline.signal.addEventListener('abort', onDeadline, { once: true });
        void result.then(resolve, reject).finally(() => {
            deadline.signal.removeEventListener('abort', onDeadline);
        });
    });
}
/**
 * The one shape of the Web Locks API this module uses, feature-detected.
 *
 * No lock mode is passed: `LockOptions.mode` defaults to `"exclusive"` in the
 * specification, which is the mode this needs, and naming it here would also
 * put a `mode:` literal into a transport module - the one place the shared
 * request boundary reserves for itself.
 */
interface SessionReadLockManager {
    request<T>(name: string, options: {
        signal: AbortSignal;
    }, callback: () => Promise<T>): Promise<T>;
}
function sessionReadLocks(): SessionReadLockManager | null {
    const candidate = (globalThis.navigator as unknown as {
        locks?: SessionReadLockManager;
    } | undefined)?.locks;
    return typeof candidate?.request === 'function' ? candidate : null;
}
/**
 * Runs one read under whichever serialization this browser offers.
 *
 * The deadline's signal is handed to the lock request, so a call that spends
 * its whole deadline waiting is never granted the lock and never reaches the
 * network. Once the lock IS granted the signal is ignored by the API, and an
 * acquired lock is released only when this callback's promise settles - after
 * the real fetch and body have settled, including when they settle because the
 * deadline aborted them. A timer firing never hands the lock to the next tab
 * while this read is still alive.
 *
 * That leaves one gap, and the wrapper below closes it: a fetch or body that
 * never honours the abort would keep the granted request's promise pending for
 * as long as it liked, and with it the caller. `withDeadline` bounds the CALLER
 * only. It wraps the outer request, never the callback, so nothing races the
 * work and nothing releases a lock whose read is still alive.
 */
async function serializeSessionRead<T>(work: () => Promise<T>, deadline: SessionReadDeadline): Promise<T> {
    const locks = sessionReadLocks();
    if (locks === null) {
        return await enqueueTabLocal(work, deadline);
    }
    // Deliberately no local `catch`. A pending request rejected by the signal
    // arrives as an `AbortError`, and only `getCurrentSession` may decide what a
    // cancellation means for the token - here it could not tell this call's own
    // deadline from an outside abort without repeating that judgement.
    return await withDeadline(locks.request(sessionReadLockName, { signal: deadline.signal }, work), deadline);
}
export async function getCurrentSession(selection?: RoleSelection): Promise<CurrentSession> {
    const read = ++latestRead;
    const deadline = startSessionReadDeadline();
    try {
        return await serializeSessionRead(() => readCurrentSessionAsync(read, deadline, selection), deadline);
    }
    catch (error) {
        // The one place the owned deadline is judged, wherever it surfaced: while
        // waiting for the shared lock, while waiting for this realm's queue,
        // during the fetch, or during the body read. Each of those can arrive as
        // an `AbortError`, and an inner handler that treated it as a cancellation
        // would leave a token from a session this client has given up proving.
        //
        // `latestRead` still decides whose token it is, and an abort that is NOT
        // this deadline keeps its existing, non-destructive meaning.
        if (error instanceof SessionReadDeadlineError) {
            dropTokenIfCurrent(read);
            throw error;
        }
        if (deadline.expired() && isCancellation(error)) {
            dropTokenIfCurrent(read);
            throw new SessionReadDeadlineError();
        }
        // Anything else that ended this read before it reached the network - a
        // lock request refused outright, for instance - never passed through the
        // fetch or body handlers, so the current token would otherwise survive a
        // read that failed. `dropTokenIfCurrent` still skips a cancellation and
        // still respects `latestRead`, so neither of those meanings changes.
        dropTokenIfCurrent(read, error);
        throw error;
    }
    finally {
        deadline.dispose();
    }
}
/// One read, once it is this call's turn.
async function readCurrentSessionAsync(read: number, deadline: SessionReadDeadline, selection?: RoleSelection): Promise<CurrentSession> {
    // A call whose deadline passed while it queued starts no network work at all,
    // and rejects on its own rather than leaving anything for the next caller.
    if (deadline.expired()) {
        throw new SessionReadDeadlineError();
    }
    const response = await readSessionAsync(read, deadline, selection);
    if (!response.ok) {
        dropTokenIfCurrent(read);
        throw new Error(`Не удалось проверить текущий сеанс (HTTP ${response.status}).`);
    }
    return await readSessionBodyAsync(response, read, deadline);
}
/// The transport half, so the caller's `!response.ok` block stays a plain
/// guard that throws before anything reads the body. It sits below its caller
/// so the reviewed session adapter keeps the shape the W52 contract reads.
async function readSessionAsync(read: number, deadline: SessionReadDeadline, selection?: RoleSelection): Promise<Response> {
    try {
        const init = secureReadInit();
        const probeHeaders = { ...init.headers };
        if (selection !== undefined)
            delete probeHeaders['X-Bms-Substitution-Id'];
        return await fetchWithSafeTransport(apiUrl('/api/v1/session'), {
            ...init,
            ...(selection === undefined ? {} : { headers: { ...probeHeaders,
                    'X-Bms-Active-Profile': selection.profileCode, 'X-Bms-Active-Role': selection.roleCode,
                    ...(selection.substitutionId ? { 'X-Bms-Substitution-Id': selection.substitutionId } : {}) } }),
            signal: deadline.signal,
        });
    }
    catch (error) {
        // An abort is left as an abort here; `getCurrentSession` decides whether
        // it was this call's own deadline.
        dropTokenIfCurrent(read, error);
        throw error;
    }
}
/// The one place the live payload becomes a session: the public parser, never
/// a cast.
async function parseSessionAsync(response: Response): Promise<CurrentSession> {
    return publicCurrentSession(await response.json());
}
/// The parsing half: a body that is not JSON and a payload that is not the
/// session contract both leave the client without a session it can prove.
async function readSessionBodyAsync(response: Response, read: number, deadline: SessionReadDeadline): Promise<CurrentSession> {
    try {
        const session = await parseSessionAsync(response);
        // A read that outlived its deadline must not remember anything, however
        // late its body arrives: the pair it belongs to is no longer this client's.
        // Checked here because an aborted body does not always reject - the parse
        // can win the race - and a token stored after the deadline would be the
        // one thing this whole correction exists to prevent.
        if (deadline.expired()) {
            throw new SessionReadDeadlineError();
        }
        if (read === latestRead) {
            rememberCsrfToken(session.actorExternalId, session.csrfToken, session.activeProfileCode, session.activeRoleCode, session.activeSubstitutionId471);
        }
        return session;
    }
    catch (error) {
        dropTokenIfCurrent(read, error);
        throw error;
    }
}
/**
 * Signed out, unreachable, unparseable, or not the contract: in every one of
 * those the token in memory no longer belongs to a session this client can
 * prove, so it must not travel with the next write.
 */
function dropTokenIfCurrent(read: number, error?: unknown): void {
    if (error !== undefined && isCancellation(error))
        return;
    if (read !== latestRead)
        return;
    clearCsrfToken();
}
/// A cancelled read is not evidence about the session, so it neither records
/// nor drops a token, and the abort keeps travelling to the caller.
function isCancellation(error: unknown): boolean {
    if (error instanceof DOMException && error.name === 'AbortError')
        return true;
    return error instanceof Error && error.name === 'AbortError';
}
