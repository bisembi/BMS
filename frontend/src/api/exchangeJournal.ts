import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { isPublicExpenseRequestUuid as uuid } from './expenseRequestPublicResponse';
export type ExchangeKind = 'all' | 'dds' | 'currency' | 'references' | 'payments';
export type ExchangeStatus = 'all' | 'success' | 'failed' | 'running' | 'warning';
export type ExchangeFilter = {
    fromUtc?: string;
    toUtc?: string;
    kind: ExchangeKind;
    status: ExchangeStatus;
};
export type ExchangeItem = {
    id: string;
    kind: Exclude<ExchangeKind, 'all'>;
    startedAtUtc: string;
    finishedAtUtc: string | null;
    name: string;
    status: Exclude<ExchangeStatus, 'all'>;
    recordCount: number | null;
    description: string;
    hasDetails: boolean;
};
export type ExchangeFact = {
    capturedAtUtc: string | null;
    checkedAtUtc: string | null;
    cutoffExclusiveLocal: string | null;
    latestBusinessDate: string | null;
    sourceLegs: number | null;
    receiptsAmountKzt: string | null;
    paymentsAmountKzt: string | null;
    netCashFlowAmountKzt: string | null;
};
export type ExchangePage = {
    items: ExchangeItem[];
    skip: number;
    take: number;
    total: number;
    coverageComplete: boolean;
    warnings: string[];
    fact: ExchangeFact | null;
};
export type ExchangeDetail = {
    title: string;
    information: string;
    columns: string[];
    rows: string[][];
    skip: number;
    take: number;
    total: number;
    totals: {
        label: string;
        amountKzt: string;
    }[];
};
const record = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const text = (v: unknown): v is string => typeof v === 'string' && v.length <= 2048 && !/[\u0000-\u001f\u007f]/u.test(v);
const integer = (v: unknown): v is number => Number.isSafeInteger(v) && (v as number) >= 0;
const time = (v: unknown): v is string => typeof v === 'string' && v.length < 80 && /^\d{4}-\d{2}-\d{2}T/.test(v) && Number.isFinite(Date.parse(v));
const day = (v: unknown): v is string => typeof v === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(v) && Number.isFinite(Date.parse(v));
const money = (v: unknown): v is string => typeof v === 'string' && /^-?\d{1,16}(?:\.\d{1,8})?$/.test(v);
const kinds = new Set(['dds', 'currency', 'references', 'payments']), statuses = new Set(['success', 'failed', 'running', 'warning']);
const invalid = () => new Error('Не удалось прочитать журнал обмена.');
function page(value: Record<string, unknown>, skip: number, take: number) {
    if (value.skip !== skip || value.take !== take || !integer(value.total))
        throw invalid();
}
export function publicExchangePage(value: unknown, skip: number, take: number): ExchangePage {
    if (!record(value))
        throw invalid();
    page(value, skip, take);
    if (!Array.isArray(value.items) || value.items.length > take || typeof value.coverageComplete !== 'boolean'
        || !Array.isArray(value.warnings) || value.warnings.length > 20 || !value.warnings.every(text))
        throw invalid();
    const items = value.items.map(raw => {
        if (!record(raw) || !uuid(raw.id) || !kinds.has(raw.kind as string) || !statuses.has(raw.status as string)
            || !time(raw.startedAtUtc) || raw.finishedAtUtc !== null && !time(raw.finishedAtUtc)
            || !text(raw.name) || !text(raw.description) || raw.recordCount !== null && !integer(raw.recordCount)
            || typeof raw.hasDetails !== 'boolean')
            throw invalid();
        return { id: raw.id, kind: raw.kind, startedAtUtc: raw.startedAtUtc, finishedAtUtc: raw.finishedAtUtc,
            name: raw.name, status: raw.status, recordCount: raw.recordCount, description: raw.description, hasDetails: raw.hasDetails } as ExchangeItem;
    });
    if (items.length > Math.max(0, (value.total as number) - skip) || !value.coverageComplete && value.warnings.length === 0)
        throw invalid();
    let fact: ExchangeFact | null = null;
    if (value.fact !== null) {
        const f = value.fact;
        if (!record(f))
            throw invalid();
        for (const key of ['capturedAtUtc', 'checkedAtUtc'])
            if (f[key] !== null && !time(f[key]))
                throw invalid();
        for (const key of ['cutoffExclusiveLocal', 'latestBusinessDate'])
            if (f[key] !== null && !day(f[key]))
                throw invalid();
        for (const key of ['receiptsAmountKzt', 'paymentsAmountKzt', 'netCashFlowAmountKzt'])
            if (f[key] !== null && !money(f[key]))
                throw invalid();
        if (f.sourceLegs !== null && !integer(f.sourceLegs))
            throw invalid();
        fact = { capturedAtUtc: f.capturedAtUtc, checkedAtUtc: f.checkedAtUtc, cutoffExclusiveLocal: f.cutoffExclusiveLocal,
            latestBusinessDate: f.latestBusinessDate, sourceLegs: f.sourceLegs, receiptsAmountKzt: f.receiptsAmountKzt,
            paymentsAmountKzt: f.paymentsAmountKzt, netCashFlowAmountKzt: f.netCashFlowAmountKzt } as ExchangeFact;
    }
    return { items, skip, take, total: value.total as number, coverageComplete: value.coverageComplete,
        warnings: value.warnings as string[], fact };
}
export function publicExchangeDetail(value: unknown, skip: number, take: number): ExchangeDetail {
    if (!record(value))
        throw invalid();
    page(value, skip, take);
    if (!text(value.title) || !text(value.information) || !Array.isArray(value.columns) || value.columns.length < 1
        || value.columns.length > 12 || !value.columns.every(text) || !Array.isArray(value.rows) || value.rows.length > take
        || value.rows.some(row => !Array.isArray(row) || row.length !== (value.columns as unknown[]).length || !row.every(text))
        || !Array.isArray(value.totals) || value.totals.length > 5)
        throw invalid();
    const totals = value.totals.map(row => {
        if (!record(row) || !text(row.label) || !money(row.amountKzt))
            throw invalid();
        return { label: row.label, amountKzt: row.amountKzt };
    });
    if (value.rows.length > Math.max(0, (value.total as number) - skip))
        throw invalid();
    return { title: value.title, information: value.information, columns: value.columns as string[],
        rows: value.rows as string[][], skip, take, total: value.total as number, totals };
}
async function read(path: string, signal: AbortSignal) {
    const response = await fetchWithSafeTransport(apiUrl(path), { ...secureReadInit(), signal });
    if (!response.ok)
        throw new Error(response.status === 403 ? 'Для журнала обмена нужны назначение администратора и активная финансовая роль с полным чтением.'
            : response.status === 401 ? 'Необходимо войти в BMS.' : response.status === 404 ? 'Запись журнала не найдена.' : 'Журнал обмена временно недоступен. Повторите загрузку.');
    return response.json() as Promise<unknown>;
}
export async function getExchangeJournal(filter: ExchangeFilter, skip: number, take: number, signal: AbortSignal): Promise<ExchangePage> {
    if (!integer(skip) || skip > 10000 || ![50, 100].includes(take) || !['all', ...kinds].includes(filter.kind)
        || !['all', ...statuses].includes(filter.status))
        throw invalid();
    const query = new URLSearchParams({ skip: String(skip), take: String(take), kind: filter.kind, status: filter.status });
    if (filter.fromUtc)
        query.set('fromUtc', filter.fromUtc);
    if (filter.toUtc)
        query.set('toUtc', filter.toUtc);
    return publicExchangePage(await read('/api/v1/administration/exchange-journal?' + query, signal), skip, take);
}
export async function getExchangeDetail(item: ExchangeItem, includeExcluded: boolean, skip: number, take: number, signal: AbortSignal): Promise<ExchangeDetail> {
    if (!uuid(item.id) || !kinds.has(item.kind) || !integer(skip) || skip > 1000000 || ![50, 100].includes(take))
        throw invalid();
    const query = new URLSearchParams({ includeExcluded: String(includeExcluded), skip: String(skip), take: String(take) });
    return publicExchangeDetail(await read(`/api/v1/administration/exchange-journal/${item.kind}/${item.id}?${query}`, signal), skip, take);
}
