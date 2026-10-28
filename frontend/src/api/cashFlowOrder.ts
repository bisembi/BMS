import { apiUrl, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
import { isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
import { publicReferenceDataPage } from './referenceData';
import { useMockApi } from './runtime';
import type { ReferenceDataItem } from '../types';
export type CashFlowOrderItem = ReferenceDataItem & {
    groupKey: string;
};
export type CashFlowOrder = {
    version: string;
    canEdit: boolean;
    items: CashFlowOrderItem[];
};
export type CashFlowOrderMove = {
    expectedVersion: string;
    sourceId: string;
    targetId: string;
    placement: 'Before' | 'After';
};
const revision = /^[A-F0-9]{64}$/u;
const invalid = 'Не удалось загрузить корректную структуру статей ДДС.';
const path = '/api/v1/reference-data/cash-flow-items/order';
class OrderApiError extends Error {
    constructor(readonly status: number) { super('Не удалось изменить порядок статей ДДС.'); }
}
export function cashFlowOrderError(error: unknown): string {
    if (!(error instanceof OrderApiError))
        return 'Результат переноса не подтверждён. Повторите ту же попытку.';
    if (error.status === 400)
        return 'Не удалось выполнить перенос: проверьте выбранные статьи и обновите структуру.';
    if (error.status === 401)
        return 'Войдите в BMS для изменения порядка.';
    if (error.status === 403)
        return 'Недостаточно прав для изменения порядка.';
    if (error.status === 409)
        return 'Структура уже изменена другим пользователем. Обновите её перед переносом.';
    return 'Результат переноса не подтверждён. Повторите ту же попытку.';
}
export const isCashFlowOrderUncertain = (error: unknown) => !(error instanceof OrderApiError)
    || ![400, 401, 403, 404, 409].includes(error.status);
export function publicCashFlowOrder(value: unknown): CashFlowOrder {
    if (typeof value !== 'object' || value === null || Array.isArray(value))
        throw new Error(invalid);
    const raw = value as Record<string, unknown>;
    if (typeof raw.version !== 'string' || !revision.test(raw.version) || typeof raw.canEdit !== 'boolean'
        || !Array.isArray(raw.items) || raw.items.length > 10000)
        throw new Error(invalid);
    const parsed = publicReferenceDataPage({ items: raw.items, total: raw.items.length, skip: 0,
        take: Math.max(1, raw.items.length) }, 0, Math.max(1, raw.items.length));
    return { version: raw.version, canEdit: raw.canEdit, items: parsed.items.map((item, i) => {
            const source = raw.items as Record<string, unknown>[];
            const key = source[i].groupKey;
            if (typeof key !== 'string' || key.length > 2000 || /[\u0000-\u001f\u007f-\u009f]/u.test(key)
                || (item.parentId != null ? key.length === 0 : key.length !== 0))
                throw new Error(invalid);
            return { ...item, groupKey: key };
        }) };
}
async function read(response: Response): Promise<CashFlowOrder> {
    if (!response.ok) {
        void response.arrayBuffer().catch(() => undefined);
        throw new OrderApiError(response.status);
    }
    return publicCashFlowOrder(await response.json());
}
export async function getCashFlowOrder(signal?: AbortSignal): Promise<CashFlowOrder> {
    return read(await fetchWithSafeTransport(apiUrl(path), { ...secureReadInit(), signal }));
}
export async function moveCashFlowOrder(body: CashFlowOrderMove, key: string): Promise<CashFlowOrder> {
    if (useMockApi || !revision.test(body.expectedVersion) || !isPublicExpenseRequestUuid(body.sourceId)
        || !isPublicExpenseRequestUuid(body.targetId) || body.sourceId.toLowerCase() === body.targetId.toLowerCase()
        || !['Before', 'After'].includes(body.placement) || !/^[A-Za-z0-9_-]{1,100}$/u.test(key))
        throw new OrderApiError(400);
    return read(await fetchWithSafeTransport(apiUrl(`${path}/move`), {
        ...secureWriteInit('POST', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, JSON.stringify(body)),
    }));
}
