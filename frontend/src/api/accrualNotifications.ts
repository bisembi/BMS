import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { isPublicExpenseRequestDate, isPublicExpenseRequestMoney, isPublicExpenseRequestText, isPublicExpenseRequestTimestamp, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
export type AccrualNotification = {
    id: string;
    expenseRequestId: string;
    organizationName: string;
    createdAtUtc: string;
    fullyPaidAtUtc: string;
    paymentBusinessDate: string;
    asOfDate: string;
    ageDays: number;
    redFromDays: number;
    remainingAmountKzt: number;
};
export type AccrualNotificationPage = {
    total: number;
    skip: number;
    take: number;
    items: AccrualNotification[];
};
export type AccrualNotificationRunStatus = 'disabled' | 'neverRun' | 'running' | 'succeeded' | 'failed';
export type AccrualNotificationMonitor = {
    controlEnabled: boolean;
    lastRunStatus: AccrualNotificationRunStatus;
    lastAttemptAtUtc: string | null;
    lastSuccessfulAtUtc: string | null;
    checkedRequests: number;
    createdNotifications: number;
    failedRequests: number;
    missingInitiators: number;
    missingAccountants: number;
};
const invalid = 'Некорректный ответ API уведомлений.';
const unavailable = 'Не удалось загрузить уведомления о начислениях.';
const int32 = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 2147483647;
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const exact = (value: Record<string, unknown>, keys: readonly string[]) => Object.keys(value).length === keys.length && keys.every((key) => Object.hasOwn(value, key));
const utc = (value: unknown): value is string => isPublicExpenseRequestTimestamp(value) && /(?:\.\d{1,3})?(?:Z|\+00:00)$/u.test(value)
    && value.slice(0, 4) !== '0000'
    && new Date(value).toISOString().slice(0, 19) === value.slice(0, 19)
    && !/\.\d{4,}/u.test(value);
const date = (value: unknown): value is string => isPublicExpenseRequestDate(value) && value.slice(0, 4) !== '0000';
const nullableUtc = (value: unknown): value is string | null => value === null || utc(value);
const day = (value: string) => Date.parse(value + 'T00:00:00Z') / 86400000;
function notification(value: unknown): AccrualNotification {
    if (!record(value) || !exact(value, ['id', 'expenseRequestId', 'organizationName', 'createdAtUtc',
        'fullyPaidAtUtc', 'paymentBusinessDate', 'asOfDate', 'ageDays', 'redFromDays', 'remainingAmountKzt'])
        || !isPublicExpenseRequestUuid(value.id) || !isPublicExpenseRequestUuid(value.expenseRequestId)
        || !isPublicExpenseRequestText(value.organizationName, 500)
        || !utc(value.createdAtUtc) || !utc(value.fullyPaidAtUtc)
        || Date.parse(value.createdAtUtc) < Date.parse(value.fullyPaidAtUtc)
        || !date(value.paymentBusinessDate) || !date(value.asOfDate)
        || !int32(value.ageDays) || !int32(value.redFromDays) || value.redFromDays === 0
        || value.ageDays < value.redFromDays || day(value.asOfDate) - day(value.paymentBusinessDate) !== value.ageDays
        || !isPublicExpenseRequestMoney(value.remainingAmountKzt, false)
        || !Number.isSafeInteger(Math.round(value.remainingAmountKzt * 100))
        || Math.round(value.remainingAmountKzt * 100) / 100 !== value.remainingAmountKzt) {
        throw new Error(invalid);
    }
    return {
        id: value.id, expenseRequestId: value.expenseRequestId, organizationName: value.organizationName,
        createdAtUtc: value.createdAtUtc, fullyPaidAtUtc: value.fullyPaidAtUtc,
        paymentBusinessDate: value.paymentBusinessDate, asOfDate: value.asOfDate,
        ageDays: value.ageDays, redFromDays: value.redFromDays, remainingAmountKzt: value.remainingAmountKzt,
    };
}
export function publicAccrualNotificationPage(value: unknown, skip: number, take: number): AccrualNotificationPage {
    if (!int32(skip) || !int32(take) || take < 1 || take > 100 || skip > 2147483647 - take
        || !record(value) || !exact(value, ['total', 'skip', 'take', 'items'])
        || !int32(value.total) || value.skip !== skip || value.take !== take || !Array.isArray(value.items)
        || value.items.length !== Math.min(take, Math.max(0, value.total - skip)))
        throw new Error(invalid);
    const items = Array.from(value.items, notification);
    if (new Set(items.map((item) => item.id.toLowerCase())).size !== items.length)
        throw new Error(invalid);
    return { total: value.total, skip, take, items };
}
function runStatus(value: unknown): value is AccrualNotificationRunStatus {
    return value === 'disabled' || value === 'neverRun' || value === 'running'
        || value === 'succeeded' || value === 'failed';
}
export function publicAccrualNotificationMonitor(value: unknown): AccrualNotificationMonitor {
    if (!record(value) || !exact(value, ['controlEnabled', 'lastRunStatus', 'lastAttemptAtUtc',
        'lastSuccessfulAtUtc', 'checkedRequests', 'createdNotifications', 'failedRequests',
        'missingInitiators', 'missingAccountants'])
        || typeof value.controlEnabled !== 'boolean' || !runStatus(value.lastRunStatus)
        || !nullableUtc(value.lastAttemptAtUtc) || !nullableUtc(value.lastSuccessfulAtUtc)
        || !int32(value.checkedRequests) || !int32(value.createdNotifications)
        || !int32(value.failedRequests) || !int32(value.missingInitiators) || !int32(value.missingAccountants)
        || (value.checkedRequests === 0 && value.createdNotifications !== 0)
        || (value.lastRunStatus === 'running'
            && (value.checkedRequests !== 0 || value.createdNotifications !== 0 || value.failedRequests !== 0))
        || (value.lastRunStatus === 'failed' && (value.failedRequests === 0
            || (value.checkedRequests === 0 ? value.failedRequests !== 1 : value.failedRequests > value.checkedRequests)))
        || value.controlEnabled === (value.lastRunStatus === 'disabled')
        || (value.lastRunStatus === 'succeeded' && (value.lastSuccessfulAtUtc === null
            || value.lastAttemptAtUtc === null || value.failedRequests !== 0))
        || ((value.lastRunStatus === 'running' || value.lastRunStatus === 'failed') && value.lastAttemptAtUtc === null)
        || ((value.lastRunStatus === 'neverRun' || value.lastRunStatus === 'disabled')
            && (value.lastAttemptAtUtc !== null || value.lastSuccessfulAtUtc !== null
                || value.checkedRequests !== 0 || value.createdNotifications !== 0 || value.failedRequests !== 0))) {
        throw new Error(invalid);
    }
    return {
        controlEnabled: value.controlEnabled, lastRunStatus: value.lastRunStatus,
        lastAttemptAtUtc: value.lastAttemptAtUtc, lastSuccessfulAtUtc: value.lastSuccessfulAtUtc,
        checkedRequests: value.checkedRequests, createdNotifications: value.createdNotifications,
        failedRequests: value.failedRequests, missingInitiators: value.missingInitiators,
        missingAccountants: value.missingAccountants,
    };
}
function invalidJson(error: unknown): null {
    if (record(error) && error.name === 'AbortError')
        throw error;
    // Invalid JSON is rejected by the same public parser as any invalid body.
    return null;
}
export async function getAccrualNotifications(skip = 0, take = 25, signal?: AbortSignal): Promise<AccrualNotificationPage> {
    if (!int32(skip) || !int32(take) || take < 1 || take > 100 || skip > 2147483647 - take)
        throw new Error(invalid);
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/notifications/accruals')
        + '?' + new URLSearchParams({ skip: String(skip), take: String(take) }), { ...secureReadInit(), signal });
    if (!response.ok)
        throw new Error(unavailable);
    return publicAccrualNotificationPage(await response.json().catch(invalidJson), skip, take);
}
export async function getAccrualNotificationMonitor(signal?: AbortSignal): Promise<AccrualNotificationMonitor> {
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/integration-monitor/accrual-notifications'), { ...secureReadInit(), signal });
    if (!response.ok)
        throw new Error('Не удалось проверить контроль уведомлений.');
    return publicAccrualNotificationMonitor(await response.json().catch(invalidJson));
}
