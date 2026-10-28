import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import type { PaymentCalendarStatus } from './paymentCalendar';
export type DatedPaymentCalendarRequest = {
    from: string;
    through: string;
    skip: number;
    take: number;
    status: PaymentCalendarStatus | null;
    currency: string | null;
    grouping: 'date' | 'organization';
};
export type DatedPaymentCalendarExportRequest = Omit<DatedPaymentCalendarRequest, 'skip' | 'take'>;
export type DatedPaymentCalendarExportDownload = {
    blob: Blob;
    fileName: string;
};
export type DatedPaymentCalendarRowKind = 'PLAN' | 'BANK_PAYMENT' | 'BANK_REVERSAL' | 'APPLIED_ALLOCATION';
type Organization = {
    organizationId: string | null;
    organizationCode: string | null;
    organizationName: string | null;
};
export type DatedPaymentCalendarRow = Organization & {
    businessDate: string;
    kind: DatedPaymentCalendarRowKind;
    expenseRequestId: string;
    status: PaymentCalendarStatus;
    currencyCode: string;
    counterpartyName: string | null;
    purpose: string | null;
    planAmount: number | null;
    planAmountKzt: number | null;
    signedAmount: number | null;
    signedAmountKzt: number | null;
    canOpen: boolean;
};
export type DatedPaymentCalendarCurrencyTotals = {
    currencyCode: string;
    planCount: number;
    planAmount: number;
    bankPaymentAmount: number;
    bankReversalAmount: number;
    appliedAllocationAmount: number;
    movementAmount: number;
};
export type DatedPaymentCalendarKztTotals = {
    planCount: number;
    planKztUnknownCount: number;
    planAmountKzt: number | null;
    bankPaymentAmountKzt: number;
    bankReversalAmountKzt: number;
    appliedAllocationAmountKzt: number;
    movementAmountKzt: number;
};
export type DatedPaymentCalendarOrganizationGroup = Organization & {
    total: number;
    currencyTotals: DatedPaymentCalendarCurrencyTotals[];
    kztTotals: DatedPaymentCalendarKztTotals;
};
export type DatedPaymentCalendarPage = {
    items: DatedPaymentCalendarRow[];
    skip: number;
    take: number;
    total: number;
    grouping: 'date' | 'organization';
    businessTimeZoneId: string;
    currencyTotals: DatedPaymentCalendarCurrencyTotals[];
    kztTotals: DatedPaymentCalendarKztTotals;
    organizationGroups: DatedPaymentCalendarOrganizationGroup[];
};
const malformed = 'Некорректный ответ API датированного платёжного календаря.';
const unavailable = 'Датированный платёжный календарь сейчас недоступен.';
const exportUnavailable = 'Не удалось сформировать файл датированного платёжного календаря.';
const excelContentType = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
const maximumRows = 100000;
// Public number contract: at most 15 decimal minor-unit digits, as with the
// existing browser money boundary. Larger server decimals fail closed, not rounded.
const maximumMinorUnits = 999999999999999n;
const statuses: readonly string[] = ['TO_PAY', 'PARTIALLY_PAID', 'PAID', 'CLOSED'];
const kinds: readonly string[] = ['PLAN', 'BANK_PAYMENT', 'BANK_REVERSAL', 'APPLIED_ALLOCATION'];
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/u;
const emptyUuid = '00000000-0000-0000-0000-000000000000';
const controls = /[\u0000-\u001f\u007f-\u009f]/u;
const nonTextControls = /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/u;
function refuse(): never { throw new Error(malformed); }
function record(value: unknown): Record<string, unknown> {
    if (typeof value !== 'object' || value === null || Array.isArray(value))
        return refuse();
    return value as Record<string, unknown>;
}
function count(value: unknown, maximum = maximumRows): number {
    if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0 || value > maximum)
        return refuse();
    return value;
}
function text(value: unknown, maximum: number, multiline = false): string {
    if (typeof value !== 'string' || value.length === 0 || value.length > maximum || value.trim() !== value
        || (multiline ? nonTextControls : controls).test(value))
        return refuse();
    return value;
}
function optionalText(value: unknown, maximum: number, multiline = false): string | null {
    return value === null ? null : text(value, maximum, multiline);
}
function currency(value: unknown): string {
    if (typeof value !== 'string' || !/^[A-Z]{3}$/u.test(value))
        return refuse();
    return value;
}
function identity(value: unknown): string {
    if (typeof value !== 'string' || !uuid.test(value) || value === emptyUuid)
        return refuse();
    return value;
}
function calendarDate(value: unknown): string {
    if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/u.test(value))
        return refuse();
    const [year, month, day] = value.split('-').map(Number);
    const date = new Date(0);
    date.setUTCHours(0, 0, 0, 0);
    date.setUTCFullYear(year, month - 1, day);
    if (year < 1 || date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day)
        return refuse();
    return value;
}
/** No tolerance, binary multiplication or rounding: decimal spelling -> exact cents. */
function minor(value: unknown): bigint {
    if (typeof value !== 'number' || !Number.isFinite(value) || Object.is(value, -0))
        return refuse();
    const match = /^(-?)(\d+)(?:\.(\d+))?(?:e([+-]?\d+))?$/u.exec(String(value));
    if (!match)
        return refuse();
    const fraction = match[3] ?? '';
    const power = 2 + Number(match[4] ?? 0) - fraction.length;
    let units = BigInt(match[2] + fraction);
    if (power >= 0)
        units *= 10n ** BigInt(power);
    else {
        const divisor = 10n ** BigInt(-power);
        if (units % divisor !== 0n)
            return refuse();
        units /= divisor;
    }
    if (units > maximumMinorUnits)
        return refuse();
    return match[1] === '-' ? -units : units;
}
function money(value: unknown, sign: 'nonnegative' | 'nonpositive' | 'signed' = 'signed'): number {
    const units = minor(value);
    if ((sign === 'nonnegative' && units < 0n) || (sign === 'nonpositive' && units > 0n))
        return refuse();
    return value as number;
}
function nullableMoney(value: unknown): number | null { return value === null ? null : money(value, 'nonnegative'); }
function organization(value: Record<string, unknown>): Organization {
    const organizationId = value.organizationId === null ? null : identity(value.organizationId);
    const organizationCode = optionalText(value.organizationCode, 100);
    const organizationName = optionalText(value.organizationName, 500);
    if ((organizationCode === null) !== (organizationName === null) || (organizationId === null && organizationCode !== null))
        return refuse();
    return { organizationId, organizationCode, organizationName };
}
function validateRequest(request: DatedPaymentCalendarExportRequest): void {
    if (calendarDate(request.from) > calendarDate(request.through)
        || (request.status !== null && !statuses.includes(request.status))
        || (request.grouping !== 'date' && request.grouping !== 'organization'))
        return refuse();
    if (request.currency !== null)
        currency(request.currency);
}
function validatePageRequest(request: DatedPaymentCalendarRequest): void {
    validateRequest(request);
    count(request.skip, 2147483647);
    if (count(request.take, 100) === 0)
        return refuse();
}
function publicRow(payload: unknown, request: DatedPaymentCalendarRequest): DatedPaymentCalendarRow {
    const value = record(payload);
    const businessDate = calendarDate(value.businessDate);
    if (businessDate < request.from || businessDate > request.through
        || typeof value.kind !== 'string' || !kinds.includes(value.kind)
        || typeof value.status !== 'string' || !statuses.includes(value.status)
        || typeof value.canOpen !== 'boolean')
        return refuse();
    const kind = value.kind as DatedPaymentCalendarRowKind;
    const status = value.status as PaymentCalendarStatus;
    const currencyCode = currency(value.currencyCode);
    if ((request.status !== null && status !== request.status) || (request.currency !== null && currencyCode !== request.currency))
        return refuse();
    const planAmount = nullableMoney(value.planAmount);
    const planAmountKzt = nullableMoney(value.planAmountKzt);
    const signedAmount = value.signedAmount === null ? null : money(value.signedAmount);
    const signedAmountKzt = value.signedAmountKzt === null ? null : money(value.signedAmountKzt);
    if (kind === 'PLAN') {
        if (planAmount === null || signedAmount !== null || signedAmountKzt !== null
            || (currencyCode === 'KZT' && planAmount !== planAmountKzt))
            return refuse();
    }
    else {
        if (planAmount !== null || planAmountKzt !== null || signedAmount === null || signedAmountKzt === null
            || (kind === 'BANK_REVERSAL' ? signedAmount >= 0 || signedAmountKzt >= 0 : signedAmount <= 0 || signedAmountKzt <= 0)
            || (currencyCode === 'KZT' && signedAmount !== signedAmountKzt))
            return refuse();
    }
    return { businessDate, kind, expenseRequestId: identity(value.expenseRequestId), status, currencyCode,
        counterpartyName: optionalText(value.counterpartyName, 500), purpose: optionalText(value.purpose, 1000, true),
        ...organization(value), planAmount, planAmountKzt, signedAmount, signedAmountKzt, canOpen: value.canOpen };
}
type Vector = {
    plans: number;
    unknown: number;
    plan: bigint | null;
    bank: bigint;
    reversal: bigint;
    applied: bigint;
    movement: bigint;
};
function zero(): Vector { return { plans: 0, unknown: 0, plan: 0n, bank: 0n, reversal: 0n, applied: 0n, movement: 0n }; }
function currencyVector(value: DatedPaymentCalendarCurrencyTotals): Vector {
    return { plans: value.planCount, unknown: 0, plan: minor(value.planAmount), bank: minor(value.bankPaymentAmount),
        reversal: minor(value.bankReversalAmount), applied: minor(value.appliedAllocationAmount), movement: minor(value.movementAmount) };
}
function kztVector(value: DatedPaymentCalendarKztTotals): Vector {
    return { plans: value.planCount, unknown: value.planKztUnknownCount,
        plan: value.planAmountKzt === null ? null : minor(value.planAmountKzt), bank: minor(value.bankPaymentAmountKzt),
        reversal: minor(value.bankReversalAmountKzt), applied: minor(value.appliedAllocationAmountKzt), movement: minor(value.movementAmountKzt) };
}
function validateVector(value: Vector, total: number): void {
    if (value.plans > total || value.unknown > value.plans || (value.plan === null) !== (value.unknown > 0)
        || (value.plans === 0 && value.plan !== 0n)
        || value.bank + value.reversal + value.applied !== value.movement)
        return refuse();
}
function publicCurrencyTotals(payload: unknown, total: number, filter: string | null): DatedPaymentCalendarCurrencyTotals[] {
    if (!Array.isArray(payload) || payload.length > total)
        return refuse();
    const seen = new Set<string>();
    let requiredRows = 0;
    return payload.map((entry) => {
        const value = record(entry);
        const result = { currencyCode: currency(value.currencyCode), planCount: count(value.planCount),
            planAmount: money(value.planAmount, 'nonnegative'), bankPaymentAmount: money(value.bankPaymentAmount, 'nonnegative'),
            bankReversalAmount: money(value.bankReversalAmount, 'nonpositive'), appliedAllocationAmount: money(value.appliedAllocationAmount, 'nonnegative'),
            movementAmount: money(value.movementAmount) };
        if (seen.has(result.currencyCode) || (filter !== null && result.currencyCode !== filter))
            return refuse();
        seen.add(result.currencyCode);
        validateVector(currencyVector(result), total);
        const minimum = result.planCount + Number(result.bankPaymentAmount > 0) + Number(result.bankReversalAmount < 0)
            + Number(result.appliedAllocationAmount > 0);
        requiredRows += minimum;
        if (minimum === 0 || requiredRows > total)
            return refuse();
        return result;
    });
}
function publicKztTotals(payload: unknown, total: number, currencies: DatedPaymentCalendarCurrencyTotals[]): DatedPaymentCalendarKztTotals {
    const value = record(payload);
    const result = { planCount: count(value.planCount), planKztUnknownCount: count(value.planKztUnknownCount),
        planAmountKzt: nullableMoney(value.planAmountKzt), bankPaymentAmountKzt: money(value.bankPaymentAmountKzt, 'nonnegative'),
        bankReversalAmountKzt: money(value.bankReversalAmountKzt, 'nonpositive'), appliedAllocationAmountKzt: money(value.appliedAllocationAmountKzt, 'nonnegative'),
        movementAmountKzt: money(value.movementAmountKzt) };
    const vector = kztVector(result);
    validateVector(vector, total);
    if (result.planCount !== currencies.reduce((sum, item) => sum + item.planCount, 0)
        || result.planKztUnknownCount > currencies.reduce((sum, item) => sum + (item.currencyCode === 'KZT' ? 0 : item.planCount), 0)
        || (total === 0 && (currencies.length !== 0 || Object.values(vector).some((item) => item !== 0 && item !== 0n)))
        || (total > 0 && currencies.length === 0))
        return refuse();
    // A component is present in KZT exactly when it is present in a currency.
    for (const key of ['bank', 'reversal', 'applied'] as const) {
        if ((vector[key] === 0n) !== currencies.every((item) => currencyVector(item)[key] === 0n))
            return refuse();
    }
    const kzt = currencies.find((item) => item.currencyCode === 'KZT');
    if (kzt)
        contains(vector, currencyVector(kzt), currencies.length === 1);
    return result;
}
function sumVectors(parts: Vector[]): Vector {
    return parts.reduce((sum, part) => ({ plans: sum.plans + part.plans, unknown: sum.unknown + part.unknown,
        plan: sum.plan === null || part.plan === null ? null : sum.plan + part.plan,
        bank: sum.bank + part.bank, reversal: sum.reversal + part.reversal, applied: sum.applied + part.applied,
        movement: sum.movement + part.movement }), zero());
}
/** Compare monotone components, never signed net (a later reversal may reduce it). */
function contains(whole: Vector, part: Vector, complete: boolean): void {
    if (part.plans > whole.plans || part.unknown > whole.unknown || part.bank > whole.bank
        || part.reversal < whole.reversal || part.applied > whole.applied
        || (whole.plan !== null && (part.plan === null || part.plan > whole.plan)))
        return refuse();
    if (complete && (Object.keys(whole) as (keyof Vector)[]).some((key) => whole[key] !== part[key]))
        return refuse();
}
function rowsVector(rows: DatedPaymentCalendarRow[], inKzt: boolean): Vector {
    const result = zero();
    for (const row of rows) {
        if (row.kind === 'PLAN') {
            result.plans++;
            const amount = inKzt ? row.planAmountKzt : row.planAmount;
            if (amount === null) {
                result.unknown++;
                result.plan = null;
            }
            else if (result.plan !== null)
                result.plan += minor(amount);
        }
        else {
            const amount = minor(inKzt ? row.signedAmountKzt : row.signedAmount);
            if (row.kind === 'BANK_PAYMENT')
                result.bank += amount;
            else if (row.kind === 'BANK_REVERSAL')
                result.reversal += amount;
            else
                result.applied += amount;
            result.movement += amount;
        }
    }
    return result;
}
function checkRows(rows: DatedPaymentCalendarRow[], total: number, currencies: DatedPaymentCalendarCurrencyTotals[], kzt: DatedPaymentCalendarKztTotals): void {
    const complete = rows.length === total;
    const byCurrency = new Map(currencies.map((item) => [item.currencyCode, item]));
    if (rows.some((row) => !byCurrency.has(row.currencyCode)))
        return refuse();
    for (const item of currencies)
        contains(currencyVector(item), rowsVector(rows.filter((row) => row.currencyCode === item.currencyCode), false), complete);
    contains(kztVector(kzt), rowsVector(rows, true), complete);
}
function checkIdentitiesAndOrder(rows: DatedPaymentCalendarRow[], grouping: DatedPaymentCalendarRequest['grouping']): void {
    const seenPlans = new Set<string>();
    const metadata = new Map<string, string>();
    const labels = new Map<string | null, string>();
    let previous: DatedPaymentCalendarRow | undefined;
    const compare = (left: string | null, right: string | null): number => left === right ? 0 : left === null ? -1 : right === null ? 1 : left < right ? -1 : 1;
    for (const row of rows) {
        if (row.kind === 'PLAN' && seenPlans.has(row.expenseRequestId))
            return refuse();
        if (row.kind === 'PLAN')
            seenPlans.add(row.expenseRequestId);
        const state = JSON.stringify([row.status, row.currencyCode, row.counterpartyName, row.purpose,
            row.organizationId, row.organizationCode, row.organizationName, row.canOpen]);
        if (metadata.has(row.expenseRequestId) && metadata.get(row.expenseRequestId) !== state)
            return refuse();
        metadata.set(row.expenseRequestId, state);
        const label = JSON.stringify([row.organizationCode, row.organizationName]);
        if (labels.has(row.organizationId) && labels.get(row.organizationId) !== label)
            return refuse();
        labels.set(row.organizationId, label);
        if (previous) {
            const organizationOrder = grouping === 'organization'
                ? Number(previous.organizationId === null) - Number(row.organizationId === null)
                    || compare(previous.organizationCode, row.organizationCode) || compare(previous.organizationId, row.organizationId)
                : 0;
            const order = organizationOrder || compare(previous.businessDate, row.businessDate)
                || compare(previous.currencyCode, row.currencyCode) || compare(previous.expenseRequestId, row.expenseRequestId);
            if (order > 0)
                return refuse();
        }
        // Equal-looking movement rows are legitimate: the public DTO deliberately
        // omits source identities. Do not invent a key or deduplicate/cap them.
        previous = row;
    }
}
function publicGroups(payload: unknown, rows: DatedPaymentCalendarRow[], request: DatedPaymentCalendarRequest, total: number, currencies: DatedPaymentCalendarCurrencyTotals[], kzt: DatedPaymentCalendarKztTotals): DatedPaymentCalendarOrganizationGroup[] {
    if (!Array.isArray(payload) || payload.length > rows.length || (request.grouping === 'date' && payload.length !== 0))
        return refuse();
    const keys = [...new Set(rows.map((row) => row.organizationId))];
    if (request.grouping === 'date')
        return [];
    if (payload.length !== keys.length)
        return refuse();
    const groups = payload.map((entry, index) => {
        const value = record(entry);
        const group = organization(value);
        const groupTotal = count(value.total, total);
        if (groupTotal === 0 || group.organizationId !== keys[index])
            return refuse();
        const members = rows.filter((row) => row.organizationId === group.organizationId);
        if (members.length > groupTotal || members.some((row) => row.organizationCode !== group.organizationCode
            || row.organizationName !== group.organizationName))
            return refuse();
        const currencyTotals = publicCurrencyTotals(value.currencyTotals, groupTotal, request.currency);
        const kztTotals = publicKztTotals(value.kztTotals, groupTotal, currencyTotals);
        checkRows(members, groupTotal, currencyTotals, kztTotals);
        return { ...group, total: groupTotal, currencyTotals, kztTotals };
    });
    const represented = groups.reduce((sum, group) => sum + group.total, 0);
    if (represented > total)
        return refuse();
    const complete = represented === total;
    const globalCodes = new Set(currencies.map((item) => item.currencyCode));
    if (groups.some((group) => group.currencyTotals.some((item) => !globalCodes.has(item.currencyCode))))
        return refuse();
    for (const item of currencies) {
        const parts = groups.flatMap((group) => group.currencyTotals.filter((part) => part.currencyCode === item.currencyCode));
        contains(currencyVector(item), sumVectors(parts.map(currencyVector)), complete);
    }
    contains(kztVector(kzt), sumVectors(groups.map((group) => kztVector(group.kztTotals))), complete);
    return groups;
}
/** Whitelist every rendered DTO field; extra diagnostics/source IDs never escape. */
export function publicDatedPaymentCalendarPage(payload: unknown, request: DatedPaymentCalendarRequest): DatedPaymentCalendarPage {
    validatePageRequest(request);
    const value = record(payload);
    const skip = count(value.skip, 2147483647);
    const take = count(value.take, 100);
    const total = count(value.total);
    if (skip !== request.skip || take !== request.take || value.grouping !== request.grouping
        || !Array.isArray(value.items) || value.items.length !== Math.min(take, Math.max(0, total - skip)))
        return refuse();
    const items = value.items.map((item) => publicRow(item, request));
    checkIdentitiesAndOrder(items, request.grouping);
    const currencyTotals = publicCurrencyTotals(value.currencyTotals, total, request.currency);
    const kztTotals = publicKztTotals(value.kztTotals, total, currencyTotals);
    checkRows(items, total, currencyTotals, kztTotals);
    const organizationGroups = publicGroups(value.organizationGroups, items, request, total, currencyTotals, kztTotals);
    return { items, skip, take, total, grouping: request.grouping, businessTimeZoneId: text(value.businessTimeZoneId, 200),
        currencyTotals, kztTotals, organizationGroups };
}
function isAbort(error: unknown): boolean {
    return (error instanceof Error || error instanceof DOMException) && error.name === 'AbortError';
}
function rethrowSafe(error: unknown, message: string, signal?: AbortSignal): never {
    signal?.throwIfAborted();
    if (isAbort(error))
        throw error;
    throw new Error(message);
}
function queryFor(request: DatedPaymentCalendarExportRequest): URLSearchParams {
    const query = new URLSearchParams({ from: request.from, through: request.through });
    if (request.status !== null)
        query.set('status', request.status);
    if (request.currency !== null)
        query.set('currency', request.currency);
    query.set('grouping', request.grouping);
    return query;
}
export async function getDatedPaymentCalendar(request: DatedPaymentCalendarRequest, signal?: AbortSignal): Promise<DatedPaymentCalendarPage> {
    signal?.throwIfAborted();
    try {
        validatePageRequest(request);
        const query = queryFor(request);
        query.set('skip', String(request.skip));
        query.set('take', String(request.take));
        const response = await fetchWithSafeTransport(apiUrl(`/api/v1/treasury/payment-calendar/dated?${query.toString()}`), { ...secureReadInit(), signal });
        signal?.throwIfAborted();
        if (!response.ok || (response.headers.get('content-type') ?? '').split(';')[0].trim().toLowerCase() !== 'application/json') {
            throw new Error(unavailable); // Do not read a refusal/problem body.
        }
        const page = publicDatedPaymentCalendarPage(await response.json(), request);
        signal?.throwIfAborted();
        return page;
    }
    catch (error) {
        return rethrowSafe(error, unavailable, signal);
    }
}
function safeFileName(header: string | null): string {
    // Only one ordinary filename parameter is accepted. An extended filename,
    // duplicate, path, control or unexpected suffix falls back to a local name.
    const matches = [...(header ?? '').matchAll(/(?:^|;)\s*filename\s*=\s*(?:"([^"]*)"|([^;\s]+))\s*(?=;|$)/giu)];
    const candidate = matches.length === 1 ? matches[0][1] ?? matches[0][2] : undefined;
    if (candidate && !controls.test(header ?? '') && !/(?:^|;)\s*filename\*/iu.test(header ?? '')
        && /^dated-payment-calendar-\d{8}-\d{6}Z\.xlsx$/u.test(candidate))
        return candidate;
    return 'dated-payment-calendar.xlsx';
}
/** One complete capture: skip/take are intentionally absent from the URL. */
export async function exportDatedPaymentCalendar(request: DatedPaymentCalendarExportRequest, signal?: AbortSignal): Promise<DatedPaymentCalendarExportDownload> {
    signal?.throwIfAborted();
    try {
        validateRequest(request);
        const response = await fetchWithSafeTransport(apiUrl(`/api/v1/treasury/payment-calendar/dated/export.xlsx?${queryFor(request).toString()}`), { ...secureReadInit(), signal });
        signal?.throwIfAborted();
        if (!response.ok || (response.headers.get('content-type') ?? '').split(';')[0].trim().toLowerCase() !== excelContentType) {
            throw new Error(exportUnavailable);
        }
        const blob = await response.blob();
        signal?.throwIfAborted();
        if (blob.size === 0)
            throw new Error(exportUnavailable);
        return { blob, fileName: safeFileName(response.headers.get('content-disposition')) };
    }
    catch (error) {
        return rethrowSafe(error, exportUnavailable, signal);
    }
}
