import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
export type UsageRow = {
    sessionId: string;
    userName: string;
    enteredAtUtc: string;
    lastSeenAtUtc: string;
    exitedAtUtc: string | null;
    durationSeconds: number;
    state: 'ACTIVE' | 'EXIT' | 'TIMEOUT';
};
export type UsagePage = {
    items: UsageRow[];
    total: number;
    skip: number;
    take: number;
    checkedAtUtc: string;
};
const unavailable = 'Не удалось загрузить журнал времени BMS.';
const validDate = (x: unknown): x is string => typeof x === 'string' && x.length <= 40 && Number.isFinite(Date.parse(x));
export async function getUsageLog(search: string, from: string, through: string, skip: number, signal: AbortSignal): Promise<UsagePage> {
    if (search.length > 100 || !/^\d{4}-\d{2}-\d{2}$/u.test(from) || !/^\d{4}-\d{2}-\d{2}$/u.test(through)
        || !Number.isSafeInteger(skip) || skip < 0 || skip > 1000000)
        throw new Error(unavailable);
    const query = new URLSearchParams({ search, from, through, skip: String(skip), take: '50' });
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/administration/usage-log?${query}`), { ...secureReadInit(), signal });
    if (!response.ok)
        throw new Error(unavailable);
    return parseUsagePage(await response.json(), skip);
}
export function parseUsagePage(value: unknown, skip: number): UsagePage {
    const bad = () => new Error(unavailable);
    if (!value || typeof value !== 'object')
        throw bad();
    const p = value as Record<string, unknown>;
    if (!Array.isArray(p.items) || p.skip !== skip || p.take !== 50 || !Number.isSafeInteger(p.total)
        || (p.total as number) < 0 || !validDate(p.checkedAtUtc)
        || p.items.length !== Math.min(50, Math.max(0, (p.total as number) - skip)))
        throw bad();
    const items = p.items.map((v: unknown): UsageRow => {
        if (!v || typeof v !== 'object')
            throw bad();
        const r = v as Record<string, unknown>;
        if (typeof r.sessionId !== 'string' || !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/iu.test(r.sessionId)
            || typeof r.userName !== 'string' || !r.userName.trim() || r.userName.length > 512
            || !validDate(r.enteredAtUtc) || !validDate(r.lastSeenAtUtc) || (r.exitedAtUtc !== null && !validDate(r.exitedAtUtc))
            || !Number.isSafeInteger(r.durationSeconds) || (r.durationSeconds as number) < 0
            || !['ACTIVE', 'EXIT', 'TIMEOUT'].includes(r.state as string)
            || ((r.state === 'ACTIVE') !== (r.exitedAtUtc === null))
            || Date.parse(r.enteredAtUtc) > Date.parse(r.lastSeenAtUtc)
            || Date.parse(r.lastSeenAtUtc) > Date.parse(p.checkedAtUtc as string)
            || (r.exitedAtUtc !== null && (Date.parse(r.exitedAtUtc as string) < Date.parse(r.lastSeenAtUtc)
                || Date.parse(r.exitedAtUtc as string) > Date.parse(p.checkedAtUtc as string))))
            throw bad();
        return { sessionId: r.sessionId, userName: r.userName, enteredAtUtc: r.enteredAtUtc, lastSeenAtUtc: r.lastSeenAtUtc,
            exitedAtUtc: r.exitedAtUtc as string | null, durationSeconds: r.durationSeconds as number, state: r.state as UsageRow['state'] };
    });
    return { items, total: p.total as number, skip, take: 50, checkedAtUtc: p.checkedAtUtc };
}
