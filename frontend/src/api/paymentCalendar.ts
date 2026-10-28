import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { isPublicExpenseRequestMoney, publicExpenseRequestMoneyCents } from './expenseRequestPublicResponse';
export type PaymentCalendarProgress = 'unpaid' | 'partiallyPaid' | 'paid';
export type PaymentCalendarStatus = 'TO_PAY' | 'PARTIALLY_PAID' | 'PAID' | 'CLOSED';
export type PaymentCalendarGrouping = 'date' | 'organization';
export type PaymentCalendarOrganization = {
    organizationId: string | null;
    organizationCode: string | null;
    organizationName: string | null;
};
// Older responses do not carry the split. Absence must remain unknown rather
// than silently labelling all settlement as bank cash.
export type PaymentCalendarSettlementBreakdown = {
    bankPaidAmount?: number;
    appliedOverpaymentAmount?: number;
    approvedRoundingAmount?: number;
};
export type PaymentCalendarRow = PaymentCalendarOrganization & PaymentCalendarSettlementBreakdown & {
    expenseRequestId: string;
    plannedPaymentDate: string;
    status: PaymentCalendarStatus;
    currencyCode: string;
    counterpartyName: string | null;
    purpose: string | null;
    obligationAmount: number;
    paidAmount: number;
    remainingAmount: number;
    progress: PaymentCalendarProgress;
    canOpen: boolean;
};
export type PaymentCalendarTotals = PaymentCalendarSettlementBreakdown & {
    obligationAmount: number;
    paidAmount: number;
    remainingAmount: number;
    overpaidAmount: number;
};
export type PaymentCalendarPage = {
    items: PaymentCalendarRow[];
    skip: number;
    take: number;
    total: number;
    totals: PaymentCalendarTotals;
    grouping: PaymentCalendarGrouping;
    organizationGroups: PaymentCalendarOrganizationGroup[];
};
export type PaymentCalendarOrganizationGroup = PaymentCalendarOrganization & {
    total: number;
    totals: PaymentCalendarTotals;
};
export type PaymentCalendarRequest = {
    from: string;
    through: string;
    skip: number;
    take: number;
    status: PaymentCalendarStatus | null;
    grouping?: PaymentCalendarGrouping;
};
export type PaymentCalendarExportRequest = Pick<PaymentCalendarRequest, 'from' | 'through' | 'status' | 'grouping'>;
export type PaymentCalendarExportDownload = {
    blob: Blob;
    fileName: string;
};
const exportUnavailableMessage = 'Не удалось сформировать файл платёжного календаря.';
const excelContentType = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
const exportFileNamePattern = /^payment-calendar-\d{8}-\d{6}Z\.xlsx$/u;
const malformedResponseMessage = 'Некорректный ответ API платёжного календаря.';
const unavailableMessage = 'Платёжный календарь сейчас недоступен.';
const statusValues: readonly PaymentCalendarStatus[] = ['TO_PAY', 'PARTIALLY_PAID', 'PAID', 'CLOSED'];
const progressValues: readonly PaymentCalendarProgress[] = ['unpaid', 'partiallyPaid', 'paid'];
const unsafePublicTextPattern = /[\u0000-\u001f\u007f-\u009f]/u;
const isoDatePattern = /^\d{4}-\d{2}-\d{2}$/u;
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/u;
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function isSafePublicText(value: string, maximumLength: number): boolean {
    return value.length > 0
        && value.length <= maximumLength
        && value.trim() === value
        && !unsafePublicTextPattern.test(value);
}
function isCalendarDate(value: unknown): value is string {
    if (typeof value !== 'string' || !isoDatePattern.test(value)) {
        return false;
    }
    // A shape match is not a date: 2026-02-30 passes the pattern and is not a
    // day that exists.
    const [year, month, day] = value.split('-').map(Number);
    const parsed = new Date(Date.UTC(year, month - 1, day));
    return parsed.getUTCFullYear() === year
        && parsed.getUTCMonth() === month - 1
        && parsed.getUTCDate() === day;
}
/// Money arrives as a JSON number of major units with two decimals. It is
/// checked in whole minor units so a value that cannot be represented exactly
/// -- and would therefore render a wrong sum -- is refused rather than rounded.
function isMoneyAmount(value: unknown): value is number {
    if (!isPublicExpenseRequestMoney(value))
        return false;
    const minorUnits = publicExpenseRequestMoneyCents(value);
    return minorUnits !== null && minorUnits <= 999999999999999;
}
function toMinorUnits(value: number): number {
    const cents = publicExpenseRequestMoneyCents(value);
    if (cents === null)
        throw new Error(malformedResponseMessage);
    return cents;
}
function publicSettlementBreakdown(value: Record<string, unknown>): PaymentCalendarSettlementBreakdown {
    const { bankPaidAmount, appliedOverpaymentAmount, paidAmount } = value;
    const approvedRoundingAmount = value.approvedRoundingAmount === undefined ? 0 : value.approvedRoundingAmount;
    if (bankPaidAmount === undefined && appliedOverpaymentAmount === undefined)
        return {};
    if (!isMoneyAmount(bankPaidAmount) || !isMoneyAmount(appliedOverpaymentAmount) || !isMoneyAmount(paidAmount)
        || !isMoneyAmount(approvedRoundingAmount)
        || toMinorUnits(bankPaidAmount) + toMinorUnits(appliedOverpaymentAmount) + toMinorUnits(approvedRoundingAmount) !== toMinorUnits(paidAmount)) {
        throw new Error(malformedResponseMessage);
    }
    return { bankPaidAmount, appliedOverpaymentAmount,
        ...(value.approvedRoundingAmount === undefined ? {} : { approvedRoundingAmount }) };
}
function assertBreakdownContains(whole: PaymentCalendarSettlementBreakdown, parts: PaymentCalendarSettlementBreakdown[], complete: boolean): void {
    for (const key of ['bankPaidAmount', 'appliedOverpaymentAmount', 'approvedRoundingAmount'] as const) {
        if (key === 'approvedRoundingAmount') {
            const wholeCents = toMinorUnits(whole[key] ?? 0);
            const sum = parts.reduce((total, part) => total + toMinorUnits(part[key] ?? 0), 0);
            if (!Number.isSafeInteger(sum) || sum > wholeCents || (complete && sum !== wholeCents))
                throw new Error(malformedResponseMessage);
            continue;
        }
        const wholeAmount = whole[key];
        if (wholeAmount === undefined) {
            if (parts.some((part) => part[key] !== undefined))
                throw new Error(malformedResponseMessage);
            continue;
        }
        let sum = 0;
        for (const part of parts) {
            const amount = part[key];
            if (amount === undefined)
                throw new Error(malformedResponseMessage);
            sum += toMinorUnits(amount);
            if (!Number.isSafeInteger(sum) || sum > toMinorUnits(wholeAmount))
                throw new Error(malformedResponseMessage);
        }
        if (complete && sum !== toMinorUnits(wholeAmount))
            throw new Error(malformedResponseMessage);
    }
}
function publicOrganization(value: Record<string, unknown>, allowLegacy: boolean): PaymentCalendarOrganization {
    const { organizationId, organizationCode, organizationName } = value;
    // Old date-only responses can still be read without inventing an organization.
    // An explicitly grouped response must carry all three fields, including nulls.
    if (allowLegacy && organizationId === undefined && organizationCode === undefined && organizationName === undefined) {
        return { organizationId: null, organizationCode: null, organizationName: null };
    }
    if (!(organizationId === null || (typeof organizationId === 'string' && uuidPattern.test(organizationId)
        && organizationId !== '00000000-0000-0000-0000-000000000000'))
        || !(organizationCode === null || (typeof organizationCode === 'string' && isSafePublicText(organizationCode, 100)))
        || !(organizationName === null || (typeof organizationName === 'string' && isSafePublicText(organizationName, 500)))
        || ((organizationCode === null) !== (organizationName === null))
        || (organizationId === null && organizationCode !== null)) {
        throw new Error(malformedResponseMessage);
    }
    // A non-null source ID with two null labels means an unresolved reference,
    // not the same group as an absent ID or another unresolved source ID.
    return { organizationId, organizationCode, organizationName };
}
function publicRow(value: unknown, requiresOrganization: boolean): PaymentCalendarRow {
    if (!isRecord(value)
        || typeof value.expenseRequestId !== 'string'
        || !uuidPattern.test(value.expenseRequestId)
        || !isCalendarDate(value.plannedPaymentDate)
        || typeof value.status !== 'string'
        || !statusValues.includes(value.status as PaymentCalendarStatus)
        || typeof value.currencyCode !== 'string'
        || value.currencyCode !== 'KZT'
        || !(value.counterpartyName === null
            || (typeof value.counterpartyName === 'string'
                && isSafePublicText(value.counterpartyName, 500)))
        || !(value.purpose === null
            || (typeof value.purpose === 'string' && isSafePublicText(value.purpose, 1000)))
        || !isMoneyAmount(value.obligationAmount)
        || value.obligationAmount <= 0
        || !isMoneyAmount(value.paidAmount)
        || !isMoneyAmount(value.remainingAmount)
        || typeof value.progress !== 'string'
        || !progressValues.includes(value.progress as PaymentCalendarProgress)
        || typeof value.canOpen !== 'boolean') {
        throw new Error(malformedResponseMessage);
    }
    // The arithmetic is checked in minor units, and the progress code must agree
    // with it: a tampered payload cannot render a misleading payment state.
    const obligation = toMinorUnits(value.obligationAmount);
    const paid = toMinorUnits(value.paidAmount);
    const remaining = toMinorUnits(value.remainingAmount);
    if (paid + remaining !== obligation || paid > obligation) {
        throw new Error(malformedResponseMessage);
    }
    const expectedProgress: PaymentCalendarProgress = paid === 0
        ? 'unpaid'
        : paid === obligation
            ? 'paid'
            : 'partiallyPaid';
    if (value.progress !== expectedProgress) {
        throw new Error(malformedResponseMessage);
    }
    return {
        ...publicOrganization(value, !requiresOrganization),
        ...publicSettlementBreakdown(value),
        expenseRequestId: value.expenseRequestId,
        plannedPaymentDate: value.plannedPaymentDate,
        status: value.status as PaymentCalendarStatus,
        currencyCode: value.currencyCode,
        counterpartyName: value.counterpartyName,
        purpose: value.purpose,
        obligationAmount: value.obligationAmount,
        paidAmount: value.paidAmount,
        remainingAmount: value.remainingAmount,
        progress: expectedProgress,
        canOpen: value.canOpen,
    };
}
function publicGroups(value: unknown, grouping: PaymentCalendarGrouping, items: PaymentCalendarRow[], total: number, totals: PaymentCalendarTotals): PaymentCalendarOrganizationGroup[] {
    if (grouping === 'date' && value === undefined)
        return [];
    if (!Array.isArray(value) || value.length > items.length || (grouping === 'date' && value.length !== 0)) {
        throw new Error(malformedResponseMessage);
    }
    if (grouping === 'date')
        return [];
    const groups = value.map((entry) => {
        if (!isRecord(entry) || !Number.isSafeInteger(entry.total) || (entry.total as number) <= 0
            || (entry.total as number) > total)
            throw new Error(malformedResponseMessage);
        return { ...publicOrganization(entry, false), total: entry.total as number, totals: publicTotals(entry.totals) };
    });
    const keyOf = (organization: PaymentCalendarOrganization) => organization.organizationId ?? '';
    const rowKeys = items.map(keyOf);
    const keys = [...new Set(rowKeys)];
    if (groups.length !== keys.length || groups.some((group, index) => keyOf(group) !== keys[index])) {
        throw new Error(malformedResponseMessage);
    }
    // Rows in the organization view must be contiguous; identical display names
    // are never used as the grouping key.
    const closedKeys = new Set<string>();
    let previousKey: string | undefined;
    for (const key of rowKeys) {
        if (key !== previousKey) {
            if (closedKeys.has(key))
                throw new Error(malformedResponseMessage);
            if (previousKey !== undefined)
                closedKeys.add(previousKey);
            previousKey = key;
        }
    }
    const moneyKeys = ['obligationAmount', 'paidAmount', 'remainingAmount', 'overpaidAmount'] as const;
    const visibleMoneyKeys = ['obligationAmount', 'paidAmount', 'remainingAmount'] as const;
    let representedCount = 0;
    const representedMoney = { obligationAmount: 0, paidAmount: 0, remainingAmount: 0, overpaidAmount: 0 };
    for (const group of groups) {
        const rows = items.filter((row) => keyOf(row) === keyOf(group));
        if (group.total < rows.length || rows.some((row) => row.organizationCode !== group.organizationCode
            || row.organizationName !== group.organizationName))
            throw new Error(malformedResponseMessage);
        assertBreakdownContains(group.totals, rows, group.total === rows.length);
        representedCount += group.total;
        if (!Number.isSafeInteger(representedCount) || representedCount > total)
            throw new Error(malformedResponseMessage);
        for (const key of visibleMoneyKeys) {
            const pageAmount = rows.reduce((sum, row) => sum + toMinorUnits(row[key]), 0);
            const groupAmount = toMinorUnits(group.totals[key]);
            if (!Number.isSafeInteger(pageAmount) || pageAmount > groupAmount
                || (group.total === rows.length && pageAmount !== groupAmount))
                throw new Error(malformedResponseMessage);
        }
        for (const key of moneyKeys) {
            representedMoney[key] += toMinorUnits(group.totals[key]);
            if (!Number.isSafeInteger(representedMoney[key]) || representedMoney[key] > toMinorUnits(totals[key])) {
                throw new Error(malformedResponseMessage);
            }
        }
    }
    // Only when these are all period groups may their totals be compared for
    // exact equality. A page may legitimately omit other organizations entirely.
    if (representedCount === total && moneyKeys.some((key) => representedMoney[key] !== toMinorUnits(totals[key]))) {
        throw new Error(malformedResponseMessage);
    }
    assertBreakdownContains(totals, groups.map((group) => group.totals), representedCount === total);
    return groups;
}
function publicTotals(value: unknown): PaymentCalendarTotals {
    if (!isRecord(value)
        || !isMoneyAmount(value.obligationAmount)
        || !isMoneyAmount(value.paidAmount)
        || !isMoneyAmount(value.remainingAmount)
        || !isMoneyAmount(value.overpaidAmount)) {
        throw new Error(malformedResponseMessage);
    }
    const obligation = toMinorUnits(value.obligationAmount);
    const paid = toMinorUnits(value.paidAmount);
    const remaining = toMinorUnits(value.remainingAmount);
    if (paid + remaining !== obligation || paid > obligation) {
        throw new Error(malformedResponseMessage);
    }
    return {
        ...publicSettlementBreakdown(value),
        obligationAmount: value.obligationAmount,
        paidAmount: value.paidAmount,
        remainingAmount: value.remainingAmount,
        overpaidAmount: value.overpaidAmount,
    };
}
/// Exact public parser. Every field the page renders is narrowed here; a field
/// the server might add later is discarded rather than becoming accidental UI
/// data.
export function publicPaymentCalendarPage(payload: unknown): PaymentCalendarPage {
    if (!isRecord(payload)
        || !Array.isArray(payload.items)
        || !Number.isSafeInteger(payload.skip)
        || (payload.skip as number) < 0
        || !Number.isSafeInteger(payload.take)
        || (payload.take as number) < 1
        || (payload.take as number) > 100
        || !Number.isSafeInteger(payload.total)
        || (payload.total as number) < 0) {
        throw new Error(malformedResponseMessage);
    }
    const grouping = payload.grouping === undefined ? 'date' : payload.grouping;
    if (grouping !== 'date' && grouping !== 'organization')
        throw new Error(malformedResponseMessage);
    const items = payload.items.map((item) => publicRow(item, grouping === 'organization'));
    const skip = payload.skip as number;
    const take = payload.take as number;
    const total = payload.total as number;
    if (items.length > take
        || items.length > total
        || (items.length > 0 && skip + items.length > total)) {
        throw new Error(malformedResponseMessage);
    }
    const seen = new Set<string>();
    for (const item of items) {
        if (seen.has(item.expenseRequestId)) {
            throw new Error(malformedResponseMessage);
        }
        seen.add(item.expenseRequestId);
    }
    const totals = publicTotals(payload.totals);
    assertBreakdownContains(totals, items, items.length === total);
    const organizationGroups = publicGroups(payload.organizationGroups, grouping, items, total, totals);
    return {
        items,
        skip,
        take,
        total,
        totals,
        grouping,
        organizationGroups,
    };
}
export async function getPaymentCalendar(request: PaymentCalendarRequest, signal?: AbortSignal): Promise<PaymentCalendarPage> {
    const grouping = request.grouping === undefined ? 'date' : request.grouping;
    if (grouping !== 'date' && grouping !== 'organization')
        throw new Error(unavailableMessage);
    const query = new URLSearchParams({
        from: request.from,
        through: request.through,
        skip: String(request.skip),
        take: String(request.take),
    });
    if (request.status !== null) {
        query.set('status', request.status);
    }
    if (request.grouping !== undefined)
        query.set('grouping', request.grouping);
    let response: Response;
    try {
        response = await fetchWithSafeTransport(apiUrl(`/api/v1/treasury/payment-calendar?${query.toString()}`), { ...secureReadInit(), signal });
    }
    catch (error) {
        // An aborted read is the caller replacing its own request, not a failure
        // to report.
        if (error instanceof DOMException && error.name === 'AbortError') {
            throw error;
        }
        throw new Error(unavailableMessage);
    }
    if (!response.ok) {
        throw new Error(unavailableMessage);
    }
    try {
        const page = publicPaymentCalendarPage(await response.json());
        if (page.grouping !== grouping)
            throw new Error(unavailableMessage);
        return page;
    }
    catch {
        throw new Error(unavailableMessage);
    }
}
function isExportCancellation(error: unknown): boolean {
    return (error instanceof DOMException || error instanceof Error) && error.name === 'AbortError';
}
function calendarExportFileName(contentDisposition: string | null): string {
    const candidate = /filename="?([^";]+)"?/iu.exec(contentDisposition ?? '')?.[1]?.trim();
    return candidate !== undefined && exportFileNamePattern.test(candidate)
        ? candidate
        : 'payment-calendar.xlsx';
}
/** One complete capture. Page offsets and page sizes never become export filters. */
export async function getPaymentCalendarExport(request: PaymentCalendarExportRequest, signal?: AbortSignal): Promise<PaymentCalendarExportDownload> {
    if (!isCalendarDate(request.from) || !isCalendarDate(request.through)
        || request.from > request.through
        || (request.status !== null && !statusValues.includes(request.status))
        || (request.grouping !== undefined && request.grouping !== 'date' && request.grouping !== 'organization')) {
        throw new Error(exportUnavailableMessage);
    }
    signal?.throwIfAborted();
    const query = new URLSearchParams({ from: request.from, through: request.through });
    if (request.status !== null)
        query.set('status', request.status);
    if (request.grouping !== undefined)
        query.set('grouping', request.grouping);
    let response: Response;
    try {
        response = await fetchWithSafeTransport(apiUrl(`/api/v1/treasury/payment-calendar/export.xlsx?${query.toString()}`), { ...secureReadInit(), signal });
    }
    catch (error) {
        if (isExportCancellation(error))
            throw error;
        throw new Error(exportUnavailableMessage);
    }
    // A problem document, even under a misleading status, is never a workbook.
    // Refusal bodies are not read and cannot become public UI text.
    if (!response.ok
        || (response.headers.get('content-type') ?? '').split(';')[0].trim().toLowerCase() !== excelContentType) {
        throw new Error(exportUnavailableMessage);
    }
    let blob: Blob;
    try {
        blob = await response.blob();
    }
    catch (error) {
        if (isExportCancellation(error))
            throw error;
        throw new Error(exportUnavailableMessage);
    }
    signal?.throwIfAborted();
    if (blob.size === 0)
        throw new Error(exportUnavailableMessage);
    return { blob, fileName: calendarExportFileName(response.headers.get('content-disposition')) };
}
