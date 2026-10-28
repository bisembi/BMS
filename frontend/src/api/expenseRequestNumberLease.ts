import { apiUrl, fetchWithOneTimeRefresh, fetchWithSafeTransport, secureWriteInit } from './http';
import { getCurrentSession } from './session';
import { responseHasInvalidExpenseCsrfToken } from './expenseRequestErrors';
export type ExpenseRequestNumberLease = {
    leaseId: string;
    costCenterId: string;
    number: string;
    expiresAtUtc: string;
};
const invalidLeaseMessage = 'Некорректный ответ резервирования номера.';
export function publicExpenseRequestNumberLease(value: unknown, costCenterId: string, leaseId: string): ExpenseRequestNumberLease {
    const fail = (): never => { throw new Error(invalidLeaseMessage); };
    if (value === null || typeof value !== 'object' || Array.isArray(value))
        return fail();
    const row = value as Record<string, unknown>;
    const uuid = (id: unknown): id is string => typeof id === 'string'
        && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/iu.test(id)
        && id !== '00000000-0000-0000-0000-000000000000';
    if (!uuid(row.leaseId) || !uuid(row.costCenterId)
        || row.leaseId.toLowerCase() !== leaseId.toLowerCase() || row.costCenterId.toLowerCase() !== costCenterId.toLowerCase()
        || typeof row.number !== 'string' || !/^[\p{L}][\p{L}\p{N}]*(?:-[\p{L}\p{N}]+)*-\d{5,}$/u.test(row.number)
        || typeof row.expiresAtUtc !== 'string'
        || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/u.test(row.expiresAtUtc)
        || !Number.isFinite(Date.parse(row.expiresAtUtc))
        || new Date(row.expiresAtUtc).toISOString().slice(0, 19) !== row.expiresAtUtc.slice(0, 19))
        return fail();
    return { leaseId: row.leaseId, costCenterId: row.costCenterId, number: row.number, expiresAtUtc: row.expiresAtUtc };
}
const demoLeases = new Map<string, ExpenseRequestNumberLease & {
    consumed: boolean;
}>();
export function consumeDemoNumberLease(id: string): string {
    const lease = demoLeases.get(id);
    if (!lease || lease.consumed)
        throw new Error('Демонстрационный номер недоступен.');
    lease.consumed = true;
    return lease.number;
}
export async function changeExpenseRequestNumberLease(costCenterId: string, leaseId: string, release = false) {
    const response = await fetchWithOneTimeRefresh(() => fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/number-reservation${release ? '/release' : ''}`), { ...secureWriteInit('POST', { 'Content-Type': 'application/json' }, JSON.stringify({ costCenterId, leaseId })), keepalive: release }), responseHasInvalidExpenseCsrfToken, getCurrentSession);
    if (!response.ok)
        throw new Error('Не удалось закрепить номер заявки. Проверьте доступ к ЦФО и повторите попытку.');
    if (release)
        return null;
    try {
        return publicExpenseRequestNumberLease(await response.json(), costCenterId, leaseId);
    }
    catch {
        throw new Error(invalidLeaseMessage);
    }
}
