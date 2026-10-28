/**
 * The antiforgery request token, held for the lifetime of the page and
 * nowhere else.
 *
 * It is deliberately not in `localStorage`, `sessionStorage`, a cookie the
 * script can read, or a URL: a token that outlives the tab, or that a
 * different tab can read, is a token that outlives the identity it was issued
 * for. Losing it on reload costs one session request and nothing else.
 *
 * The identity is kept beside the token for one reason: when the session
 * endpoint answers with a different actor than the one the current token was
 * issued for, the old token is dropped rather than sent to the server with
 * somebody else's request.
 */
let token: string | null = null;
let issuedFor: string | null = null;
let issuedProfile: string | null = null;
let issuedRole: string | null = null;
let issuedSubstitution471: string | null = null;
/** Identity fence only; the token itself never leaves its in-memory store. */
export function getCsrfSessionIdentity(): {
    actorExternalId: string;
    profileCode: string | null;
    roleCode?: string | null;
    substitutionId471?: string;
} | null {
    return token === null || issuedFor === null
        ? null
        : { actorExternalId: issuedFor, profileCode: issuedProfile, ...(issuedRole === null ? {} : { roleCode: issuedRole }),
            ...(issuedSubstitution471 === null ? {} : { substitutionId471: issuedSubstitution471 }) };
}
/** The token to send with the next write, or `null` when there is none. */
export function getCsrfToken(): string | null {
    return token;
}
/**
 * Records the token that came with a session response.
 *
 * A response for a different actor clears first, so a token issued for the
 * previous identity is never combined with the new one. A response with no
 * token — the state a deployment is in while the protection is off — clears
 * as well, rather than leaving a stale value behind.
 */
export function rememberCsrfToken(actorExternalId: string, nextToken: string | undefined, profileCode?: string, roleCode?: string | null, substitutionId471?: string): void {
    if (issuedFor !== null && issuedFor !== actorExternalId) {
        clearCsrfToken();
    }
    token = nextToken ?? null;
    issuedFor = token === null ? null : actorExternalId;
    issuedProfile = token === null ? null : profileCode ?? null;
    issuedRole = token === null ? null : roleCode ?? null;
    issuedSubstitution471 = token === null ? null : substitutionId471 ?? null;
}
/** Drops the token on sign-out, on a failed session read, and on identity change. */
export function clearCsrfToken(): void {
    token = null;
    issuedFor = null;
    issuedProfile = null;
    issuedRole = null;
    issuedSubstitution471 = null;
}
