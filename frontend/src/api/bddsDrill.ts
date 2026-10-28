import { apiUrl, fetchWithSafeTransport, secureWriteInit } from './http';
import { isPublicExpenseRequestUuid as uuid, isPublicExpenseRequestDate as validDay } from './expenseRequestPublicResponse';
export type BddsDrillSortColumn = 'entryDateLocal' | 'company' | 'documentType' | 'document' | 'counterparty' | 'notes' | 'comment' | 'amountKzt' | 'lineNo';
export type BddsDrillQuery = {
    year: number;
    fromMonth: string;
    toMonth: string;
    snapshotId: string;
    costCenterId: string | null;
    versionId: string | null;
    articleIds: string[];
    skip: number;
    take: 50 | 100;
    sortBy?: BddsDrillSortColumn;
    sortDirection?: 'asc' | 'desc';
};
export type BddsDrillContext = {
    query: BddsDrillQuery;
    title: string;
};
export type BddsDrillRow = {
    entryDateLocal: string;
    company: string;
    documentType: string;
    document: string;
    counterparty: string;
    notes: string;
    comment: string;
    amountKzt: number;
    lineNo: number;
};
export type BddsDrillPage = {
    snapshotId: string;
    capturedAtUtc: string;
    items: BddsDrillRow[];
    skip: number;
    take: number;
    total: number;
    totalKzt: number;
};
// Only an opaque workspace key is placed in browser history. No amounts, names,
// source references or draft fields are serialized there.
const contexts = new Map<string, BddsDrillContext>();
export function rememberBddsDrill(context: BddsDrillContext): string {
    const key = crypto.randomUUID();
    contexts.set(key, context);
    return key;
}
export const findBddsDrill = (key: string) => contexts.get(key);
const record = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const money = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v) && Number.isSafeInteger(Math.round(v * 100));
const integer = (v: unknown): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0;
export function publicBddsDrill(value: unknown, query: BddsDrillQuery): BddsDrillPage {
    if (!record(value) || value.snapshotId !== query.snapshotId || !uuid(value.snapshotId)
        || typeof value.capturedAtUtc !== 'string' || !Number.isFinite(Date.parse(value.capturedAtUtc))
        || value.skip !== query.skip || value.take !== query.take || !integer(value.total) || !money(value.totalKzt)
        || !Array.isArray(value.items) || value.items.length > query.take || value.items.length > Math.max(0, value.total - query.skip))
        throw new Error('Некорректный ответ расшифровки. Данные не показаны.');
    const items = value.items.map(row => {
        if (!record(row) || typeof row.entryDateLocal !== 'string' || !/^\d{4}-\d\d-\d\dT/u.test(row.entryDateLocal)
            || !validDay(row.entryDateLocal.slice(0, 10)) || !Number.isFinite(Date.parse(row.entryDateLocal)) || !money(row.amountKzt)
            || row.entryDateLocal.slice(0, 4) !== String(query.year)
            || row.entryDateLocal.slice(0, 7) < query.fromMonth.slice(0, 7) || row.entryDateLocal.slice(0, 7) > query.toMonth.slice(0, 7)
            || !integer(row.lineNo) || row.lineNo === 0
            || ['company', 'documentType', 'document', 'counterparty', 'notes', 'comment'].some(key => typeof row[key] !== 'string' || row[key].length > 1000000))
            throw new Error('Некорректный ответ расшифровки. Данные не показаны.');
        return row as BddsDrillRow;
    });
    return { snapshotId: value.snapshotId, capturedAtUtc: value.capturedAtUtc, items, skip: query.skip,
        take: query.take, total: value.total, totalKzt: value.totalKzt };
}
export async function getBddsDrill(query: BddsDrillQuery, signal: AbortSignal): Promise<BddsDrillPage> {
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/budget/reports/bdds/drill'), {
        ...secureWriteInit('POST', { 'Content-Type': 'application/json' }, JSON.stringify(query)), signal
    });
    if (!response.ok)
        throw new Error(response.status === 409
            ? 'Факт обновился или контроль сумм не пройден. Обновите БДДС и откройте расшифровку заново.'
            : response.status === 403 ? 'Нет доступа к выбранным статьям. Откройте БДДС заново.'
                : response.status === 401 ? 'Сеанс завершён. Войдите в BMS заново.'
                    : 'Расшифровка этого снимка пока недоступна. Проверьте журнал обмена или повторите позже.');
    return publicBddsDrill(await response.json(), query);
}
