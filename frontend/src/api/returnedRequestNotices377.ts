import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { isPublicExpenseRequestText, isPublicExpenseRequestUuid, isPublicExpenseRequestTimestamp } from './expenseRequestPublicResponse';
export type ReturnedRequestNotice377 = {
    id: string;
    expenseRequestId: string;
    number: string;
    organizationName: string;
    costCenterName: string;
    returnedAtUtc: string;
    reason: string | null;
};
export type ReturnedRequestNoticePage377 = {
    items: ReturnedRequestNotice377[];
    skip: number;
    take: number;
    total: number;
};
const record = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const invalid = () => Error('Не удалось загрузить уведомления о возврате заявок.');
export function publicReturnedRequestNotices377(value: unknown, skip: number, take: number): ReturnedRequestNoticePage377 {
    if (!record(value) || Object.keys(value).some(k => !['items', 'skip', 'take', 'total'].includes(k)) || value.skip !== skip || value.take !== take
        || !Number.isSafeInteger(value.total) || (value.total as number) < 0 || !Array.isArray(value.items)
        || value.items.length !== Math.min(take, Math.max(0, (value.total as number) - skip)))
        throw invalid();
    const ids = new Set<string>();
    for (const row of value.items) {
        if (!record(row) || Object.keys(row).length !== 7 || Object.keys(row).some(k => !['id', 'expenseRequestId', 'number', 'organizationName', 'costCenterName', 'returnedAtUtc', 'reason'].includes(k))
            || !isPublicExpenseRequestUuid(row.id) || !isPublicExpenseRequestUuid(row.expenseRequestId) || !isPublicExpenseRequestText(row.number, 150)
            || !isPublicExpenseRequestText(row.organizationName, 500) || !isPublicExpenseRequestText(row.costCenterName, 500)
            || !isPublicExpenseRequestTimestamp(row.returnedAtUtc) || !(row.reason === null || typeof row.reason === 'string' && row.reason.length <= 1000 && !/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/u.test(row.reason))
            || ids.has(row.id.toLowerCase()))
            throw invalid();
        ids.add(row.id.toLowerCase());
    }
    return value as ReturnedRequestNoticePage377;
}
export async function listReturnedRequestNotices377(skip = 0, take = 25, signal?: AbortSignal): Promise<ReturnedRequestNoticePage377> {
    if (!Number.isSafeInteger(skip) || skip < 0 || !Number.isInteger(take) || take < 1 || take > 100 || skip > 2147483647 - take)
        throw invalid();
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/notifications/returned-requests?skip=${skip}&take=${take}`), { ...secureReadInit(), signal });
    if (response.status !== 200)
        throw invalid();
    return publicReturnedRequestNotices377(await response.json(), skip, take);
}
