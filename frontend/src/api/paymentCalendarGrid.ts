import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { useMockApi } from './runtime';
import { publicExpenseRequestMoneyCents } from './expenseRequestPublicResponse';
import type { PaymentCalendarStatus } from './paymentCalendar';
export type PaymentCalendarGridStatus = PaymentCalendarStatus | 'IN_APPROVAL' | 'RETURNED' | 'AWAITING_NSI' | 'APPROVED';
export const paymentCalendarGridStatuses: readonly PaymentCalendarGridStatus[] = ['IN_APPROVAL', 'RETURNED', 'AWAITING_NSI', 'APPROVED', 'TO_PAY', 'PARTIALLY_PAID', 'PAID', 'CLOSED'];
export type GridAmounts = {
    knownPlanAmountKzt: number;
    unknownPlanCount: number;
    actualAmountKzt: number;
    planAmountKzt: number | null;
    totalAmountKzt: number | null;
};
export type GridEntry = {
    businessDate: string;
    kind: 'PLAN' | 'BANK_PAYMENT' | 'BANK_REVERSAL';
    expenseRequestId: string | null;
    currencyCode: string;
    amount: number;
    amountKzt: number | null;
    counterpartyName: string | null;
    canOpen: boolean;
    goodsCategory?: string;
    source?: 'ODDS' | null;
    sourceEntryId?: string | null;
    documentNumber?: string | null;
};
export type GridDay = {
    date: string;
    amounts: GridAmounts;
    entryCount: number;
};
export type PaymentCalendarGrid = {
    from: string;
    through: string;
    businessTimeZoneId: string;
    days: GridDay[];
    totals: GridAmounts;
    entries: GridEntry[];
};
const errorMessage = 'Не удалось получить корректный календарь платежей.';
const fail = (): never => { throw new Error(errorMessage); };
function record(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}
export function gridDate(value: unknown): value is string {
    return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/u.test(value)
        && !Number.isNaN(Date.parse(value + 'T00:00:00Z'))
        && new Date(value + 'T00:00:00Z').toISOString().slice(0, 10) === value;
}
export function gridCents(value: unknown): number {
    if (typeof value !== 'number' || !Number.isFinite(value))
        return fail();
    const cents = publicExpenseRequestMoneyCents(Math.abs(value));
    if (cents === null || !Number.isSafeInteger(cents))
        return fail();
    return value < 0 ? -cents : cents;
}
function amounts(value: unknown): GridAmounts {
    if (!record(value) || !Number.isSafeInteger(value.unknownPlanCount)
        || (value.unknownPlanCount as number) < 0 || (value.unknownPlanCount as number) > 100000)
        return fail();
    const plan = gridCents(value.knownPlanAmountKzt), actual = gridCents(value.actualAmountKzt);
    if (plan < 0)
        return fail();
    if (value.unknownPlanCount === 0) {
        if (value.planAmountKzt === null || value.totalAmountKzt === null
            || gridCents(value.planAmountKzt) !== plan || gridCents(value.totalAmountKzt) !== plan + actual)
            return fail();
    }
    else if (value.planAmountKzt !== null || value.totalAmountKzt !== null)
        return fail();
    return value as GridAmounts;
}
export function sumGridAmounts(parts: readonly GridAmounts[]): GridAmounts {
    let plan = 0, actual = 0, unknown = 0;
    for (const part of parts) {
        plan += gridCents(part.knownPlanAmountKzt);
        actual += gridCents(part.actualAmountKzt);
        unknown += part.unknownPlanCount;
        if (!Number.isSafeInteger(plan) || !Number.isSafeInteger(actual) || !Number.isSafeInteger(plan + actual))
            return fail();
    }
    return { knownPlanAmountKzt: plan / 100, actualAmountKzt: actual / 100, unknownPlanCount: unknown,
        planAmountKzt: unknown === 0 ? plan / 100 : null, totalAmountKzt: unknown === 0 ? (plan + actual) / 100 : null };
}
function sameAmounts(left: GridAmounts, right: GridAmounts): boolean {
    return gridCents(left.knownPlanAmountKzt) === gridCents(right.knownPlanAmountKzt)
        && gridCents(left.actualAmountKzt) === gridCents(right.actualAmountKzt) && left.unknownPlanCount === right.unknownPlanCount;
}
export function publicPaymentCalendarGrid(value: unknown, from: string, through: string, goodsSplit = false): PaymentCalendarGrid {
    if (!record(value) || value.from !== from || value.through !== through || !gridDate(from) || !gridDate(through)
        || from > through || typeof value.businessTimeZoneId !== 'string' || value.businessTimeZoneId.length < 1
        || value.businessTimeZoneId.length > 150 || /[\u0000-\u001f\u007f-\u009f]/u.test(value.businessTimeZoneId)
        || !Array.isArray(value.days) || value.days.length > 366 || !Array.isArray(value.entries) || value.entries.length > 100000)
        return fail();
    const totals = amounts(value.totals);
    const oddsKeys = new Set<string>();
    const entries: GridEntry[] = value.entries.map(entry => {
        const odds = record(entry) && goodsSplit && entry.source === 'ODDS';
        if (!record(entry) || !gridDate(entry.businessDate) || entry.businessDate < from || entry.businessDate > through
            || !['PLAN', 'BANK_PAYMENT', 'BANK_REVERSAL'].includes(String(entry.kind))
            || (entry.expenseRequestId === null ? !odds || entry.canOpen : typeof entry.expenseRequestId !== 'string'
                || !/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/iu.test(entry.expenseRequestId))
            || typeof entry.currencyCode !== 'string' || !/^[A-Z]{3}$/u.test(entry.currencyCode) || typeof entry.canOpen !== 'boolean'
            || (entry.counterpartyName !== null && (typeof entry.counterpartyName !== 'string' || entry.counterpartyName.length > 500
                || /[\u0000-\u001f\u007f-\u009f]/u.test(entry.counterpartyName))))
            return fail();
        if (odds) {
            if (typeof entry.sourceEntryId !== 'string' || !/^[0-9A-F]{64}$/u.test(entry.sourceEntryId)
                || oddsKeys.has(entry.sourceEntryId) || entry.kind === 'PLAN' || entry.currencyCode !== 'KZT'
                || typeof entry.documentNumber !== 'string' || entry.documentNumber.length > 100
                || /[\u0000-\u001f\u007f-\u009f]/u.test(entry.documentNumber))
                return fail();
            oddsKeys.add(entry.sourceEntryId);
        }
        else if (entry.source !== undefined && entry.source !== null)
            return fail();
        const amount = gridCents(entry.amount);
        if ((entry.kind === 'BANK_REVERSAL' ? amount > 0 || amount === 0 && !goodsSplit : amount < 0 || amount === 0 && !goodsSplit)
            || amount === 0 && (entry.amountKzt === null || gridCents(entry.amountKzt) === 0)
            || (entry.amountKzt === null && (entry.kind !== 'PLAN' || entry.currencyCode === 'KZT')))
            return fail();
        if (entry.amountKzt !== null) {
            const kzt = gridCents(entry.amountKzt);
            if ((entry.kind === 'BANK_REVERSAL' ? kzt > 0 : kzt < 0)
                || (entry.currencyCode === 'KZT' && kzt !== amount))
                return fail();
        }
        return entry as GridEntry;
    });
    const rowsByDay = new Map<string, GridEntry[]>();
    for (const entry of entries) {
        const rows = rowsByDay.get(entry.businessDate) ?? [];
        rows.push(entry);
        rowsByDay.set(entry.businessDate, rows);
    }
    const days: GridDay[] = value.days.map((day, index) => {
        const expected = new Date(from + 'T00:00:00Z');
        expected.setUTCDate(expected.getUTCDate() + index);
        if (!record(day) || day.date !== expected.toISOString().slice(0, 10) || day.date > through
            || !Number.isSafeInteger(day.entryCount))
            return fail();
        const rows = rowsByDay.get(day.date) ?? [];
        if (rows.length !== day.entryCount)
            return fail();
        const result = amounts(day.amounts);
        const computed = sumGridAmounts(rows.map(row => ({ knownPlanAmountKzt: row.kind === 'PLAN' ? row.amountKzt ?? 0 : 0,
            unknownPlanCount: row.amountKzt === null ? 1 : 0, actualAmountKzt: row.kind !== 'PLAN' ? row.amountKzt! : 0,
            planAmountKzt: null, totalAmountKzt: null })));
        if (!sameAmounts(result, computed))
            return fail();
        return { date: day.date, amounts: result, entryCount: rows.length };
    });
    if (days.length === 0 || days.at(-1)!.date !== through || !sameAmounts(sumGridAmounts(days.map(day => day.amounts)), totals))
        return fail();
    return { from, through, businessTimeZoneId: value.businessTimeZoneId, days, totals, entries };
}
export async function getPaymentCalendarGrid(query: {
    from: string;
    through: string;
    status: PaymentCalendarGridStatus | null;
}, signal?: AbortSignal): Promise<PaymentCalendarGrid> {
    if (!gridDate(query.from) || !gridDate(query.through) || query.from > query.through
        || (query.status !== null && !paymentCalendarGridStatuses.includes(query.status)) || useMockApi)
        return fail();
    const parameters = new URLSearchParams({ from: query.from, through: query.through });
    if (query.status !== null)
        parameters.set('status', query.status);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/treasury/payment-calendar/grid?${parameters}`), { ...secureReadInit(), signal });
    if (response.status !== 200)
        return fail();
    return publicPaymentCalendarGrid(await response.json(), query.from, query.through);
}
export async function getPaymentCalendarGridExport(query: {
    from: string;
    through: string;
    status: PaymentCalendarGridStatus | null;
}, signal?: AbortSignal): Promise<{
    blob: Blob;
    fileName: string;
}> {
    if (!gridDate(query.from) || !gridDate(query.through) || query.from > query.through
        || query.status !== null && !paymentCalendarGridStatuses.includes(query.status) || useMockApi)
        return fail();
    const parameters = new URLSearchParams({ from: query.from, through: query.through });
    if (query.status !== null)
        parameters.set('status', query.status);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/treasury/payment-calendar/grid/export.xlsx?${parameters}`), { ...secureReadInit(), signal });
    if (response.status !== 200 || response.headers.get('Content-Type')?.split(';')[0].trim().toLowerCase()
        !== 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet')
        return fail();
    const disposition = response.headers.get('Content-Disposition') ?? '';
    const fileName = /filename="?(payment-calendar-grid-\d{8}-\d{6}Z\.xlsx)"?(?:;|$)/iu.exec(disposition)?.[1];
    if (!fileName)
        return fail();
    const blob = await response.blob();
    if (blob.size === 0)
        return fail();
    return { blob, fileName };
}
