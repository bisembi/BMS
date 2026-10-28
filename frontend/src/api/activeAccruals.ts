import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { ExcelExportLimitError } from './excelExportFailure';
import { isPublicExpenseRequestMoney, publicExpenseRequestMoneyCents } from './expenseRequestPublicResponse';
export type ActiveAccrualClosingProgress = 'open' | 'partiallyClosed';
export type ActiveAccrualAgeZone = 'none' | 'yellow' | 'orange' | 'red';
export type ActiveAccrualGrouping = 'none' | 'counterparty' | 'contract';
export type ActiveAccrualAging = {
    asOfDate: string;
    yellowFromDays: number;
    orangeFromDays: number;
    redFromDays: number;
};
export type ActiveAccrualRow = {
    startsGroup?: boolean;
    expenseRequestId: string;
    lineNumber: number | null;
    lineCount: number;
    isRequestSummary: boolean;
    fullyPaidAtUtc: string;
    fullyPaidBusinessDate: string;
    ageDays: number;
    ageZone: ActiveAccrualAgeZone;
    accrualPeriod: string;
    accrualPeriodThrough: string;
    currencyCode: string;
    organizationName: string;
    costCenterName: string;
    cashFlowItemName: string;
    expenseItemName: string;
    counterpartyName: string | null;
    contractName: string | null;
    initiatorName: string;
    purpose: string | null;
    closingProgress: ActiveAccrualClosingProgress;
    originalAmount: number;
    confirmedClosedAmount: number;
    remainingAmount: number;
    originalAmountKzt: number;
    confirmedClosedAmountKzt: number;
    remainingAmountKzt: number;
};
export type ActiveAccrualTotals = {
    originalAmountKzt: number;
    confirmedClosedAmountKzt: number;
    remainingAmountKzt: number;
};
export type ActiveAccrualPage = {
    grouping?: Exclude<ActiveAccrualGrouping, 'none'>;
    items: ActiveAccrualRow[];
    skip: number;
    take: number;
    total: number;
    totals: ActiveAccrualTotals;
    aging: ActiveAccrualAging;
};
export type ActiveAccrualFilters = {
    organizationId?: string;
    costCenterId?: string;
    counterpartyId?: string;
    contractId?: string;
    accrualPeriodFrom?: string;
    accrualPeriodThrough?: string;
    detailLevel?: 'line' | 'request';
    groupBy?: ActiveAccrualGrouping;
};
export const activeAccrualColumnKeys = [
    'accrualPeriod',
    'fullyPaidDate',
    'ageDays',
    'ageZone',
    'requestLine',
    'lineCount',
    'organization',
    'initiator',
    'costCenter',
    'cashFlowItem',
    'expenseItem',
    'counterparty',
    'contract',
    'purpose',
    'closingProgress',
    'originalAmount',
    'confirmedClosedAmount',
    'remainingAmount',
    'originalAmountKzt',
    'confirmedClosedAmountKzt',
    'remainingAmountKzt',
] as const;
export type ActiveAccrualColumnKey = typeof activeAccrualColumnKeys[number];
export type ActiveAccrualExportDownload = {
    blob: Blob;
    fileName: string;
};
const malformedResponseMessage = 'Некорректный ответ API управленческих начислений.';
const unavailableMessage = 'Отчёт активных управленческих начислений сейчас недоступен.';
const exportUnavailableMessage = 'Не удалось сформировать файл управленческих начислений.';
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
const monthPattern = /^\d{4}-\d{2}-01$/u;
const utcTimestampPattern = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d{1,7})?(?:Z|\+00:00)$/u;
const unsafePublicTextPattern = /[\u0000-\u001f\u007f-\u009f]/u;
const excelContentType = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
const exportFileNamePattern = /^active-accruals-\d{8}-\d{6}Z\.xlsx$/u;
const defaultExportFileName = 'active-accruals.xlsx';
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function isAbortError(error: unknown): boolean {
    return ((typeof DOMException !== 'undefined' && error instanceof DOMException)
        || error instanceof Error)
        && error.name === 'AbortError';
}
function hasExactKeys(value: Record<string, unknown>, expected: readonly string[]): boolean {
    const actual = Object.keys(value).sort();
    return actual.length === expected.length
        && actual.every((key, index) => key === expected[index]);
}
function isUuid(value: unknown): value is string {
    return typeof value === 'string'
        && value !== '00000000-0000-0000-0000-000000000000'
        && uuidPattern.test(value);
}
function isNonNegativeInt32(value: unknown): value is number {
    return typeof value === 'number'
        && Number.isSafeInteger(value)
        && value >= 0
        && value <= 2147483647;
}
function isPositiveInt32(value: unknown): value is number {
    return isNonNegativeInt32(value) && value > 0;
}
function isSafeText(value: unknown, maximumLength: number): value is string {
    return typeof value === 'string'
        && value.length > 0
        && value.length <= maximumLength
        && value.trim() === value
        && !unsafePublicTextPattern.test(value);
}
function isNullableSafeText(value: unknown, maximumLength: number): value is string | null {
    return value === null || isSafeText(value, maximumLength);
}
function isAccountingMonth(value: unknown): value is string {
    if (typeof value !== 'string' || !monthPattern.test(value))
        return false;
    const [year, month] = value.split('-').map(Number);
    const parsed = new Date(Date.UTC(year, month - 1, 1));
    return parsed.getUTCFullYear() === year && parsed.getUTCMonth() === month - 1;
}
function isCalendarDate(value: unknown): value is string {
    if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/u.test(value))
        return false;
    const [year, month, day] = value.split('-').map(Number);
    const parsed = new Date(`${value}T00:00:00Z`);
    return year >= 1
        && parsed.getUTCFullYear() === year
        && parsed.getUTCMonth() === month - 1
        && parsed.getUTCDate() === day;
}
function calendarDay(value: string): number {
    return Date.parse(`${value}T00:00:00Z`) / 86400000;
}
function publicAging(value: unknown): ActiveAccrualAging {
    if (!isRecord(value)
        || !hasExactKeys(value, ['asOfDate', 'orangeFromDays', 'redFromDays', 'yellowFromDays'])
        || !isCalendarDate(value.asOfDate)
        || !isPositiveInt32(value.yellowFromDays)
        || !isPositiveInt32(value.orangeFromDays)
        || !isPositiveInt32(value.redFromDays)
        || value.yellowFromDays >= value.orangeFromDays
        || value.orangeFromDays >= value.redFromDays) {
        throw new Error(malformedResponseMessage);
    }
    return {
        asOfDate: value.asOfDate,
        yellowFromDays: value.yellowFromDays,
        orangeFromDays: value.orangeFromDays,
        redFromDays: value.redFromDays,
    };
}
function ageZone(days: number, aging: ActiveAccrualAging): ActiveAccrualAgeZone {
    return days >= aging.redFromDays ? 'red'
        : days >= aging.orangeFromDays ? 'orange'
            : days >= aging.yellowFromDays ? 'yellow' : 'none';
}
function isAgeZone(value: unknown): value is ActiveAccrualAgeZone {
    return value === 'none' || value === 'yellow' || value === 'orange' || value === 'red';
}
function isUtcTimestamp(value: unknown): value is string {
    if (typeof value !== 'string')
        return false;
    const match = utcTimestampPattern.exec(value);
    if (match === null)
        return false;
    const [year, month, day, hour, minute, second] = match.slice(1).map(Number);
    const parsed = new Date(value);
    return !Number.isNaN(parsed.valueOf())
        && parsed.getUTCFullYear() === year
        && parsed.getUTCMonth() === month - 1
        && parsed.getUTCDate() === day
        && parsed.getUTCHours() === hour
        && parsed.getUTCMinutes() === minute
        && parsed.getUTCSeconds() === second;
}
function isCurrencyCode(value: unknown): value is string {
    return typeof value === 'string' && /^[A-Z]{3}$/u.test(value);
}
function isMoney(value: unknown, positive: boolean): value is number {
    return isPublicExpenseRequestMoney(value, !positive);
}
function toMinorUnits(value: number): number {
    const cents = publicExpenseRequestMoneyCents(value);
    if (cents === null)
        throw new Error(malformedResponseMessage);
    return cents;
}
function isClosingProgress(value: unknown): value is ActiveAccrualClosingProgress {
    return value === 'open' || value === 'partiallyClosed';
}
const rowKeys = [
    'accrualPeriod',
    'accrualPeriodThrough',
    'ageDays',
    'ageZone',
    'cashFlowItemName',
    'closingProgress',
    'confirmedClosedAmount',
    'confirmedClosedAmountKzt',
    'contractName',
    'costCenterName',
    'counterpartyName',
    'currencyCode',
    'expenseItemName',
    'expenseRequestId',
    'fullyPaidAtUtc',
    'fullyPaidBusinessDate',
    'initiatorName',
    'isRequestSummary',
    'lineCount',
    'lineNumber',
    'organizationName',
    'originalAmount',
    'originalAmountKzt',
    'purpose',
    'remainingAmount',
    'remainingAmountKzt',
] as const;
function publicRow(value: unknown, aging: ActiveAccrualAging, grouped: boolean): ActiveAccrualRow {
    if (!isRecord(value)
        || !hasExactKeys(value, grouped ? [...rowKeys, 'startsGroup'] : rowKeys)
        || (grouped && typeof value.startsGroup !== 'boolean')
        || !isUuid(value.expenseRequestId)
        || !isPositiveInt32(value.lineCount)
        || typeof value.isRequestSummary !== 'boolean'
        || (value.isRequestSummary ? value.lineNumber !== null : !isPositiveInt32(value.lineNumber))
        || (!value.isRequestSummary && value.lineCount !== 1)
        || !isUtcTimestamp(value.fullyPaidAtUtc)
        || !isCalendarDate(value.fullyPaidBusinessDate)
        || !isNonNegativeInt32(value.ageDays)
        || !isAgeZone(value.ageZone)
        || !isAccountingMonth(value.accrualPeriod)
        || !isAccountingMonth(value.accrualPeriodThrough)
        || value.accrualPeriodThrough < value.accrualPeriod
        || !isCurrencyCode(value.currencyCode)
        || !isSafeText(value.organizationName, 500)
        || !isSafeText(value.costCenterName, 500)
        || !isSafeText(value.cashFlowItemName, 500)
        || !isSafeText(value.expenseItemName, 500)
        || !isNullableSafeText(value.counterpartyName, 500)
        || !isNullableSafeText(value.contractName, 500)
        || !isSafeText(value.initiatorName, 500)
        || !isNullableSafeText(value.purpose, 1000)
        || !isClosingProgress(value.closingProgress)
        || !isMoney(value.originalAmount, true)
        || !isMoney(value.confirmedClosedAmount, false)
        || !isMoney(value.remainingAmount, true)
        || !isMoney(value.originalAmountKzt, false)
        || !isMoney(value.confirmedClosedAmountKzt, false)
        || !isMoney(value.remainingAmountKzt, false)) {
        throw new Error(malformedResponseMessage);
    }
    const original = toMinorUnits(value.originalAmount);
    const closed = toMinorUnits(value.confirmedClosedAmount);
    const remaining = toMinorUnits(value.remainingAmount);
    const originalKzt = toMinorUnits(value.originalAmountKzt);
    const closedKzt = toMinorUnits(value.confirmedClosedAmountKzt);
    const remainingKzt = toMinorUnits(value.remainingAmountKzt);
    const expectedProgress: ActiveAccrualClosingProgress = closed === 0 ? 'open' : 'partiallyClosed';
    const expectedAge = calendarDay(aging.asOfDate) - calendarDay(value.fullyPaidBusinessDate);
    let lineNumber: number | null;
    if (value.isRequestSummary) {
        lineNumber = null;
    }
    else if (isPositiveInt32(value.lineNumber)) {
        lineNumber = value.lineNumber;
    }
    else {
        throw new Error(malformedResponseMessage);
    }
    if (closed + remaining !== original
        || closedKzt + remainingKzt !== originalKzt
        || value.closingProgress !== expectedProgress
        || expectedAge !== value.ageDays
        || value.ageZone !== ageZone(expectedAge, aging)
        || Math.abs(calendarDay(value.fullyPaidBusinessDate)
            - calendarDay(value.fullyPaidAtUtc.slice(0, 10))) > 1) {
        throw new Error(malformedResponseMessage);
    }
    return {
        expenseRequestId: value.expenseRequestId,
        ...(typeof value.startsGroup === 'boolean' ? { startsGroup: value.startsGroup } : {}),
        lineNumber,
        lineCount: value.lineCount,
        isRequestSummary: value.isRequestSummary,
        fullyPaidAtUtc: value.fullyPaidAtUtc,
        fullyPaidBusinessDate: value.fullyPaidBusinessDate,
        ageDays: value.ageDays,
        ageZone: value.ageZone,
        accrualPeriod: value.accrualPeriod,
        accrualPeriodThrough: value.accrualPeriodThrough,
        currencyCode: value.currencyCode,
        organizationName: value.organizationName,
        costCenterName: value.costCenterName,
        cashFlowItemName: value.cashFlowItemName,
        expenseItemName: value.expenseItemName,
        counterpartyName: value.counterpartyName,
        contractName: value.contractName,
        initiatorName: value.initiatorName,
        purpose: value.purpose,
        closingProgress: expectedProgress,
        originalAmount: value.originalAmount,
        confirmedClosedAmount: value.confirmedClosedAmount,
        remainingAmount: value.remainingAmount,
        originalAmountKzt: value.originalAmountKzt,
        confirmedClosedAmountKzt: value.confirmedClosedAmountKzt,
        remainingAmountKzt: value.remainingAmountKzt,
    };
}
const totalsKeys = [
    'confirmedClosedAmountKzt',
    'originalAmountKzt',
    'remainingAmountKzt',
] as const;
function publicTotals(value: unknown): ActiveAccrualTotals {
    if (!isRecord(value)
        || !hasExactKeys(value, totalsKeys)
        || !isMoney(value.originalAmountKzt, false)
        || !isMoney(value.confirmedClosedAmountKzt, false)
        || !isMoney(value.remainingAmountKzt, false)
        || toMinorUnits(value.confirmedClosedAmountKzt)
            + toMinorUnits(value.remainingAmountKzt) !== toMinorUnits(value.originalAmountKzt)) {
        throw new Error(malformedResponseMessage);
    }
    return {
        originalAmountKzt: value.originalAmountKzt,
        confirmedClosedAmountKzt: value.confirmedClosedAmountKzt,
        remainingAmountKzt: value.remainingAmountKzt,
    };
}
export function publicActiveAccrualPage(payload: unknown, grouping: ActiveAccrualGrouping = 'none'): ActiveAccrualPage {
    const grouped = grouping !== 'none';
    if (!['none', 'counterparty', 'contract'].includes(grouping)
        || !isRecord(payload)
        || !hasExactKeys(payload, grouped
            ? ['aging', 'grouping', 'items', 'skip', 'take', 'total', 'totals']
            : ['aging', 'items', 'skip', 'take', 'total', 'totals'])
        || (grouped && payload.grouping !== grouping)
        || !Array.isArray(payload.items)
        || !isNonNegativeInt32(payload.skip)
        || !isPositiveInt32(payload.take)
        || payload.take > 100
        || !isNonNegativeInt32(payload.total)) {
        throw new Error(malformedResponseMessage);
    }
    const aging = publicAging(payload.aging);
    const items = payload.items.map((value) => publicRow(value, aging, grouped));
    if (items.length > payload.take
        || (grouped && items.length > 0 && items[0].startsGroup !== true)
        || items.length > payload.total
        || (items.length > 0 && payload.skip + items.length > payload.total)) {
        throw new Error(malformedResponseMessage);
    }
    const identities = new Set<string>();
    let previousAge = Number.POSITIVE_INFINITY;
    let requestSummaryMode: boolean | undefined;
    let previousGroupName: string | null | undefined;
    for (const item of items) {
        if (requestSummaryMode !== undefined && requestSummaryMode !== item.isRequestSummary) {
            throw new Error(malformedResponseMessage);
        }
        requestSummaryMode = item.isRequestSummary;
        const identity = item.isRequestSummary
            ? item.expenseRequestId.toLowerCase()
            : `${item.expenseRequestId.toLowerCase()}:${item.lineNumber}`;
        if (identities.has(identity))
            throw new Error(malformedResponseMessage);
        identities.add(identity);
        const groupName = grouping === 'counterparty' ? item.counterpartyName : item.contractName;
        if (item.startsGroup !== true
            && (item.ageDays > previousAge || (grouped && groupName !== previousGroupName))) {
            throw new Error(malformedResponseMessage);
        }
        previousAge = item.ageDays;
        previousGroupName = groupName;
    }
    const totals = publicTotals(payload.totals);
    if (items.length === payload.total) {
        const sum = (selector: (row: ActiveAccrualRow) => number) => items.reduce((total, row) => total + toMinorUnits(selector(row)), 0);
        if (sum((row) => row.originalAmountKzt) !== toMinorUnits(totals.originalAmountKzt)
            || sum((row) => row.confirmedClosedAmountKzt)
                !== toMinorUnits(totals.confirmedClosedAmountKzt)
            || sum((row) => row.remainingAmountKzt) !== toMinorUnits(totals.remainingAmountKzt)) {
            throw new Error(malformedResponseMessage);
        }
    }
    return {
        items,
        ...(grouping === 'none' ? {} : { grouping }),
        skip: payload.skip,
        take: payload.take,
        total: payload.total,
        totals,
        aging,
    };
}
function validateFilters(filters: ActiveAccrualFilters): void {
    if (!isRecord(filters)
        || (filters.organizationId !== undefined && !isUuid(filters.organizationId))
        || (filters.costCenterId !== undefined && !isUuid(filters.costCenterId))
        || (filters.counterpartyId !== undefined && !isUuid(filters.counterpartyId))
        || (filters.contractId !== undefined && !isUuid(filters.contractId))
        || (filters.detailLevel !== undefined
            && filters.detailLevel !== 'line'
            && filters.detailLevel !== 'request')
        || (filters.groupBy !== undefined
            && !['none', 'counterparty', 'contract'].includes(filters.groupBy))
        || (filters.accrualPeriodFrom !== undefined
            && !isAccountingMonth(filters.accrualPeriodFrom))
        || (filters.accrualPeriodThrough !== undefined
            && !isAccountingMonth(filters.accrualPeriodThrough))
        || (filters.accrualPeriodFrom !== undefined
            && filters.accrualPeriodThrough !== undefined
            && filters.accrualPeriodFrom > filters.accrualPeriodThrough)) {
        throw new Error(malformedResponseMessage);
    }
}
function queryFor(filters: ActiveAccrualFilters, columns?: readonly ActiveAccrualColumnKey[]): URLSearchParams {
    const query = new URLSearchParams();
    if (filters.organizationId !== undefined)
        query.set('organizationId', filters.organizationId);
    if (filters.costCenterId !== undefined)
        query.set('costCenterId', filters.costCenterId);
    if (filters.counterpartyId !== undefined)
        query.set('counterpartyId', filters.counterpartyId);
    if (filters.contractId !== undefined)
        query.set('contractId', filters.contractId);
    if (filters.accrualPeriodFrom !== undefined) {
        query.set('accrualPeriodFrom', filters.accrualPeriodFrom);
    }
    if (filters.accrualPeriodThrough !== undefined) {
        query.set('accrualPeriodThrough', filters.accrualPeriodThrough);
    }
    if (filters.detailLevel === 'request')
        query.set('detail', 'request');
    if (filters.groupBy !== undefined && filters.groupBy !== 'none') {
        query.set('groupBy', filters.groupBy);
    }
    if (columns !== undefined) {
        if (columns.length === 0 || new Set(columns).size !== columns.length
            || columns.some((column) => !activeAccrualColumnKeys.includes(column))) {
            throw new Error(malformedResponseMessage);
        }
        query.set('columns', columns.join(','));
    }
    return query;
}
export async function getActiveAccruals(filters: ActiveAccrualFilters, skip: number, take: number, signal?: AbortSignal): Promise<ActiveAccrualPage> {
    validateFilters(filters);
    if (!isNonNegativeInt32(skip) || !isPositiveInt32(take) || take > 100) {
        throw new Error(malformedResponseMessage);
    }
    const query = queryFor(filters);
    query.set('skip', String(skip));
    query.set('take', String(take));
    let response: Response;
    try {
        response = await fetchWithSafeTransport(apiUrl(`/api/v1/management/accruals/active?${query.toString()}`), { ...secureReadInit(), signal });
    }
    catch (error) {
        if (isAbortError(error))
            throw error;
        throw new Error(unavailableMessage);
    }
    if (!response.ok) {
        throw new Error(unavailableMessage);
    }
    try {
        const page = publicActiveAccrualPage(await response.json(), filters.groupBy);
        if (page.skip !== skip || page.take !== take)
            throw new Error(malformedResponseMessage);
        return page;
    }
    catch (error) {
        if (isAbortError(error))
            throw error;
        throw new Error(unavailableMessage);
    }
}
function exportFileName(contentDisposition: string | null): string {
    if (contentDisposition === null)
        return defaultExportFileName;
    const match = /filename="?([^";]+)"?/iu.exec(contentDisposition);
    const candidate = match?.[1]?.trim();
    return candidate !== undefined && exportFileNamePattern.test(candidate)
        ? candidate
        : defaultExportFileName;
}
export async function getActiveAccrualExport(filters: ActiveAccrualFilters, signal?: AbortSignal, columns?: readonly ActiveAccrualColumnKey[]): Promise<ActiveAccrualExportDownload> {
    validateFilters(filters);
    const query = queryFor(filters, columns);
    const suffix = query.size === 0 ? '' : `?${query.toString()}`;
    let response: Response;
    try {
        response = await fetchWithSafeTransport(apiUrl(`/api/v1/management/accruals/active/export.xlsx${suffix}`), { ...secureReadInit(), signal });
    }
    catch (error) {
        if (isAbortError(error))
            throw error;
        throw new Error(exportUnavailableMessage);
    }
    if (response.status === 422)
        throw new ExcelExportLimitError();
    if (!response.ok
        || !(response.headers.get('content-type') ?? '').toLowerCase().startsWith(excelContentType)) {
        throw new Error(exportUnavailableMessage);
    }
    let blob: Blob;
    try {
        blob = await response.blob();
    }
    catch (error) {
        if (isAbortError(error))
            throw error;
        throw new Error(exportUnavailableMessage);
    }
    if (blob.size === 0)
        throw new Error(exportUnavailableMessage);
    return {
        blob,
        fileName: exportFileName(response.headers.get('content-disposition')),
    };
}
