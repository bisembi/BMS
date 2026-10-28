import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { isPublicExpenseRequestText, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
export type ExpenseRequestInitiatorOption = {
    id: string;
    name: string;
};
const unavailable = 'Не удалось загрузить список сотрудников.';
export function publicExpenseRequestInitiators(payload: unknown): ExpenseRequestInitiatorOption[] {
    if (!Array.isArray(payload))
        throw new Error(unavailable);
    const ids = new Set<string>();
    return payload.map((value: unknown) => {
        if (value === null || typeof value !== 'object' || Array.isArray(value))
            throw new Error(unavailable);
        const row = value as Record<string, unknown>;
        if (Object.keys(row).sort().join('|') !== 'id|name' || !isPublicExpenseRequestUuid(row.id)
            || !isPublicExpenseRequestText(row.name, 150) || ids.has(row.id.toLowerCase()))
            throw new Error(unavailable);
        ids.add(row.id.toLowerCase());
        return { id: row.id, name: row.name };
    });
}
export async function listExpenseRequestInitiators(signal?: AbortSignal): Promise<ExpenseRequestInitiatorOption[]> {
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/expense-requests/initiators'), { ...secureReadInit(), signal });
    if (!response.ok)
        throw new Error(unavailable);
    return publicExpenseRequestInitiators(await response.json());
}
