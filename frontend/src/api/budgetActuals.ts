import { apiUrl, fetchWithOneTimeRefresh, fetchWithSafeTransport, secureWriteInit } from './http';
import { getCurrentSession } from './session';
import { isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
import type { BudgetFlowDirection } from './budgetInstances';
export type BudgetActualsInput = {
    costCenterId: string;
    fromMonth: string;
    toMonth: string;
    cashFlowItemIds: readonly string[];
    budgetInstanceId?: string;
    expectedConcurrencyStamp?: string;
};
export type BudgetActualsLine = {
    cashFlowItemId: string;
    budgetMonth: string;
    limitAmountKzt: number;
    flowDirection: BudgetFlowDirection;
};
export type BudgetActualsPreview = {
    snapshotId: string;
    sourceHash: string;
    capturedAtUtc: string;
    costCenterId: string;
    budgetInstanceId: string | null;
    fromMonth: string;
    toMonth: string;
    cashFlowItemIds: string[];
    availableMonths: string[];
    lines: BudgetActualsLine[];
    receiptsAmountKzt: number;
    paymentsAmountKzt: number;
    netCashFlowAmountKzt: number;
};
const codes = new Set(['actuals-source-invalid', 'actuals-not-loaded', 'actuals-period-unavailable',
    'actuals-mapping-unavailable', 'version-conflict', 'csrf-token-invalid']);
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const month = (value: unknown): value is string => typeof value === 'string' && /^\d{4}-(0[1-9]|1[0-2])-01$/u.test(value)
    && Number(value.slice(0, 4)) >= 2026 && Number(value.slice(0, 4)) <= 2100;
const same = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();
// Exact decimal roundtrip, not a fixed floating-point epsilon on value * 100.
// Large valid sums from ODDS retain their kopecks without accepting extra precision.
const signedCents = (value: unknown): number | null => {
    if (typeof value !== 'number' || !Number.isFinite(value) || Math.abs(value) > 90071992547409.91)
        return null;
    const text = value.toFixed(2);
    if (Number(text) !== value)
        return null;
    const negative = text.startsWith('-');
    const [whole, fraction] = (negative ? text.slice(1) : text).split('.');
    const exact = BigInt(whole) * 100n + BigInt(fraction);
    if (exact > BigInt(Number.MAX_SAFE_INTEGER))
        return null;
    return Number(negative ? -exact : exact);
};
const cents = (value: unknown): number | null => typeof value === 'number' && value >= 0 ? signedCents(value) : null;
const invalid = 'Не удалось получить корректный факт. Ввод не изменён.';
export class BudgetActualsApiError extends Error {
    constructor(readonly status: number, readonly code: string | null = null) { super('Budget actuals request failed.'); }
}
export function budgetActualsErrorMessage(cause: unknown): string {
    if (!(cause instanceof BudgetActualsApiError))
        return 'Не удалось скопировать факт. Проверьте параметры и повторите попытку.';
    if (cause.code === 'actuals-not-loaded')
        return 'Факт ещё не загружен в SQL на сервере 143.';
    if (cause.code === 'actuals-period-unavailable')
        return 'В SQL нет факта за весь выбранный период. Выберите только доступные месяцы.';
    if (cause.code === 'actuals-mapping-unavailable')
        return 'Для выбранной статьи нет однозначного соответствия в источнике ОДДС. Копирование не выполнено.';
    if (cause.code === 'version-conflict')
        return 'Черновик изменился. Загрузите текущее состояние перед копированием.';
    if (cause.code === 'csrf-token-invalid')
        return 'Обновите страницу: сеанс безопасности устарел.';
    if (cause.status === 403)
        return 'Нет права редактирования этого экземпляра бюджета.';
    if (cause.status === 401)
        return 'Для копирования факта требуется авторизация.';
    if (cause.status === 400)
        return 'Проверьте статьи и период выбранного года.';
    return 'Не удалось скопировать факт. Ввод не изменён.';
}
function selectedMonths(input: BudgetActualsInput): string[] {
    if (!month(input.fromMonth) || !month(input.toMonth) || input.fromMonth.slice(0, 4) !== input.toMonth.slice(0, 4) || input.fromMonth > input.toMonth)
        throw new Error(invalid);
    const from = Number(input.fromMonth.slice(5, 7));
    const to = Number(input.toMonth.slice(5, 7));
    return Array.from({ length: to - from + 1 }, (_, index) => `${input.fromMonth.slice(0, 4)}-${String(from + index).padStart(2, '0')}-01`);
}
/** Validate the complete selected Cartesian slice before any form value is replaced. */
export function publicBudgetActualsPreview(value: unknown, input: BudgetActualsInput): BudgetActualsPreview {
    const months = selectedMonths(input);
    const ids = new Set(input.cashFlowItemIds.map(id => id.toLowerCase()));
    if (!record(value) || !isPublicExpenseRequestUuid(value.snapshotId)
        || typeof value.sourceHash !== 'string' || !/^[0-9A-F]{64}$/u.test(value.sourceHash)
        || typeof value.capturedAtUtc !== 'string' || value.capturedAtUtc.length > 64 || Number.isNaN(Date.parse(value.capturedAtUtc))
        || !isPublicExpenseRequestUuid(value.costCenterId) || !same(value.costCenterId, input.costCenterId)
        || (input.budgetInstanceId ? typeof value.budgetInstanceId !== 'string' || !same(value.budgetInstanceId, input.budgetInstanceId) : value.budgetInstanceId !== null)
        || value.fromMonth !== input.fromMonth || value.toMonth !== input.toMonth
        || !Array.isArray(value.cashFlowItemIds) || value.cashFlowItemIds.length !== ids.size
        || !value.cashFlowItemIds.every(id => isPublicExpenseRequestUuid(id) && ids.has(id.toLowerCase()))
        || new Set(value.cashFlowItemIds.map(id => (id as string).toLowerCase())).size !== ids.size
        || !Array.isArray(value.availableMonths) || value.availableMonths.length < 1 || value.availableMonths.length > 12
        || !value.availableMonths.every(month) || new Set(value.availableMonths).size !== value.availableMonths.length
        || !months.every(month => (value.availableMonths as string[]).includes(month))
        || !Array.isArray(value.lines) || value.lines.length !== ids.size * months.length)
        throw new Error(invalid);
    const keys = new Set<string>();
    let receipts = 0;
    let payments = 0;
    const lines: BudgetActualsLine[] = value.lines.map(line => {
        if (!record(line) || !isPublicExpenseRequestUuid(line.cashFlowItemId) || !ids.has(line.cashFlowItemId.toLowerCase())
            || !month(line.budgetMonth) || !months.includes(line.budgetMonth) || cents(line.limitAmountKzt) === null
            || (line.flowDirection !== 'Receipt' && line.flowDirection !== 'Payment'))
            throw new Error(invalid);
        const key = `${line.cashFlowItemId.toLowerCase()}\u0000${line.budgetMonth}`;
        if (keys.has(key))
            throw new Error(invalid);
        keys.add(key);
        if (line.flowDirection === 'Receipt')
            receipts += cents(line.limitAmountKzt)!;
        else
            payments += cents(line.limitAmountKzt)!;
        return { cashFlowItemId: line.cashFlowItemId, budgetMonth: line.budgetMonth, limitAmountKzt: line.limitAmountKzt as number, flowDirection: line.flowDirection };
    });
    if (!Number.isSafeInteger(receipts) || !Number.isSafeInteger(payments)
        || cents(value.receiptsAmountKzt) !== receipts || cents(value.paymentsAmountKzt) !== payments
        || signedCents(value.netCashFlowAmountKzt) !== receipts - payments)
        throw new Error(invalid);
    return { snapshotId: value.snapshotId, sourceHash: value.sourceHash, capturedAtUtc: value.capturedAtUtc,
        costCenterId: value.costCenterId, budgetInstanceId: value.budgetInstanceId as string | null,
        fromMonth: input.fromMonth, toMonth: input.toMonth, cashFlowItemIds: [...input.cashFlowItemIds],
        availableMonths: value.availableMonths as string[], lines, receiptsAmountKzt: receipts / 100,
        paymentsAmountKzt: payments / 100, netCashFlowAmountKzt: (receipts - payments) / 100 };
}
async function errorCode(response: Response): Promise<string | null> {
    try {
        const body: unknown = await response.clone().json();
        return record(body) && typeof body.code === 'string' && codes.has(body.code) ? body.code : null;
    }
    catch {
        return null;
    }
}
export async function previewBudgetActuals(input: BudgetActualsInput, signal?: AbortSignal): Promise<BudgetActualsPreview> {
    selectedMonths(input);
    if (!isPublicExpenseRequestUuid(input.costCenterId) || input.cashFlowItemIds.length < 1 || input.cashFlowItemIds.length > 2000
        || !input.cashFlowItemIds.every(isPublicExpenseRequestUuid)
        || new Set(input.cashFlowItemIds.map(id => id.toLowerCase())).size !== input.cashFlowItemIds.length
        || Boolean(input.budgetInstanceId) !== Boolean(input.expectedConcurrencyStamp)
        || (input.budgetInstanceId !== undefined && !isPublicExpenseRequestUuid(input.budgetInstanceId))
        || (input.expectedConcurrencyStamp !== undefined && !isPublicExpenseRequestUuid(input.expectedConcurrencyStamp)))
        throw new BudgetActualsApiError(400);
    const captured: BudgetActualsInput = { costCenterId: input.costCenterId, fromMonth: input.fromMonth, toMonth: input.toMonth,
        cashFlowItemIds: [...input.cashFlowItemIds], ...(input.budgetInstanceId ? { budgetInstanceId: input.budgetInstanceId, expectedConcurrencyStamp: input.expectedConcurrencyStamp } : {}) };
    const body = JSON.stringify(captured);
    const response = await fetchWithOneTimeRefresh(() => fetchWithSafeTransport(apiUrl('/api/v1/budget/actuals/preview'), { ...secureWriteInit('POST', { 'Content-Type': 'application/json' }, body), signal }), async (response) => response.status === 403 && await errorCode(response) === 'csrf-token-invalid', getCurrentSession);
    if (response.status !== 200)
        throw new BudgetActualsApiError(response.status, await errorCode(response));
    return publicBudgetActualsPreview(await response.json(), captured);
}
