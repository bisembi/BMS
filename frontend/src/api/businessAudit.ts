import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import type { BusinessAuditMonitorDetail, BusinessAuditMonitorPage, BusinessAuditSearchFilters } from '../types';
import { isPublicBusinessAuditUuid, publicBusinessAuditDetail, publicBusinessAuditMonitorPage, publicBusinessAuditSearchPage } from './businessAuditPublicResponse';
const invalidMonitorPagingMessage = 'Некорректные параметры страницы бизнес-аудита.';
const invalidBusinessAuditFilterMessage = 'Фильтр бизнес-аудита содержит некорректное значение.';
const maximumInt32 = 2147483647;
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function assertBusinessAuditFilters(value: unknown): asserts value is BusinessAuditSearchFilters {
    if (!isRecord(value)) {
        throw new Error(invalidBusinessAuditFilterMessage);
    }
}
function assertMonitorPaging(skip: number, take: number): void {
    if (!Number.isSafeInteger(skip)
        || skip < 0
        || skip > maximumInt32
        || !Number.isSafeInteger(take)
        || take < 1
        || take > 100) {
        throw new Error(invalidMonitorPagingMessage);
    }
}
export async function getBusinessAuditMonitor(skip = 0, take = 25): Promise<BusinessAuditMonitorPage> {
    assertMonitorPaging(skip, take);
    const parameters = new URLSearchParams({
        skip: String(skip),
        take: String(take),
    });
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/audit-monitor/business-events?${parameters.toString()}`), secureReadInit());
    if (!response.ok) {
        throw new Error(`Ошибка бизнес-аудита: ${response.status}`);
    }
    return publicBusinessAuditMonitorPage(await response.json(), Math.max(skip, 0), Math.min(Math.max(take, 1), 100));
}
function canonicalBusinessAuditText(candidate: unknown, maximumLength: number): candidate is string {
    return typeof candidate === 'string'
        && candidate.length > 0
        && candidate.length <= maximumLength
        && candidate.trim() === candidate
        && !/[\u0000-\u001f\u007f-\u009f]/u.test(candidate);
}
export async function getBusinessAuditMonitorDetail(auditId: string): Promise<BusinessAuditMonitorDetail> {
    if (!isPublicBusinessAuditUuid(auditId)) {
        throw new Error('Некорректный идентификатор события бизнес-аудита.');
    }
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/audit-monitor/business-events/${encodeURIComponent(auditId)}`), secureReadInit());
    if (!response.ok) {
        if (response.status === 404) {
            throw new Error('Событие бизнес-аудита не найдено.');
        }
        throw new Error(`Ошибка детализации бизнес-аудита: ${response.status}`);
    }
    return publicBusinessAuditDetail(await response.json(), auditId);
}
export async function searchBusinessAuditMonitor(filters: BusinessAuditSearchFilters, skip = 0, take = 25): Promise<BusinessAuditMonitorPage> {
    assertBusinessAuditFilters(filters);
    assertMonitorPaging(skip, take);
    const textLimits: Array<[
        keyof BusinessAuditSearchFilters,
        number
    ]> = [
        ['actorExternalId', 256],
        ['objectType', 100],
        ['objectId', 100],
        ['action', 100],
        ['correlationId', 64],
    ];
    const parameters = new URLSearchParams({ skip: String(skip), take: String(take) });
    for (const [key, maximumLength] of textLimits) {
        const value = filters[key];
        if (value === undefined)
            continue;
        if (!canonicalBusinessAuditText(value, maximumLength)) {
            throw new Error(invalidBusinessAuditFilterMessage);
        }
        parameters.set(key, value);
    }
    const canonicalUtc = (value: string | undefined): value is string => value !== undefined
        && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/u.test(value)
        && !Number.isNaN(Date.parse(value));
    if (filters.fromUtc !== undefined) {
        if (!canonicalUtc(filters.fromUtc)) {
            throw new Error('Начало периода бизнес-аудита должно быть указано в UTC.');
        }
        parameters.set('fromUtc', filters.fromUtc);
    }
    if (filters.toUtc !== undefined) {
        if (!canonicalUtc(filters.toUtc)) {
            throw new Error('Конец периода бизнес-аудита должен быть указан в UTC.');
        }
        parameters.set('toUtc', filters.toUtc);
    }
    if (filters.fromUtc !== undefined
        && filters.toUtc !== undefined
        && Date.parse(filters.fromUtc) > Date.parse(filters.toUtc)) {
        throw new Error('Начало периода бизнес-аудита не может быть позже конца.');
    }
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/audit-monitor/business-events/search?${parameters.toString()}`), secureReadInit());
    if (!response.ok) {
        throw new Error(`Ошибка поиска бизнес-аудита: ${response.status}`);
    }
    return publicBusinessAuditSearchPage(await response.json(), Math.max(skip, 0), Math.min(Math.max(take, 1), 100));
}
