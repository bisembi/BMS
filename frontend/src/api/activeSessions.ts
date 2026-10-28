import { apiUrl, fetchWithSafeTransport, fetchSessionPresence, secureReadInit } from './http';
import { isBmsRoleCode } from '../utils/activeRole';
export type ActiveSession = {
    sessionId: string;
    userName: string;
    roleCode: string | null;
    connectedAtUtc: string;
    lastSeenAtUtc: string;
};
export type ActiveSessionsPage = {
    items: ActiveSession[];
    total: number;
    skip: number;
    take: number;
    checkedAtUtc: string;
    activeSeconds: number;
};
const unavailable = 'Монитор состояний временно недоступен.';
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
const date = (v: unknown): v is string => typeof v === 'string' && v.length <= 40 && Number.isFinite(Date.parse(v));
export async function sendPresence(tabId: string, sequence: number, signal: AbortSignal, closed = false): Promise<void> {
    if (!uuid.test(tabId) || !Number.isSafeInteger(sequence) || sequence < 1)
        throw new Error(unavailable);
    const response = closed ? await fetchSessionPresence(tabId, sequence, signal, true) : await fetchSessionPresence(tabId, sequence, signal);
    if (response.status !== 204)
        throw new Error(unavailable);
}
export async function getActiveSessions(search: string, skip: number, take: number, signal?: AbortSignal): Promise<ActiveSessionsPage> {
    if (search.length > 100 || !Number.isSafeInteger(skip) || skip < 0 || skip > 1000000
        || !Number.isSafeInteger(take) || take < 1 || take > 100)
        throw new Error(unavailable);
    const query = new URLSearchParams({ search, skip: String(skip), take: String(take) });
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/administration/active-sessions?${query}`), {
        ...secureReadInit(), signal,
    });
    if (!response.ok)
        throw new Error(response.status === 403 ? 'Нет доступа к монитору состояний в выбранной роли.' : unavailable);
    return publicActiveSessions(await response.json(), skip, take);
}
export function publicActiveSessions(value: unknown, skip: number, take: number): ActiveSessionsPage {
    const invalid = () => new Error(unavailable);
    if (!value || typeof value !== 'object')
        throw invalid();
    const p = value as Record<string, unknown>;
    if (!Array.isArray(p.items) || p.items.length > take || p.skip !== skip || p.take !== take
        || !Number.isSafeInteger(p.total) || (p.total as number) < 0 || (p.total as number) > 4096
        || !date(p.checkedAtUtc) || p.activeSeconds !== 180)
        throw invalid();
    const items = p.items.map((v: unknown) => {
        if (!v || typeof v !== 'object')
            throw invalid();
        const r = v as Record<string, unknown>;
        if (typeof r.sessionId !== 'string' || !uuid.test(r.sessionId) || typeof r.userName !== 'string'
            || !r.userName.trim() || r.userName.length > 512 || (r.roleCode !== null && !isBmsRoleCode(r.roleCode))
            || !date(r.connectedAtUtc) || !date(r.lastSeenAtUtc)
            || Date.parse(r.connectedAtUtc) > Date.parse(r.lastSeenAtUtc)
            || Date.parse(r.lastSeenAtUtc) > Date.parse(p.checkedAtUtc as string))
            throw invalid();
        return { sessionId: r.sessionId, userName: r.userName, roleCode: r.roleCode as string | null,
            connectedAtUtc: r.connectedAtUtc, lastSeenAtUtc: r.lastSeenAtUtc };
    });
    if (items.length !== Math.min(take, Math.max(0, (p.total as number) - skip)))
        throw invalid();
    return { items, total: p.total as number, skip, take, checkedAtUtc: p.checkedAtUtc, activeSeconds: 180 };
}
