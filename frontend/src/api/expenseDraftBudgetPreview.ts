import { apiUrl, fetchWithSafeTransport, secureWriteInit } from './http';
import { isPublicExpenseRequestUuid as uuid } from './expenseRequestPublicResponse';
export type ExpenseDraftBudgetInput = {
    costCenterId: string;
    plannedPaymentDate: string;
    currencyCode: string;
    lines: {
        cashFlowItemId: string;
        costCenterId: string | null;
        amount: number;
        vatRateCode: string;
    }[];
    expenseRequestId?: string;
    expectedVersion?: string;
    limitsOnly?: boolean;
};
export type ExpenseDraftBudgetPosition = {
    costCenterId: string;
    cashFlowItemId: string;
    budgetMonth: string;
    limitAmountKzt: number;
    otherReservedAmountKzt: number;
    paidAmountKzt: number;
    requestedAmountKzt: number;
    availableAmountKzt: number;
    positiveDeficitKzt: number;
};
const error = 'Не удалось рассчитать остаток бюджета.';
const record = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v);
const cents = (v: unknown): number | null => {
    if (typeof v !== 'number' || !Number.isFinite(v) || Number(v.toFixed(2)) !== v)
        return null;
    const text = v.toFixed(2), sign = v < 0 ? -1 : 1;
    const [whole, fraction] = text.replace('-', '').split('.');
    const value = BigInt(whole) * 100n + BigInt(fraction);
    return value <= BigInt(Number.MAX_SAFE_INTEGER) ? sign * Number(value) : null;
};
export function publicExpenseDraftBudgetPreview(value: unknown, input: ExpenseDraftBudgetInput): ExpenseDraftBudgetPosition[] {
    if (!record(value) || !Array.isArray(value.positions))
        throw new Error(error);
    const ids = new Set(input.lines.map(line => line.cashFlowItemId.toLowerCase()));
    const requested = new Map<string, bigint>();
    if (input.currencyCode === 'KZT')
        for (const line of input.lines) {
            const amount = cents(line.amount);
            if (amount === null)
                throw new Error(error);
            const id = line.cashFlowItemId.toLowerCase();
            requested.set(id, (requested.get(id) ?? 0n) + BigInt(amount));
        }
    const seen = new Set<string>();
    const rows = value.positions.map((item: unknown) => {
        if (!record(item) || !uuid(item.costCenterId) || !uuid(item.cashFlowItemId)
            || item.costCenterId.toLowerCase() !== input.costCenterId.toLowerCase()
            || !ids.has(item.cashFlowItemId.toLowerCase()) || item.budgetMonth !== `${input.plannedPaymentDate.slice(0, 7)}-01`
            || seen.has(item.cashFlowItemId.toLowerCase()))
            throw new Error(error);
        seen.add(item.cashFlowItemId.toLowerCase());
        const names = ['limitAmountKzt', 'otherReservedAmountKzt', 'paidAmountKzt', 'requestedAmountKzt', 'availableAmountKzt', 'positiveDeficitKzt'] as const;
        const amounts = names.map(name => cents(item[name]));
        if (amounts.some(amount => amount === null) || amounts.filter((_, i) => i !== 4).some(amount => amount! < 0)
            || BigInt(amounts[0]!) - BigInt(amounts[1]!) - BigInt(amounts[2]!) - BigInt(amounts[3]!) !== BigInt(amounts[4]!)
            || Math.max(0, -amounts[4]!) !== amounts[5])
            throw new Error(error);
        // For KZT the response must account for every selected operation, not merely one row.
        if (input.currencyCode === 'KZT' && requested.get(item.cashFlowItemId.toLowerCase()) !== BigInt(amounts[3]!))
            throw new Error(error);
        return item as ExpenseDraftBudgetPosition;
    });
    if (seen.size !== ids.size)
        throw new Error(error);
    return rows;
}
export async function previewExpenseDraftBudget(input: ExpenseDraftBudgetInput, signal: AbortSignal): Promise<ExpenseDraftBudgetPosition[]> {
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/budget/expense-draft/preview'), { ...secureWriteInit('POST', { 'Content-Type': 'application/json' }, JSON.stringify(input)), signal });
    if (!response.ok)
        throw new Error(error);
    return publicExpenseDraftBudgetPreview(await response.json(), input);
}
export async function previewExpenseBudgetLimits(input: ExpenseDraftBudgetInput, signal: AbortSignal): Promise<ExpenseDraftBudgetPosition[]> {
    if (!input.limitsOnly || input.expenseRequestId || input.expectedVersion || input.lines.some(line => line.amount !== 0))
        throw new Error(error);
    return previewExpenseDraftBudget(input, signal);
}
