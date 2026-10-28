import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { isPublicExpenseRequestUuid as uuid } from './expenseRequestPublicResponse';
export type UnbudgetedQuery = {
    fromMonth: string;
    toMonth: string;
    costCenterId?: string;
    groupId?: string;
    versionId?: string;
    skip: number;
    take: number;
};
export type UnbudgetedArticle = {
    id: string;
    name: string;
    activityType: 'Operating' | 'Investing' | 'Financing';
    costCenterId: string;
    costCenterCode: string;
    costCenterName: string;
    groupId: string | null;
    groupName: string;
    planAmountKzt: 0;
    displayOrder: number | null;
};
export type UnbudgetedReport = {
    versionId: string;
    versionName: string;
    fromMonth: string;
    toMonth: string;
    scopeLimited: boolean;
    skip: number;
    take: number;
    total: number;
    costCenters: {
        id: string;
        code: string;
        name: string;
    }[];
    groups: {
        id: string;
        name: string;
    }[];
    items: UnbudgetedArticle[];
};
const invalid = 'Некорректный ответ отчёта незабюджетированных статей.';
const object = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const text = (v: unknown, max: number): v is string => typeof v === 'string' && v.trim() === v && v.length > 0
    && v.length <= max && !/[\u0000-\u001f\u007f-\u009f]/u.test(v);
const count = (v: unknown): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0;
const month = (v: string) => /^(20\d{2}|2100)-(0[1-9]|1[0-2])$/u.test(v);
const same = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();
export function publicUnbudgetedReport(v: unknown, q: UnbudgetedQuery): UnbudgetedReport {
    if (!object(v) || !uuid(v.versionId) || !text(v.versionName, 200)
        || v.fromMonth !== q.fromMonth + '-01' || v.toMonth !== q.toMonth + '-01'
        || v.skip !== q.skip || v.take !== q.take || !count(v.total) || v.total > 10000
        || typeof v.scopeLimited !== 'boolean' || !Array.isArray(v.items)
        || v.items.length !== Math.min(q.take, Math.max(0, v.total - q.skip))
        || !Array.isArray(v.costCenters) || v.costCenters.length > 10000
        || !Array.isArray(v.groups) || v.groups.length > 10000
        || q.versionId && !same(v.versionId, q.versionId))
        throw new Error(invalid);
    const centers = v.costCenters.map(c => {
        if (!object(c) || !uuid(c.id) || !text(c.code, 100) || !text(c.name, 500))
            throw new Error(invalid);
        return { id: c.id, code: c.code, name: c.name };
    });
    const groups = v.groups.map(g => {
        if (!object(g) || !uuid(g.id) || !text(g.name, 150))
            throw new Error(invalid);
        return { id: g.id, name: g.name };
    });
    const centerMap = new Map(centers.map(c => [c.id.toLowerCase(), c]));
    const groupMap = new Map(groups.map(g => [g.id.toLowerCase(), g]));
    if (centerMap.size !== centers.length || groupMap.size !== groups.length
        || q.costCenterId && !centerMap.has(q.costCenterId.toLowerCase()))
        throw new Error(invalid);
    const items = v.items.map(a => {
        if (!object(a) || !uuid(a.id) || !uuid(a.costCenterId) || !text(a.name, 150)
            || !['Operating', 'Investing', 'Financing'].includes(a.activityType as string)
            || !text(a.costCenterCode, 100) || !text(a.costCenterName, 500)
            || !(a.groupId === null || uuid(a.groupId)) || !text(a.groupName, 150)
            || a.planAmountKzt !== 0 || !(a.displayOrder === null || count(a.displayOrder))
            || q.costCenterId && !same(a.costCenterId, q.costCenterId)
            || q.groupId && (q.groupId === 'none' ? a.groupId !== null
                : typeof a.groupId !== 'string' || !same(a.groupId, q.groupId)))
            throw new Error(invalid);
        const center = centerMap.get(a.costCenterId.toLowerCase());
        if (!center || center.code !== a.costCenterCode || center.name !== a.costCenterName
            || a.groupId !== null && groupMap.get(a.groupId.toLowerCase())?.name !== a.groupName
            || a.groupId === null && a.groupName !== 'Без группы')
            throw new Error(invalid);
        return a as UnbudgetedArticle;
    });
    if (new Set(items.map(a => a.id.toLowerCase())).size !== items.length)
        throw new Error(invalid);
    return { versionId: v.versionId, versionName: v.versionName, fromMonth: v.fromMonth as string,
        toMonth: v.toMonth as string, scopeLimited: v.scopeLimited, skip: q.skip, take: q.take,
        total: v.total, costCenters: centers, groups, items };
}
export async function getUnbudgetedArticles(q: UnbudgetedQuery, signal?: AbortSignal): Promise<UnbudgetedReport> {
    if (!month(q.fromMonth) || !month(q.toMonth) || q.fromMonth > q.toMonth
        || !count(q.skip) || q.skip > 2147483547 || !count(q.take) || q.take < 1 || q.take > 100
        || q.costCenterId && !uuid(q.costCenterId) || q.versionId && !uuid(q.versionId)
        || q.groupId && q.groupId !== 'none' && !uuid(q.groupId))
        throw new Error(invalid);
    const params = new URLSearchParams({ fromMonth: q.fromMonth, toMonth: q.toMonth,
        skip: String(q.skip), take: String(q.take) });
    for (const key of ['costCenterId', 'groupId', 'versionId'] as const)
        if (q[key])
            params.set(key, q[key]!);
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/budget/reports/unbudgeted-articles?' + params), { ...secureReadInit(), signal });
    if (!response.ok) {
        void response.arrayBuffer().catch(() => undefined);
        if (response.status === 409)
            throw new Error('Порядок перекрывающихся черновиков требует подтверждения владельца.');
        throw new Error('Не удалось загрузить незабюджетированные статьи. Повторите попытку.');
    }
    return publicUnbudgetedReport(await response.json(), q);
}
