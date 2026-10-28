import { apiUrl, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
export type CurrencyRateRow = {
    currencyId: string;
    code: string;
    name: string;
    rateDate: string;
    rate: number | null;
    multiplicity: number | null;
    sourceName: string | null;
    stamp: string;
};
export type CurrencyRatePage = {
    businessDate: string;
    date: string;
    items: CurrencyRateRow[];
    total: number;
    skip: number;
    take: number;
    notice: string | null;
};
const path = '/api/v1/reference-data/currency-rates';
function fail(status: number): never {
    throw new Error(status === 409 ? 'Курс изменён другим пользователем. Обновите данные.'
        : status === 403 ? 'Недостаточно прав для изменения курса.'
            : status === 400 ? 'Проверьте дату, положительный курс, целую кратность и причину ручного ввода.'
                : 'Не удалось подтвердить операцию с курсами. Обновите данные и проверьте результат.');
}
export async function getCurrencyRates(date: string, skip: number, signal: AbortSignal): Promise<CurrencyRatePage> {
    const response = await fetchWithSafeTransport(apiUrl(`${path}/?skip=${skip}&take=25${date ? `&date=${encodeURIComponent(date)}` : ''}`), { ...secureReadInit(), signal });
    if (!response.ok)
        fail(response.status);
    const page: CurrencyRatePage = await response.json();
    if (!page || !Array.isArray(page.items) || !Number.isSafeInteger(page.total) || page.total < 0
        || !/^\d{4}-\d{2}-\d{2}$/u.test(page.businessDate) || !/^\d{4}-\d{2}-\d{2}$/u.test(page.date)
        || page.skip !== skip || page.take !== 25 || page.items.length > 25
        || page.items.some(item => !/^[A-Z]{3}$/u.test(item.code) || typeof item.name !== 'string'
            || typeof item.stamp !== 'string' || (item.rate !== null && (!Number.isFinite(item.rate) || item.rate <= 0))
            || (item.multiplicity !== null && (!Number.isSafeInteger(item.multiplicity) || item.multiplicity <= 0))))
        throw new Error('Некорректный ответ справочника курсов валют.');
    return page;
}
export async function writeManualCurrencyRate(input: {
    currencyId: string;
    date: string;
    rate: number;
    multiplicity: number;
    expectedStamp: string;
    reason: string;
}): Promise<void> {
    const response = await fetchWithSafeTransport(apiUrl(`${path}/manual`), secureWriteInit('PUT', { 'Content-Type': 'application/json' }, JSON.stringify(input)));
    if (!response.ok)
        fail(response.status);
}
export async function importCurrencyRates(): Promise<boolean> {
    const response = await fetchWithSafeTransport(apiUrl(`${path}/import`), secureWriteInit('POST', {}));
    if (!response.ok)
        fail(response.status);
    const result: unknown = await response.json();
    if (!result || typeof result !== 'object' || !('succeeded' in result) || typeof result.succeeded !== 'boolean')
        throw new Error('Не удалось подтвердить загрузку курсов.');
    return result.succeeded;
}
