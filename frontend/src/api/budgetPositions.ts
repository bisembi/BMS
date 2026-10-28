/// Budget positions reserved by one expense request.
import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { ExcelExportLimitError } from './excelExportFailure';
import { assertExpenseRequestIdentifier } from './expenseRequestIdentifier';
import { isExpenseRequestStatus } from '../expenseRequestStatus';
import type { BudgetPosition, BudgetPositionPage, BudgetPositionTotals, BudgetReservationPage, BudgetReservationParticipant, ExecutionFactSource } from '../types';
const invalidBudgetResponseMessage = 'Некорректный ответ API бюджета.';
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
const maximumMoney = 90071992547409.9;
const maximumInt32 = 2147483647;
const executionBudgetErrorMessage = 'Не удалось загрузить исполнение бюджета.';
function publicFactSource(value: unknown): ExecutionFactSource {
    if (!isRecord(value)
        || Object.keys(value).sort().join('|') !== 'capturedAtUtc|cutoffExclusiveLocal|grossHash|snapshotId|sourceGeneration|sourceYear'
        || !isUuid(value.snapshotId) || !Number.isInteger(value.sourceYear) || (value.sourceYear as number) < 2026 || (value.sourceYear as number) > 2100
        || typeof value.sourceGeneration !== 'string' || !/^[A-F0-9]{64}$/u.test(value.sourceGeneration)
        || typeof value.grossHash !== 'string' || !/^[A-F0-9]{64}$/u.test(value.grossHash)
        || !isCalendarDate(value.cutoffExclusiveLocal) || value.cutoffExclusiveLocal < '2026-01-02' || value.cutoffExclusiveLocal > '2101-01-01'
        || typeof value.capturedAtUtc !== 'string' || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/u.test(value.capturedAtUtc)
        || !Number.isFinite(Date.parse(value.capturedAtUtc)))
        throw new Error(invalidBudgetResponseMessage);
    const expectedCutoff = new Date(Date.parse(value.capturedAtUtc) + (5 + 24) * 3600000).toISOString().slice(0, 10);
    if (value.cutoffExclusiveLocal !== expectedCutoff)
        throw new Error(invalidBudgetResponseMessage);
    return value as ExecutionFactSource;
}
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function isUuid(value: unknown): value is string {
    return typeof value === 'string'
        && value !== '00000000-0000-0000-0000-000000000000'
        && uuidPattern.test(value);
}
function isBudgetMonth(value: unknown): value is string {
    if (typeof value !== 'string' || !/^\d{4}-\d{2}-01$/u.test(value))
        return false;
    const parsed = new Date(`${value}T00:00:00Z`);
    return !Number.isNaN(parsed.valueOf()) && parsed.toISOString().slice(0, 10) === value;
}
function isSafeInteger(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value);
}
function isNonNegativeInt32(value: unknown): value is number {
    return isSafeInteger(value) && value >= 0 && value <= maximumInt32;
}
// Validate the canonical decimal representation of the wire Number. Binary
// multiplication by 100 can introduce a fraction (even for valid cents) or
// change a cent near the safe-integer boundary. Never round invalid input.
function exactMinorUnits(value: unknown): number | null {
    if (typeof value !== 'number' || !Number.isFinite(value)
        || Math.abs(value) > maximumMoney) {
        return null;
    }
    const decimal = Math.abs(value).toString();
    if (!/^\d{1,16}(?:\.\d{1,2})?$/u.test(decimal))
        return null;
    const [whole, fraction = ''] = decimal.split('.');
    const cents = BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
    if (cents > BigInt(Number.MAX_SAFE_INTEGER))
        return null;
    return value < 0 ? -Number(cents) : Number(cents);
}
function isMoney(value: unknown, allowNegative: boolean): value is number {
    return exactMinorUnits(value) !== null && (allowNegative || (value as number) >= 0);
}
function minorUnits(value: number): number {
    const cents = exactMinorUnits(value);
    if (cents === null)
        throw new Error(invalidBudgetResponseMessage);
    return cents;
}
/// The totals describe the whole filtered area, so they are checked against
/// that area and not against the page: the count they report must be the count
/// the page reports, and it must be exactly the two limit counters added up.
///
/// Every published position has an effective numeric limit. The server maps a
/// missing approved version or omitted line to zero, so the counters remain
/// additive and the available total is always limit minus reserved minus paid.
function publicBudgetPositionTotals(value: unknown, total: number): BudgetPositionTotals {
    if (!isRecord(value))
        throw new Error(invalidBudgetResponseMessage);
    const names = Object.keys(value).sort().join('|');
    if (names !== 'availableAmountKzt|configuredLimitCount|limitAmountKzt|paidAmountKzt|reservedAmountKzt|totalPositions|unconfiguredLimitCount'
        || !isNonNegativeInt32(value.totalPositions)
        || !isNonNegativeInt32(value.configuredLimitCount)
        || !isNonNegativeInt32(value.unconfiguredLimitCount)
        || value.configuredLimitCount + value.unconfiguredLimitCount !== value.totalPositions
        || value.totalPositions !== total
        || value.unconfiguredLimitCount !== 0
        || value.configuredLimitCount !== value.totalPositions
        || !isMoney(value.limitAmountKzt, false)
        || !isMoney(value.availableAmountKzt, true)
        || !isMoney(value.reservedAmountKzt, false)
        || !isMoney(value.paidAmountKzt, false)) {
        throw new Error(invalidBudgetResponseMessage);
    }
    const expected = minorUnits(value.limitAmountKzt)
        - minorUnits(value.reservedAmountKzt)
        - minorUnits(value.paidAmountKzt);
    if (!Number.isSafeInteger(expected)
        || minorUnits(value.availableAmountKzt) !== expected) {
        throw new Error(invalidBudgetResponseMessage);
    }
    return {
        totalPositions: value.totalPositions,
        configuredLimitCount: value.configuredLimitCount,
        unconfiguredLimitCount: value.unconfiguredLimitCount,
        limitAmountKzt: value.limitAmountKzt,
        reservedAmountKzt: value.reservedAmountKzt,
        paidAmountKzt: value.paidAmountKzt,
        availableAmountKzt: value.availableAmountKzt,
    };
}
/// One budget position exactly as the server publishes it. The execution list
/// and the reservation drill-down both parse a position through here, so the
/// two screens cannot come to accept different shapes of the same thing.
function publicBudgetPosition(value: unknown, allowPlanCostCenters = false, allowUnassignedCenter = false): BudgetPosition {
    if (!isRecord(value))
        throw new Error(invalidBudgetResponseMessage);
    const itemNames = Object.keys(value).filter(name => name !== 'planCostCenters' && name !== 'requestControl343').sort();
    let requestControl343: BudgetPosition['requestControl343'];
    if ('requestControl343' in value) {
        const c = value.requestControl343;
        if (!isRecord(c) || Object.keys(c).sort().join('|') !== 'availableAfterKzt|availableBeforeKzt|frozen|positiveDeficitKzt'
            || (c.availableBeforeKzt !== null && !isMoney(c.availableBeforeKzt, true))
            || (c.availableAfterKzt !== null && !isMoney(c.availableAfterKzt, true))
            || (c.positiveDeficitKzt !== null && !isMoney(c.positiveDeficitKzt, false)) || typeof c.frozen !== 'boolean')
            throw new Error(invalidBudgetResponseMessage);
        requestControl343 = c as BudgetPosition['requestControl343'];
    }
    let planCostCenters: BudgetPosition['planCostCenters'];
    if ('planCostCenters' in value) {
        if (!allowPlanCostCenters || !Array.isArray(value.planCostCenters) || value.planCostCenters.length > 100000)
            throw new Error(invalidBudgetResponseMessage);
        const ids = new Set<string>();
        planCostCenters = value.planCostCenters.map(source => {
            if (!isRecord(source) || Object.keys(source).sort().join('|') !== 'code|costCenterId|name|reservedAmountKzt'
                || !isUuid(source.costCenterId) || ids.has(source.costCenterId.toLowerCase())
                || !(source.code === null || (typeof source.code === 'string' && source.code.length <= 1000))
                || !(source.name === null || (typeof source.name === 'string' && source.name.length <= 1000))
                || !isMoney(source.reservedAmountKzt, false))
                throw new Error(invalidBudgetResponseMessage);
            ids.add(source.costCenterId.toLowerCase());
            return { costCenterId: source.costCenterId, code: source.code, name: source.name, reservedAmountKzt: source.reservedAmountKzt };
        });
        const reserve = planCostCenters.reduce((sum, source) => sum + minorUnits(source.reservedAmountKzt), 0);
        if (!isMoney(value.reservedAmountKzt, false) || !Number.isSafeInteger(reserve) || reserve !== minorUnits(value.reservedAmountKzt))
            throw new Error(invalidBudgetResponseMessage);
    }
    if (itemNames.join('|') !== 'availableAmountKzt|budgetMonth|cashFlowItemId|costCenterId|isLimitConfigured|isOverLimit|limitAmountKzt|paidAmountKzt|reservedAmountKzt'
        || !(isUuid(value.costCenterId) || (allowUnassignedCenter && value.costCenterId === '00000000-0000-0000-0000-000000000000'))
        || !isUuid(value.cashFlowItemId)
        || !isBudgetMonth(value.budgetMonth)
        || !isMoney(value.limitAmountKzt, false)
        || !isMoney(value.reservedAmountKzt, false)
        || !isMoney(value.paidAmountKzt, false)
        || !isMoney(value.availableAmountKzt, true)
        || typeof value.isLimitConfigured !== 'boolean'
        || typeof value.isOverLimit !== 'boolean'
        || !value.isLimitConfigured
        || value.isOverLimit !== (value.availableAmountKzt < 0)) {
        throw new Error(invalidBudgetResponseMessage);
    }
    // Keep the W526A arithmetic boundary shared by the execution list and its
    // drill-down. The effective zero is an ordinary numeric limit.
    const expectedAvailable = minorUnits(value.limitAmountKzt)
        - minorUnits(value.reservedAmountKzt)
        - minorUnits(value.paidAmountKzt);
    if (!Number.isSafeInteger(expectedAvailable)
        || minorUnits(value.availableAmountKzt) !== expectedAvailable) {
        throw new Error(invalidBudgetResponseMessage);
    }
    if (value.costCenterId === '00000000-0000-0000-0000-000000000000'
        && (value.reservedAmountKzt !== 0 && !planCostCenters?.length)) {
        throw new Error(invalidBudgetResponseMessage);
    }
    return {
        costCenterId: value.costCenterId,
        ...(planCostCenters === undefined ? {} : { planCostCenters }),
        ...(requestControl343 === undefined ? {} : { requestControl343 }),
        cashFlowItemId: value.cashFlowItemId,
        budgetMonth: value.budgetMonth,
        limitAmountKzt: value.limitAmountKzt,
        reservedAmountKzt: value.reservedAmountKzt,
        paidAmountKzt: value.paidAmountKzt,
        availableAmountKzt: value.availableAmountKzt,
        isLimitConfigured: value.isLimitConfigured,
        isOverLimit: value.isOverLimit,
    };
}
function matchesExpectedRequestId(value: unknown, expectedRequestId: string | undefined): value is string | null {
    if (expectedRequestId === undefined)
        return value === null || isUuid(value);
    return isUuid(expectedRequestId)
        && isUuid(value)
        && value.toLowerCase() === expectedRequestId.toLowerCase();
}
function publicBudgetPositionPage(payload: unknown, expectedRequestId?: string, completeRequestCapture = false, expectedRequestVersion?: string): BudgetPositionPage {
    if (!isRecord(payload))
        throw new Error(invalidBudgetResponseMessage);
    if ('factSource' in payload && (expectedRequestId !== undefined || payload.expenseRequestId !== null))
        throw new Error(invalidBudgetResponseMessage);
    const factSource = 'factSource' in payload ? publicFactSource(payload.factSource) : undefined;
    const pageNames = Object.keys(payload).filter(name => name !== 'factSource').sort();
    const stampedNames = 'expenseRequestId|expenseRequestVersion|items|skip|take|total|totals';
    if ((pageNames.join('|') !== 'expenseRequestId|items|skip|take|total|totals'
        && pageNames.join('|') !== stampedNames)
        || ('expenseRequestVersion' in payload && !isUuid(payload.expenseRequestVersion))
        || !Array.isArray(payload.items)
        || !isNonNegativeInt32(payload.skip)
        || !isSafeInteger(payload.take) || payload.take < 1 || payload.take > (completeRequestCapture ? 100000 : 100)
        || !isNonNegativeInt32(payload.total)
        || payload.total < payload.items.length
        || payload.items.length > payload.take
        || (payload.items.length > 0
            && (!Number.isSafeInteger(payload.skip + payload.items.length)
                || payload.total < payload.skip + payload.items.length))
        || !matchesExpectedRequestId(payload.expenseRequestId, expectedRequestId)) {
        throw new Error(invalidBudgetResponseMessage);
    }
    if (completeRequestCapture && (expectedRequestId === undefined || payload.skip !== 0
        || payload.items.length !== payload.total || payload.total > 100000))
        throw new Error(invalidBudgetResponseMessage);
    if (expectedRequestVersion !== undefined && (!isUuid(payload.expenseRequestVersion)
        || payload.expenseRequestVersion.toLowerCase() !== expectedRequestVersion.toLowerCase()))
        throw new Error(invalidBudgetResponseMessage);
    const items = payload.items.map(item => publicBudgetPosition(item, expectedRequestId === undefined && payload.expenseRequestId === null, factSource !== undefined));
    if (completeRequestCapture) {
        const keys = new Set(items.map(item => `${item.costCenterId.toLowerCase()}:${item.cashFlowItemId.toLowerCase()}:${item.budgetMonth}`));
        if (keys.size !== items.length)
            throw new Error(invalidBudgetResponseMessage);
    }
    const totals = publicBudgetPositionTotals(payload.totals, payload.total);
    if (items.length === totals.totalPositions) {
        // The page holds the whole area, so the page and the totals must be the
        // same sums. A totals block computed over the page alone survives every
        // other check and dies here.
        const reserved = items.reduce((sum, item) => sum + minorUnits(item.reservedAmountKzt), 0);
        const paid = items.reduce((sum, item) => sum + minorUnits(item.paidAmountKzt), 0);
        const limit = items.reduce((sum, item) => sum + minorUnits(item.limitAmountKzt), 0);
        if (reserved !== minorUnits(totals.reservedAmountKzt)
            || paid !== minorUnits(totals.paidAmountKzt)
            || items.filter((item) => item.isLimitConfigured).length !== totals.configuredLimitCount
            || limit !== minorUnits(totals.limitAmountKzt)) {
            throw new Error(invalidBudgetResponseMessage);
        }
    }
    return {
        items,
        skip: payload.skip,
        take: payload.take,
        total: payload.total,
        expenseRequestId: payload.expenseRequestId,
        ...(factSource === undefined ? {} : { factSource }),
        ...('expenseRequestVersion' in payload ? { expenseRequestVersion: payload.expenseRequestVersion as string } : {}),
        totals,
    };
}
export type BudgetExecutionFilters = {
    structure?: 'current' | 'approved';
    expectedFactSnapshotId?: string;
    costCenterId?: string;
    cashFlowItemId?: string;
    /** Wire format is the first day of the requested month: yyyy-MM-01. */
    budgetMonth?: string;
};
function assertExecutionPage(page: BudgetPositionPage, expectedSkip: number, expectedTake: number): BudgetPositionPage {
    if (page.expenseRequestId !== null
        || page.skip !== expectedSkip
        || page.take !== expectedTake) {
        throw new Error(invalidBudgetResponseMessage);
    }
    const keys = new Set<string>();
    for (const item of page.items) {
        const key = `${item.costCenterId.toLowerCase()}:${item.cashFlowItemId.toLowerCase()}:${item.budgetMonth}`;
        if (keys.has(key)) {
            throw new Error(invalidBudgetResponseMessage);
        }
        keys.add(key);
    }
    return page;
}
function validateExecutionFilters(filters: BudgetExecutionFilters): void {
    if (!isRecord(filters)
        || (filters.structure !== undefined && filters.structure !== 'current' && filters.structure !== 'approved')
        || (filters.costCenterId !== undefined && !isUuid(filters.costCenterId))
        || (filters.cashFlowItemId !== undefined && !isUuid(filters.cashFlowItemId))
        || (filters.budgetMonth !== undefined && !isBudgetMonth(filters.budgetMonth))
        || (filters.expectedFactSnapshotId !== undefined && !isUuid(filters.expectedFactSnapshotId))) {
        throw new Error(invalidBudgetResponseMessage);
    }
}
function validateExecutionQuery(filters: BudgetExecutionFilters, skip: number, take: number): void {
    validateExecutionFilters(filters);
    if (!isNonNegativeInt32(skip)
        || !isSafeInteger(take) || take < 1 || take > 100) {
        throw new Error(invalidBudgetResponseMessage);
    }
}
/// Every filter value is placed through URLSearchParams, so it is encoded once
/// and cannot escape into the path or add a parameter of its own.
function executionQuery(filters: BudgetExecutionFilters): URLSearchParams {
    const query = new URLSearchParams();
    if (filters.structure !== undefined)
        query.set('structure', filters.structure);
    if (filters.costCenterId !== undefined)
        query.set('costCenterId', filters.costCenterId);
    if (filters.cashFlowItemId !== undefined)
        query.set('cashFlowItemId', filters.cashFlowItemId);
    if (filters.budgetMonth !== undefined)
        query.set('budgetMonth', filters.budgetMonth);
    if (filters.expectedFactSnapshotId !== undefined)
        query.set('expectedFactSnapshotId', filters.expectedFactSnapshotId);
    return query;
}
/// Totals of an area that is already in hand, by the same rule the server
/// follows: a key without an approved budget is an effective zero, never an
/// undefined limit.
export function budgetTotalsForItems(items: BudgetPositionPage['items']): BudgetPositionTotals {
    const reserved = items.reduce((sum, item) => sum + minorUnits(item.reservedAmountKzt), 0);
    const paid = items.reduce((sum, item) => sum + minorUnits(item.paidAmountKzt), 0);
    const limit = items.reduce((sum, item) => sum + minorUnits(item.limitAmountKzt), 0);
    return {
        totalPositions: items.length,
        configuredLimitCount: items.length,
        unconfiguredLimitCount: 0,
        limitAmountKzt: limit / 100,
        reservedAmountKzt: reserved / 100,
        paidAmountKzt: paid / 100,
        availableAmountKzt: (limit - reserved - paid) / 100,
    };
}
export async function getBudgetPositionsForExecution(filters: BudgetExecutionFilters = {}, skip = 0, take = 25): Promise<BudgetPositionPage> {
    validateExecutionQuery(filters, skip, take);
    const query = executionQuery(filters);
    query.set('skip', String(skip));
    query.set('take', String(take));
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/budget/positions?${query.toString()}`), secureReadInit());
    if (!response.ok) {
        throw new Error(executionBudgetErrorMessage);
    }
    try {
        const page = assertExecutionPage(publicBudgetPositionPage(await response.json()), skip, take);
        if (filters.expectedFactSnapshotId !== undefined
            && page.factSource?.snapshotId.toLowerCase() !== filters.expectedFactSnapshotId.toLowerCase())
            throw new Error(invalidBudgetResponseMessage);
        return page;
    }
    catch {
        throw new Error(invalidBudgetResponseMessage);
    }
}
/// W521/W522: the whole filtered area as a workbook.
///
/// The name the browser is told to save under is the one value this facade
/// takes from a server header. The exporter builds it as
/// budget-execution-<yyyyMMdd-HHmmss>Z.xlsx, so exactly that shape is accepted
/// and anything else falls back to a fixed name.
const budgetExportFileNamePattern = /^budget-execution-\d{8}-\d{6}Z\.xlsx$/u;
const defaultBudgetExportFileName = 'budget-execution.xlsx';
const excelContentType = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
const budgetExportErrorMessage = 'Не удалось сформировать файл выгрузки.';
function budgetExportFileName(contentDisposition: string | null): string {
    if (contentDisposition === null)
        return defaultBudgetExportFileName;
    const match = /filename="?([^";]+)"?/iu.exec(contentDisposition);
    const candidate = match?.[1]?.trim();
    return candidate !== undefined && budgetExportFileNamePattern.test(candidate)
        ? candidate
        : defaultBudgetExportFileName;
}
export async function getBudgetExecutionExport(filters: BudgetExecutionFilters = {}): Promise<{
    blob: Blob;
    fileName: string;
}> {
    validateExecutionFilters(filters);
    const query = executionQuery(filters);
    const suffix = query.size === 0 ? '' : `?${query.toString()}`;
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/budget/positions/export.xlsx${suffix}`), secureReadInit());
    if (response.status === 422)
        throw new ExcelExportLimitError();
    // A refusal is never read: the body of a failed export is a server problem
    // document, and none of it belongs on screen. The caller gets one fixed
    // sentence whatever went wrong.
    if (!response.ok) {
        throw new Error(budgetExportErrorMessage);
    }
    const contentType = (response.headers.get('content-type') ?? '').toLowerCase();
    if (!contentType.startsWith(excelContentType)) {
        throw new Error(budgetExportErrorMessage);
    }
    const blob = await response.blob();
    if (blob.size === 0) {
        throw new Error(budgetExportErrorMessage);
    }
    return { blob, fileName: budgetExportFileName(response.headers.get('content-disposition')) };
}
/// W531/W532: the composition of one budget position's current reserve.
const invalidReservationMessage = 'Некорректный ответ API состава резерва.';
const maximumPurposeLength = 1000;
const unsafeTextPattern = /[\u0000-\u001f\u007f-\u009f]/u;
const isoDatePattern = /^\d{4}-\d{2}-\d{2}$/u;
export type BudgetPositionKey = {
    expectedFactSnapshotId?: string;
    costCenterId: string;
    cashFlowItemId: string;
    /** Wire format is the first day of the month: yyyy-MM-01. */
    budgetMonth: string;
};
function isCalendarDate(value: unknown): value is string {
    if (typeof value !== 'string' || !isoDatePattern.test(value))
        return false;
    // A shape match is not a date: 2026-02-30 passes the pattern and is not a
    // day that exists.
    const [year, month, day] = value.split('-').map(Number);
    const parsed = new Date(Date.UTC(year, month - 1, day));
    return parsed.getUTCFullYear() === year
        && parsed.getUTCMonth() === month - 1
        && parsed.getUTCDate() === day;
}
function isSafePurpose(value: unknown): value is string | null {
    if (value === null)
        return true;
    return typeof value === 'string'
        && value.length > 0
        && value.length <= maximumPurposeLength
        && !unsafeTextPattern.test(value);
}
function isPositiveInt32(value: unknown): value is number {
    return isNonNegativeInt32(value) && value >= 1;
}
function sameKey(left: string, right: string): boolean {
    return left.toLowerCase() === right.toLowerCase();
}
/// One reserving request. The field set is exact: an unknown field means the
/// server published something this screen was never reviewed to show.
function publicReservationParticipant(value: unknown): BudgetReservationParticipant {
    if (!isRecord(value))
        throw new Error(invalidReservationMessage);
    const names = Object.keys(value).sort().join('|');
    if (names !== 'lineCount|plannedPaymentDate|purpose|reservedAmountKzt|status'
        || !isExpenseRequestStatus(value.status)
        || !isSafePurpose(value.purpose)
        || !isCalendarDate(value.plannedPaymentDate)
        || !isPositiveInt32(value.lineCount)
        || !isMoney(value.reservedAmountKzt, false)
        || value.reservedAmountKzt <= 0) {
        throw new Error(invalidReservationMessage);
    }
    return {
        status: value.status,
        purpose: value.purpose,
        plannedPaymentDate: value.plannedPaymentDate,
        lineCount: value.lineCount,
        reservedAmountKzt: value.reservedAmountKzt,
    };
}
/// The composition of one reserve, checked against the key that was asked for
/// and against itself.
///
/// The strongest check is the last one: when the page holds the whole reserve,
/// its rows must sum to the totals beside them. A page sum mistaken for the
/// grand total, a doubled row, or a row quietly dropped all fail there.
function publicBudgetReservationPage(payload: unknown, key: BudgetPositionKey, expectedSkip: number, expectedTake: number): BudgetReservationPage {
    if (!isRecord(payload))
        throw new Error(invalidReservationMessage);
    const factSource = 'factSource' in payload ? publicFactSource(payload.factSource) : undefined;
    if (key.expectedFactSnapshotId !== undefined && factSource?.snapshotId.toLowerCase() !== key.expectedFactSnapshotId.toLowerCase())
        throw new Error(invalidReservationMessage);
    const names = Object.keys(payload).filter(name => name !== 'factSource').sort().join('|');
    const visibleReserved = Object.hasOwn(payload, 'visibleReservedAmountKzt')
        ? payload.visibleReservedAmountKzt : payload.totalReservedAmountKzt;
    if ((names !== 'items|position|skip|take|total|totalLineCount|totalReservedAmountKzt'
        && names !== 'items|position|skip|take|total|totalLineCount|totalReservedAmountKzt|visibleReservedAmountKzt')
        || !isMoney(visibleReserved, false)
        || !Array.isArray(payload.items)
        || payload.skip !== expectedSkip
        || payload.take !== expectedTake
        || !isNonNegativeInt32(payload.total)
        || !isNonNegativeInt32(payload.totalLineCount)
        || !isMoney(payload.totalReservedAmountKzt, false)
        || minorUnits(visibleReserved as number) > minorUnits(payload.totalReservedAmountKzt)
        || payload.total < payload.items.length
        || payload.items.length > payload.take
        || payload.totalLineCount < payload.total
        || (payload.items.length > 0
            && (!Number.isSafeInteger(payload.skip + payload.items.length)
                || payload.total < payload.skip + payload.items.length))) {
        throw new Error(invalidReservationMessage);
    }
    const position = publicBudgetPosition(payload.position);
    if (!sameKey(position.costCenterId, key.costCenterId)
        || !sameKey(position.cashFlowItemId, key.cashFlowItemId)
        || position.budgetMonth !== key.budgetMonth
        || minorUnits(position.reservedAmountKzt) !== minorUnits(payload.totalReservedAmountKzt)) {
        // Either the answer is about another position, or the composition does not
        // add up to the reserve of the position it describes.
        throw new Error(invalidReservationMessage);
    }
    const items = payload.items.map(publicReservationParticipant);
    const pageReserved = items.reduce((sum, item) => sum + minorUnits(item.reservedAmountKzt), 0);
    const pageLines = items.reduce((sum, item) => sum + item.lineCount, 0);
    if (!Number.isSafeInteger(pageReserved)
        || pageReserved > minorUnits(visibleReserved as number)
        || pageLines > payload.totalLineCount) {
        throw new Error(invalidReservationMessage);
    }
    if (items.length === payload.total
        && (pageReserved !== minorUnits(visibleReserved as number)
            || pageLines !== payload.totalLineCount)) {
        throw new Error(invalidReservationMessage);
    }
    return {
        items,
        skip: payload.skip,
        take: payload.take,
        total: payload.total,
        totalLineCount: payload.totalLineCount,
        totalReservedAmountKzt: payload.totalReservedAmountKzt,
        visibleReservedAmountKzt: visibleReserved as number,
        position,
        ...(factSource === undefined ? {} : { factSource }),
    };
}
function validateReservationQuery(key: BudgetPositionKey, skip: number, take: number): void {
    if (!isRecord(key)
        || !isUuid(key.costCenterId)
        || !isUuid(key.cashFlowItemId)
        || !isBudgetMonth(key.budgetMonth)
        || (key.expectedFactSnapshotId !== undefined && !isUuid(key.expectedFactSnapshotId))
        || !isNonNegativeInt32(skip)
        || !isSafeInteger(take) || take < 1 || take > 100) {
        throw new Error(invalidReservationMessage);
    }
}
/// Returns null when the position itself does not exist.
export async function getBudgetPositionReservations(key: BudgetPositionKey, skip = 0, take = 25): Promise<BudgetReservationPage | null> {
    validateReservationQuery(key, skip, take);
    const query = new URLSearchParams();
    query.set('costCenterId', key.costCenterId);
    query.set('cashFlowItemId', key.cashFlowItemId);
    query.set('budgetMonth', key.budgetMonth);
    if (key.expectedFactSnapshotId !== undefined)
        query.set('expectedFactSnapshotId', key.expectedFactSnapshotId);
    query.set('skip', String(skip));
    query.set('take', String(take));
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/budget/positions/reservations?${query.toString()}`), secureReadInit());
    if (response.status === 404) {
        return null;
    }
    // A refusal is never read: its body is a server problem document, and none
    // of it belongs on screen.
    if (!response.ok) {
        throw new Error(invalidReservationMessage);
    }
    try {
        return publicBudgetReservationPage(await response.json(), key, skip, take);
    }
    catch {
        throw new Error(invalidReservationMessage);
    }
}
export async function getBudgetPositionsForRequest(id: string, expectedRequestVersion?: string): Promise<BudgetPositionPage> {
    assertExpenseRequestIdentifier(id);
    if (expectedRequestVersion !== undefined && !isUuid(expectedRequestVersion))
        throw new Error(invalidBudgetResponseMessage);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/budget/positions?expenseRequestId=${encodeURIComponent(id)}&complete=true${expectedRequestVersion === undefined ? '' : `&expectedRequestVersion=${encodeURIComponent(expectedRequestVersion)}`}`), secureReadInit());
    if (!response.ok) {
        throw new Error(`Ошибка API бюджета: ${response.status}`);
    }
    return publicBudgetPositionPage(await response.json(), id, true, expectedRequestVersion);
}
