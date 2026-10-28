import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { publicAccrualNotificationPage, type AccrualNotification } from './accrualNotifications';
import { isPublicExpenseRequestText, isPublicExpenseRequestTimestamp, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
export type AdministrativeAccrualNotification = AccrualNotification & {
    recipientCode: string;
    recipientName: string;
    recipientIsActive: boolean;
    recipientWasInitiator: boolean;
    recipientWasResponsibleAccountant: boolean;
};
export type NotificationIssueCode = 'NO_RESPONSIBLE_ACCOUNTANT' | 'INITIATOR_MAPPING_UNAVAILABLE';
export type AccrualNotificationIssue = {
    expenseRequestId: string;
    organizationName: string;
    code: NotificationIssueCode;
    firstDetectedAtUtc: string;
    lastDetectedAtUtc: string;
};
export type NotificationAdministrationPage<T> = {
    total: number;
    skip: number;
    take: number;
    items: T[];
};
const invalid = 'Некорректный ответ служебного API уведомлений.';
const int32 = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 2147483647;
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const exact = (value: Record<string, unknown>, keys: readonly string[]) => Object.keys(value).length === keys.length && keys.every((key) => Object.hasOwn(value, key));
const utc = (value: unknown): value is string => isPublicExpenseRequestTimestamp(value)
    && /(?:\.\d{1,3})?(?:Z|\+00:00)$/u.test(value) && !/\.\d{4,}/u.test(value)
    && value.slice(0, 4) !== '0000' && new Date(value).toISOString().slice(0, 19) === value.slice(0, 19);
const issueCode = (value: unknown): value is NotificationIssueCode => value === 'NO_RESPONSIBLE_ACCOUNTANT' || value === 'INITIATOR_MAPPING_UNAVAILABLE';
function requirePaging(skip: number, take: number) {
    if (!int32(skip) || !int32(take) || take < 1 || take > 100 || skip > 2147483647 - take)
        throw new Error(invalid);
}
function notification(value: unknown): AdministrativeAccrualNotification {
    if (!record(value) || !exact(value, ['id', 'expenseRequestId', 'organizationName', 'recipientCode',
        'recipientName', 'recipientIsActive', 'recipientWasInitiator', 'recipientWasResponsibleAccountant',
        'createdAtUtc', 'fullyPaidAtUtc', 'paymentBusinessDate', 'asOfDate', 'ageDays', 'redFromDays', 'remainingAmountKzt'])
        || !isPublicExpenseRequestText(value.recipientCode, 20) || !isPublicExpenseRequestText(value.recipientName, 150)
        || typeof value.recipientIsActive !== 'boolean' || typeof value.recipientWasInitiator !== 'boolean'
        || typeof value.recipientWasResponsibleAccountant !== 'boolean'
        || (!value.recipientWasInitiator && !value.recipientWasResponsibleAccountant))
        throw new Error(invalid);
    // Reuse the accepted snapshot parser, projecting only its named fields.
    const basic = publicAccrualNotificationPage({ total: 1, skip: 0, take: 1, items: [{
                id: value.id, expenseRequestId: value.expenseRequestId, organizationName: value.organizationName,
                createdAtUtc: value.createdAtUtc, fullyPaidAtUtc: value.fullyPaidAtUtc,
                paymentBusinessDate: value.paymentBusinessDate, asOfDate: value.asOfDate,
                ageDays: value.ageDays, redFromDays: value.redFromDays, remainingAmountKzt: value.remainingAmountKzt,
            }] }, 0, 1).items[0];
    if (!basic)
        throw new Error(invalid);
    return { ...basic, recipientCode: value.recipientCode, recipientName: value.recipientName,
        recipientIsActive: value.recipientIsActive, recipientWasInitiator: value.recipientWasInitiator,
        recipientWasResponsibleAccountant: value.recipientWasResponsibleAccountant };
}
function issue(value: unknown): AccrualNotificationIssue {
    if (!record(value) || !exact(value, ['expenseRequestId', 'organizationName', 'code', 'firstDetectedAtUtc', 'lastDetectedAtUtc'])
        || !isPublicExpenseRequestUuid(value.expenseRequestId) || !isPublicExpenseRequestText(value.organizationName, 500)
        || !issueCode(value.code) || !utc(value.firstDetectedAtUtc) || !utc(value.lastDetectedAtUtc)
        || Date.parse(value.lastDetectedAtUtc) < Date.parse(value.firstDetectedAtUtc))
        throw new Error(invalid);
    return { expenseRequestId: value.expenseRequestId, organizationName: value.organizationName,
        code: value.code, firstDetectedAtUtc: value.firstDetectedAtUtc, lastDetectedAtUtc: value.lastDetectedAtUtc };
}
function page<T>(value: unknown, skip: number, take: number, parse: (row: unknown) => T, key: (row: T) => string, occurredAt: (row: T) => string): NotificationAdministrationPage<T> {
    requirePaging(skip, take);
    if (!record(value) || !exact(value, ['total', 'skip', 'take', 'items']) || !int32(value.total)
        || value.skip !== skip || value.take !== take || !Array.isArray(value.items)
        || value.items.length !== Math.min(take, Math.max(0, value.total - skip)))
        throw new Error(invalid);
    const items = Array.from(value.items, parse);
    const keys = new Set<string>();
    let previous = Number.POSITIVE_INFINITY;
    for (const item of items) {
        const identity = key(item);
        const at = Date.parse(occurredAt(item));
        if (keys.has(identity) || at > previous)
            throw new Error(invalid);
        keys.add(identity);
        previous = at;
    }
    return { total: value.total, skip, take, items };
}
export function publicAdministrativeAccrualNotifications(value: unknown, skip: number, take: number) {
    return page(value, skip, take, notification, (row) => row.id.toLowerCase(), (row) => row.createdAtUtc);
}
export function publicAccrualNotificationIssues(value: unknown, skip: number, take: number) {
    return page(value, skip, take, issue, (row) => `${row.expenseRequestId.toLowerCase()}:${row.code}`, (row) => row.lastDetectedAtUtc);
}
function invalidJson(error: unknown): null {
    if (record(error) && error.name === 'AbortError')
        throw error;
    return null;
}
export async function getAdministrativeAccrualNotifications(skip = 0, take = 25, signal?: AbortSignal) {
    requirePaging(skip, take);
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/administration/accrual-notifications')
        + '?' + new URLSearchParams({ skip: String(skip), take: String(take) }), { ...secureReadInit(), signal });
    if (!response.ok)
        throw new Error('Не удалось загрузить служебные уведомления.');
    return publicAdministrativeAccrualNotifications(await response.json().catch(invalidJson), skip, take);
}
export async function getAccrualNotificationIssues(skip = 0, take = 25, signal?: AbortSignal) {
    requirePaging(skip, take);
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/administration/accrual-notifications/issues')
        + '?' + new URLSearchParams({ skip: String(skip), take: String(take) }), { ...secureReadInit(), signal });
    if (!response.ok)
        throw new Error('Не удалось загрузить ошибки настройки уведомлений.');
    return publicAccrualNotificationIssues(await response.json().catch(invalidJson), skip, take);
}
