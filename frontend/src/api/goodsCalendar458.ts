import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { gridCents, publicPaymentCalendarGrid, sumGridAmounts, type PaymentCalendarGrid, type PaymentCalendarGridStatus } from './paymentCalendarGrid';
export const goodsCategories458 = ['PREPAYMENT', 'CONSIGNMENT', 'REALIZATION'] as const;
export type GoodsCategory458 = typeof goodsCategories458[number];
export const goodsLabels458: Record<GoodsCategory458, string> = { PREPAYMENT: 'Предоплата', CONSIGNMENT: 'Консигнация', REALIZATION: 'Реализация' };
export type GoodsCalendar458 = {
    calendar: PaymentCalendarGrid;
    categories: {
        code: GoodsCategory458;
        calendar: PaymentCalendarGrid;
    }[];
};
const fail = (): never => { throw new Error('Не удалось получить корректный товарный календарь.'); };
type Query = {
    from: string;
    through: string;
    status: PaymentCalendarGridStatus | null;
};
export function publicGoodsCalendar458(value: unknown, from: string, through: string): GoodsCalendar458 {
    if (!value || typeof value !== 'object' || !('calendar' in value) || !('categories' in value) || !Array.isArray(value.categories)
        || value.categories.length !== 3)
        return fail();
    const calendar = publicPaymentCalendarGrid(value.calendar, from, through, true);
    const categories = value.categories.map((part: unknown, index) => {
        if (!part || typeof part !== 'object' || !('code' in part) || part.code !== goodsCategories458[index] || !('calendar' in part))
            return fail();
        const section = publicPaymentCalendarGrid(part.calendar, from, through, true);
        if (section.businessTimeZoneId !== calendar.businessTimeZoneId)
            return fail();
        return { code: goodsCategories458[index], calendar: section };
    });
    const entries = categories.flatMap(c => c.calendar.entries.map(e => ({ ...e, goodsCategory: c.code })))
        .sort((a, b) => a.businessDate.localeCompare(b.businessDate));
    if (entries.length > 100000 || entries.length !== calendar.entries.length)
        return fail();
    const key = (e: PaymentCalendarGrid['entries'][number]) => JSON.stringify([e.businessDate, e.kind, e.expenseRequestId,
        e.currencyCode, e.amount, e.amountKzt, e.counterpartyName, e.canOpen, e.source ?? null, e.sourceEntryId ?? null, e.documentNumber ?? null]);
    const source = new Map<string, number>();
    for (const e of calendar.entries)
        source.set(key(e), (source.get(key(e)) ?? 0) + 1);
    for (const e of entries) {
        const count = source.get(key(e)) ?? 0;
        if (!count)
            return fail();
        source.set(key(e), count - 1);
    }
    for (let i = 0; i < calendar.days.length; i++) {
        const sum = sumGridAmounts(categories.map(c => c.calendar.days[i].amounts)), actual = calendar.days[i].amounts;
        if (gridCents(sum.knownPlanAmountKzt) !== gridCents(actual.knownPlanAmountKzt)
            || gridCents(sum.actualAmountKzt) !== gridCents(actual.actualAmountKzt) || sum.unknownPlanCount !== actual.unknownPlanCount)
            return fail();
    }
    return { calendar: { ...calendar, entries }, categories };
}
export async function getGoodsCalendar458(query: Query, signal?: AbortSignal): Promise<GoodsCalendar458> {
    const parameters = new URLSearchParams({ from: query.from, through: query.through });
    if (query.status !== null)
        parameters.set('status', query.status);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/treasury/goods-calendar/grid?${parameters}`), { ...secureReadInit(), signal });
    if (response.status !== 200)
        return fail();
    return publicGoodsCalendar458(await response.json(), query.from, query.through);
}
export async function getGoodsCalendarExport458(query: Query, signal?: AbortSignal) {
    const parameters = new URLSearchParams({ from: query.from, through: query.through });
    if (query.status !== null)
        parameters.set('status', query.status);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/treasury/goods-calendar/grid/export.xlsx?${parameters}`), { ...secureReadInit(), signal });
    if (response.status !== 200 || response.headers.get('Content-Type')?.split(';')[0].trim().toLowerCase()
        !== 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet')
        return fail();
    const fileName = /filename="?(goods-calendar-\d{8}-\d{6}Z\.xlsx)"?(?:;|$)/iu.exec(response.headers.get('Content-Disposition') ?? '')?.[1];
    const blob = await response.blob();
    if (!fileName || blob.size === 0)
        return fail();
    return { blob, fileName };
}
