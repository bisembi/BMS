import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
export type BudgetRegisterRow343 = {
    sequence: number;
    postedAtUtc: string;
    budgetMonth: string;
    costCenter: string;
    article: string;
    requestId: string | null;
    requestNumber: string | null;
    event: string;
    limitDeltaKzt: number;
    reservedDeltaKzt: number;
    paidDeltaKzt: number;
    amountKzt: number;
    balanceAfterKzt: number;
    sourceVersion: string;
    evidence: string;
};
export type BudgetRegisterPage343 = {
    items: BudgetRegisterRow343[];
    skip: number;
    take: number;
    total: number;
    revision: number;
    cashCapturedAtUtc: string | null;
    totals: {
        limitKzt: number;
        reserveKzt: number;
        paidKzt: number;
        balanceKzt: number;
    };
};
export type BudgetRegisterFilters343 = {
    month: string;
    costCenterId?: string;
    articleId?: string;
};
export const budgetRegisterPageSizes350 = [100, 500, 1000] as const;
export type BudgetRegisterPageSize350 = typeof budgetRegisterPageSizes350[number];
function money(value: unknown): value is number {
    return typeof value === 'number' && Number.isFinite(value) && Math.abs(value) <= 90071992547409.9
        && /^-?\d+(?:\.\d{1,2})?$/.test(String(value));
}
export function publicBudgetRegister343(value: unknown, skip: number, take: number): BudgetRegisterPage343 {
    const invalid = () => new Error('Некорректный ответ регистра исполнения бюджета.');
    if (typeof value !== 'object' || value === null)
        throw invalid();
    const page = value as BudgetRegisterPage343;
    if (!Array.isArray(page.items) || page.items.length > take || page.skip !== skip || page.take !== take
        || !Number.isSafeInteger(page.total) || page.total < skip + page.items.length && page.items.length > 0
        || page.total < 0 || !Number.isSafeInteger(page.revision) || page.revision < 0 || !page.totals
        || page.cashCapturedAtUtc !== null && !Number.isFinite(Date.parse(page.cashCapturedAtUtc)))
        throw invalid();
    for (const amount of [page.totals.limitKzt, page.totals.reserveKzt, page.totals.paidKzt, page.totals.balanceKzt])
        if (!money(amount))
            throw invalid();
    if (Math.abs(page.totals.limitKzt - page.totals.reserveKzt - page.totals.paidKzt - page.totals.balanceKzt) > 0.005)
        throw invalid();
    const seen = new Set<number>();
    const items = page.items.map(row => {
        if (!row || !Number.isSafeInteger(row.sequence) || row.sequence < 1 || seen.has(row.sequence)
            || !Number.isFinite(Date.parse(row.postedAtUtc)) || !/^\d{4}-\d{2}-01$/.test(row.budgetMonth)
            || row.requestId !== null && !/^[a-f0-9-]{36}$/i.test(row.requestId))
            throw invalid();
        seen.add(row.sequence);
        for (const text of [row.costCenter, row.article, row.event, row.sourceVersion, row.evidence])
            if (typeof text !== 'string' || text.length > 8192)
                throw invalid();
        if (row.requestNumber !== null && (typeof row.requestNumber !== 'string' || row.requestNumber.length > 100)
            || row.requestId !== null && !row.requestNumber?.trim())
            throw invalid();
        for (const amount of [row.limitDeltaKzt, row.reservedDeltaKzt, row.paidDeltaKzt, row.amountKzt, row.balanceAfterKzt])
            if (!money(amount))
                throw invalid();
        if (Math.abs(row.limitDeltaKzt - row.reservedDeltaKzt - row.paidDeltaKzt - row.amountKzt) > 0.005)
            throw invalid();
        return { sequence: row.sequence, postedAtUtc: row.postedAtUtc, budgetMonth: row.budgetMonth, costCenter: row.costCenter,
            article: row.article, requestId: row.requestId, requestNumber: row.requestNumber, event: row.event,
            limitDeltaKzt: row.limitDeltaKzt, reservedDeltaKzt: row.reservedDeltaKzt, paidDeltaKzt: row.paidDeltaKzt,
            amountKzt: row.amountKzt, balanceAfterKzt: row.balanceAfterKzt, sourceVersion: row.sourceVersion, evidence: row.evidence };
    });
    return { items, skip, take, total: page.total, revision: page.revision, cashCapturedAtUtc: page.cashCapturedAtUtc,
        totals: { limitKzt: page.totals.limitKzt, reserveKzt: page.totals.reserveKzt, paidKzt: page.totals.paidKzt, balanceKzt: page.totals.balanceKzt } };
}
export async function getBudgetRegister343(filters: BudgetRegisterFilters343, skip: number, signal?: AbortSignal, take: BudgetRegisterPageSize350 = 100): Promise<BudgetRegisterPage343> {
    if (!Number.isSafeInteger(skip) || skip < 0 || !budgetRegisterPageSizes350.includes(take))
        throw new Error('Некорректная страница регистра.');
    const query = new URLSearchParams({ month: `${filters.month}-01`, skip: String(skip), take: String(take) });
    if (filters.costCenterId)
        query.set('costCenterId', filters.costCenterId);
    if (filters.articleId)
        query.set('articleId', filters.articleId);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/administration/registers/budget-execution?${query}`), { ...secureReadInit(), signal });
    if (!response.ok)
        throw new Error('Регистр исполнения бюджета недоступен.');
    return publicBudgetRegister343(await response.json(), skip, take);
}
