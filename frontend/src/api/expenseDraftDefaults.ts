import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import type { CurrencyRateRow } from './currencyRates';
export type ExpenseDraftDefaults = {
    businessDate: string;
    costCenterId: string | null;
    currencyRate: CurrencyRateRow | null;
};
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
export function validDay(value: unknown): value is string {
    if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/u.test(value))
        return false;
    const date = new Date(`${value}T00:00:00Z`);
    return Number.isFinite(date.valueOf()) && date.toISOString().slice(0, 10) === value;
}
export function parseExpenseDraftDefaults(value: unknown, code: string): ExpenseDraftDefaults {
    const x = value as ExpenseDraftDefaults | null;
    const rate = x?.currencyRate;
    if (!x || !validDay(x.businessDate) || !(x.costCenterId === null ||
        (typeof x.costCenterId === 'string' && guid.test(x.costCenterId) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/u.test(x.costCenterId)))
        || !(rate === null || (rate && rate.code === code && guid.test(rate.currencyId)
            && validDay(rate.rateDate) && rate.rateDate <= x.businessDate && typeof rate.name === 'string'
            && typeof rate.stamp === 'string' && typeof rate.rate === 'number' && Number.isFinite(rate.rate) && rate.rate > 0
            && typeof rate.multiplicity === 'number' && Number.isSafeInteger(rate.multiplicity) && rate.multiplicity > 0)))
        throw new Error('Некорректный ответ параметров расходной заявки.');
    return x;
}
export async function getExpenseDraftDefaults(code: string, signal: AbortSignal): Promise<ExpenseDraftDefaults> {
    if (!/^[A-Z]{3}$/u.test(code))
        throw new Error('Некорректная валюта.');
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/reference-data/currency-rates/expense-draft-defaults?currencyCode=' + code), { ...secureReadInit(), signal });
    if (!response.ok)
        throw new Error('Не удалось загрузить курс и параметры заявки.');
    return parseExpenseDraftDefaults(await response.json(), code);
}
