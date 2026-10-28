import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { isPublicExpenseRequestMoney as money, publicExpenseRequestMoneyCents as rawCents, isPublicExpenseRequestUuid as uuid } from './expenseRequestPublicResponse';
export type OpiuArticle = {
    id: string;
    name: string;
    months: number[];
    total: number;
};
export type OpiuDepartment = OpiuArticle & {
    articles: OpiuArticle[];
};
export type OpiuReport = {
    capturedAtUtc: string;
    months: string[];
    departments: OpiuDepartment[];
    totals: number[];
    total: number;
    lineCount: number;
    basis: string;
};
export type OpiuDetail = {
    expenseRequestId: string;
    lineNumber: number;
    accrualPeriod: string;
    organization: string;
    counterparty: string | null;
    purpose: string | null;
    remainingNetAmountKzt: number;
};
export type OpiuDetails = {
    items: OpiuDetail[];
    skip: number;
    take: number;
    total: number;
    totalNetAmountKzt: number;
    capturedAtUtc: string;
};
export type OpiuQuery = {
    from: string;
    through: string;
    organizationId?: string;
    costCenterId?: string;
    expenseItemId?: string;
    skip?: number;
    take?: number;
};
const errorMessage = 'Не удалось загрузить ОПиУ. Данные не заменены нулями. Попробуйте обновить отчёт.';
const record = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const text = (v: unknown): v is string => typeof v === 'string' && v.length > 0 && v.length <= 500 && !/[\u0000-\u001f]/u.test(v);
const month = (v: unknown): v is string => typeof v === 'string' && /^(20\d\d)-(0[1-9]|1[0-2])-01$/u.test(v);
const cents = (v: number) => {
    const n = rawCents(v);
    if (n === null)
        throw Error(errorMessage);
    return BigInt(n);
};
const sum = (v: number[]) => v.reduce((s, n) => s + cents(n), 0n);
export function publicOpiu(v: unknown): OpiuReport {
    if (!record(v) || !Array.isArray(v.months) || v.months.length < 1 || v.months.length > 24
        || !v.months.every(month) || v.basis !== 'currentUnclosedAccrualsExcludingVat'
        || !Array.isArray(v.departments) || v.departments.length > 3000
        || !Number.isInteger(v.lineCount) || (v.lineCount as number) < 0 || (v.lineCount as number) > 20000
        || !text(v.capturedAtUtc) || Number.isNaN(Date.parse(v.capturedAtUtc)))
        throw Error(errorMessage);
    const months = v.months as string[];
    for (let i = 1; i < months.length; i++) {
        const next = new Date(`${months[i - 1]}T00:00:00Z`);
        next.setUTCMonth(next.getUTCMonth() + 1);
        if (next.toISOString().slice(0, 10) !== months[i])
            throw Error(errorMessage);
    }
    const validAmounts = (r: Record<string, unknown>) => Array.isArray(r.months)
        && r.months.length === months.length && r.months.every(n => money(n) && (n as number) >= 0)
        && money(r.total) && cents(r.total) === sum(r.months as number[]);
    const ids = new Set<string>();
    let count = 0;
    for (const d of v.departments) {
        if (!record(d) || !uuid(d.id) || ids.has(d.id) || !text(d.name) || !validAmounts(d)
            || !Array.isArray(d.articles))
            throw Error(errorMessage);
        ids.add(d.id);
        const articleIds = new Set<string>();
        for (const a of d.articles) {
            if (++count > 3000 || !record(a) || !uuid(a.id) || articleIds.has(a.id)
                || !text(a.name) || !validAmounts(a))
                throw Error(errorMessage);
            articleIds.add(a.id);
        }
        for (let i = 0; i < months.length; i++) {
            if (cents((d.months as number[])[i]!) !== sum(d.articles.map(a => a.months[i])))
                throw Error(errorMessage);
        }
    }
    if (!Array.isArray(v.totals) || v.totals.length !== months.length || !v.totals.every(n => money(n))
        || !money(v.total) || cents(v.total) !== sum(v.totals as number[]))
        throw Error(errorMessage);
    for (let i = 0; i < months.length; i++) {
        if (cents(v.totals[i] as number) !== sum(v.departments.map(d => d.months[i])))
            throw Error(errorMessage);
    }
    return v as unknown as OpiuReport;
}
export async function getOpiu(query: OpiuQuery, signal: AbortSignal): Promise<OpiuReport> {
    const report = publicOpiu(await load(query, false, signal));
    if (report.months[0] !== query.from || report.months.at(-1) !== query.through)
        throw Error(errorMessage);
    return report;
}
export async function getOpiuDetails(query: OpiuQuery, signal: AbortSignal): Promise<OpiuDetails> {
    const v = await load(query, true, signal);
    if (!record(v) || !Array.isArray(v.items) || v.items.length > (query.take ?? 50)
        || v.skip !== (query.skip ?? 0) || v.take !== (query.take ?? 50) || !Number.isInteger(v.total)
        || (v.total as number) < v.items.length + (v.skip as number) || !money(v.totalNetAmountKzt)
        || !text(v.capturedAtUtc) || Number.isNaN(Date.parse(v.capturedAtUtc)))
        throw Error(errorMessage);
    const ids = new Set<string>();
    for (const item of v.items) {
        if (!record(item) || !uuid(item.expenseRequestId) || !Number.isInteger(item.lineNumber)
            || (item.lineNumber as number) < 1 || !month(item.accrualPeriod) || !text(item.organization)
            || !(item.counterparty === null || text(item.counterparty))
            || !(item.purpose === null || (typeof item.purpose === 'string' && item.purpose.length <= 1000))
            || !money(item.remainingNetAmountKzt) || (item.remainingNetAmountKzt as number) < 0)
            throw Error(errorMessage);
        const key = `${item.expenseRequestId}:${item.lineNumber}`;
        if (ids.has(key))
            throw Error(errorMessage);
        ids.add(key);
    }
    if (v.skip === 0 && v.items.length === v.total && cents(v.totalNetAmountKzt as number)
        !== sum(v.items.map(i => i.remainingNetAmountKzt)))
        throw Error(errorMessage);
    return v as unknown as OpiuDetails;
}
async function load(query: OpiuQuery, details: boolean, signal: AbortSignal): Promise<unknown> {
    if (!month(query.from) || !month(query.through))
        throw Error('Укажите период начисления.');
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(query))
        if (value !== undefined && value !== '')
            params.set(key, String(value));
    const r = await fetchWithSafeTransport(apiUrl(`/api/v1/management/accruals/opiu${details ? '/details' : ''}?${params}`), { ...secureReadInit(), signal });
    if (r.status === 422)
        throw Error('Слишком большая область ОПиУ. Сузьте период или подразделение.');
    if (!r.ok)
        throw Error(errorMessage);
    return r.json();
}
