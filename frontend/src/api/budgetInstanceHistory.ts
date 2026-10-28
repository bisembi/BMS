import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { isPublicExpenseRequestUuid, isPublicExpenseRequestTimestamp } from './expenseRequestPublicResponse';
export type BudgetHistoryEvent = {
    id: string;
    occurredAtUtc: string;
    eventCode: string;
    title: string;
    actorName: string;
    actorKey?: string | null;
    roleName: string | null;
    comment: string | null;
    detailsAvailable: boolean;
};
export type BudgetHistoryPage = {
    budgetInstanceId: string;
    items: BudgetHistoryEvent[];
    skip: number;
    take: number;
    total: number;
};
const titles: Record<string, string> = { created: 'Создан черновик', saved: 'Сохранены изменения черновика',
    submitted: 'Отправлен на согласование', agreed: 'Согласовано', returned: 'Отправлен на доработку',
    rejected: 'Отклонён', approved: 'Утверждён', other: 'Действие с документом' };
const roles = ['Владелец ЦФО', 'Финансовый контролёр', 'Финансовый директор', 'Генеральный директор'];
const failure = 'Не удалось загрузить историю согласований. Повторите загрузку.';
export class BudgetHistoryError extends Error {
    constructor(inaccessible = false) { super(inaccessible ? 'Бюджет не найден или история недоступна для вашего ЦФО.' : failure); }
}
function record(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function text(value: unknown, max: number, empty = false): value is string {
    return typeof value === 'string' && value.length <= max && (empty || value.trim().length > 0)
        && !/[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]/u.test(value);
}
function validRequest(id: string, skip: number, take: number) {
    return isPublicExpenseRequestUuid(id) && Number.isSafeInteger(skip) && skip >= 0
        && Number.isSafeInteger(take) && take >= 1 && take <= 50;
}
export function publicBudgetHistory(value: unknown, id: string, skip: number, take: number): BudgetHistoryPage {
    const invalid = () => { throw new BudgetHistoryError(); };
    if (!validRequest(id, skip, take) || !record(value) || value.budgetInstanceId !== id || value.skip !== skip || value.take !== take
        || !Number.isSafeInteger(value.total) || Number(value.total) < 0 || !Array.isArray(value.items)
        || value.items.length !== Math.min(take, Math.max(0, Number(value.total) - skip)))
        return invalid();
    const ids = new Set<string>();
    let previousTime = -Infinity;
    for (const item of value.items) {
        if (!record(item) || !isPublicExpenseRequestUuid(item.id) || ids.has(item.id.toLowerCase())
            || !isPublicExpenseRequestTimestamp(item.occurredAtUtc)
            || !/(?:Z|\+00:00)$/u.test(item.occurredAtUtc)
            || Date.parse(item.occurredAtUtc) < previousTime || typeof item.eventCode !== 'string'
            || !Object.hasOwn(titles, item.eventCode) || item.title !== titles[item.eventCode]
            || !text(item.actorName, 500)
            || (item.actorKey !== undefined && item.actorKey !== null && (typeof item.actorKey !== 'string' || !/^[0-9a-f]{64}$/u.test(item.actorKey)))
            || (item.roleName !== null && (typeof item.roleName !== 'string' || !roles.includes(item.roleName)))
            || (item.comment !== null && !text(item.comment, 2000, true)) || typeof item.detailsAvailable !== 'boolean'
            || (!item.detailsAvailable && (item.roleName !== null || item.comment !== null))
            || (['created', 'saved', 'submitted', 'other'].includes(item.eventCode) && (item.roleName !== null || item.comment !== null)))
            return invalid();
        ids.add(item.id.toLowerCase());
        previousTime = Date.parse(item.occurredAtUtc);
    }
    return value as BudgetHistoryPage;
}
export async function getBudgetHistory(id: string, skip: number, take: number, signal?: AbortSignal) {
    if (!validRequest(id, skip, take))
        throw new BudgetHistoryError();
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/budget/instances/' + encodeURIComponent(id)
        + '/history?skip=' + skip + '&take=' + take), { ...secureReadInit(), signal });
    if (response.status !== 200)
        throw new BudgetHistoryError(response.status === 404 || response.status === 403);
    try {
        return publicBudgetHistory(await response.json() as unknown, id, skip, take);
    }
    catch {
        throw new BudgetHistoryError();
    }
}
