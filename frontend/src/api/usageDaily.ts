import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
export type UsageDailyRow = {
    summaryId: string;
    day: string;
    userName: string;
    firstEntryAtUtc: string;
    lastExitAtUtc: string;
    durationSeconds: number;
    sessionCount: number;
    hasApproximateExit: boolean;
    isActive: boolean;
};
export type UsageDailyPage = {
    items: UsageDailyRow[];
    total: number;
    skip: number;
    take: number;
    checkedAtUtc: string;
};
const unavailable = 'Не удалось загрузить сводку времени BMS.';
const timestamp = (value: unknown): value is string => typeof value === 'string' && value.length <= 40
    && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/u.test(value) && Number.isFinite(Date.parse(value));
const day = (value: unknown): value is string => typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/u.test(value)
    && !value.startsWith('0000') && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 10) === value;
export function parseUsageDaily(value: unknown, skip: number, from: string, through: string): UsageDailyPage {
    if (!value || typeof value !== 'object')
        throw new Error(unavailable);
    const p = value as UsageDailyPage;
    if (!Array.isArray(p.items) || p.skip !== skip || p.take !== 50 || !Number.isSafeInteger(p.total) || p.total < 0
        || !timestamp(p.checkedAtUtc) || p.items.length !== Math.min(50, Math.max(0, p.total - skip)))
        throw new Error(unavailable);
    const keys = new Set<string>();
    for (const row of p.items) {
        if (!row || typeof row !== 'object' || typeof row.summaryId !== 'string'
            || !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/iu.test(row.summaryId)
            || !day(row.day) || row.day < from || row.day > through || typeof row.userName !== 'string'
            || !row.userName.trim() || row.userName.length > 512 || !timestamp(row.firstEntryAtUtc) || !timestamp(row.lastExitAtUtc)
            || Date.parse(row.firstEntryAtUtc) > Date.parse(row.lastExitAtUtc) || Date.parse(row.lastExitAtUtc) > Date.parse(p.checkedAtUtc)
            || Date.parse(row.firstEntryAtUtc) < Date.parse(row.day + 'T00:00:00+05:00')
            || Date.parse(row.lastExitAtUtc) > Date.parse(row.day + 'T00:00:00+05:00') + 86400000
            || !Number.isSafeInteger(row.durationSeconds) || row.durationSeconds < 0 || row.durationSeconds > 86400
            || row.durationSeconds > Math.ceil((Date.parse(row.lastExitAtUtc) - Date.parse(row.firstEntryAtUtc)) / 1000)
            || !Number.isSafeInteger(row.sessionCount) || row.sessionCount < 1
            || typeof row.hasApproximateExit !== 'boolean' || typeof row.isActive !== 'boolean')
            throw new Error(unavailable);
        const key = row.summaryId.toLowerCase() + row.day;
        if (keys.has(key))
            throw new Error(unavailable);
        keys.add(key);
    }
    return { items: p.items.map(({ summaryId, day, userName, firstEntryAtUtc, lastExitAtUtc, durationSeconds, sessionCount, hasApproximateExit, isActive }) => ({ summaryId, day, userName, firstEntryAtUtc, lastExitAtUtc, durationSeconds, sessionCount, hasApproximateExit, isActive })),
        total: p.total, skip, take: 50, checkedAtUtc: p.checkedAtUtc };
}
export async function getUsageDaily(search: string, from: string, through: string, skip: number, signal: AbortSignal): Promise<UsageDailyPage> {
    if (search.length > 100 || !day(from) || !day(through) || from > through
        || (Date.parse(through) - Date.parse(from)) / 86400000 > 366 || !Number.isSafeInteger(skip) || skip < 0 || skip > 1000000)
        throw new Error(unavailable);
    const query = new URLSearchParams({ search, from, through, skip: String(skip), take: '50' });
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/administration/usage-log/daily?' + query), { ...secureReadInit(), signal });
    if (!response.ok)
        throw new Error(unavailable);
    return parseUsageDaily(await response.json(), skip, from, through);
}
