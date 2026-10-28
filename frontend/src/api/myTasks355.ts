import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { isPublicExpenseRequestText, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
export type MyApprovalTask355 = {
    kind: 'ExpenseRequest' | 'BudgetInstance' | 'BudgetAdjustment';
    id: string;
    number: string;
    title: string;
    costCenterName: string;
    roleCode: string;
    receivedAtUtc: string;
};
export type MyApprovalTaskPage355 = {
    items: MyApprovalTask355[];
    total: number;
    skip: number;
    take: number;
};
const roleCodes = ['BUDGET_OWNER', 'FIN_CONTROLLER', 'FIN_DIRECTOR', 'GENERAL_DIRECTOR', 'NSI_SPECIALIST', 'TREASURER', 'PAYROLL_CALCULATOR'];
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const invalid = () => Error('Не удалось загрузить мои задачи. Повторите загрузку.');
export function publicMyApprovalTaskPage355(value: unknown, skip: number, take: number): MyApprovalTaskPage355 {
    if (!record(value) || value.skip !== skip || value.take !== take || !Number.isSafeInteger(value.total)
        || (value.total as number) < 0 || !Array.isArray(value.items) || value.items.length !== Math.min(take, Math.max(0, (value.total as number) - skip))
        || Object.keys(value).some(key => !['items', 'total', 'skip', 'take'].includes(key)))
        throw invalid();
    const keys = new Set<string>();
    for (const item of value.items) {
        if (!record(item) || !['ExpenseRequest', 'BudgetInstance', 'BudgetAdjustment'].includes(String(item.kind))
            || !isPublicExpenseRequestUuid(item.id) || !isPublicExpenseRequestText(item.number, 150)
            || typeof item.title !== 'string' || (item.title.length > 4000) || /[\u0000-\u0008\u000b\u000c\u000e-\u001f]/.test(item.title)
            || typeof item.costCenterName !== 'string' || item.costCenterName.length > 500
            || !roleCodes.includes(String(item.roleCode))
            || (item.kind === 'BudgetAdjustment' && !['FIN_DIRECTOR', 'GENERAL_DIRECTOR'].includes(String(item.roleCode)))
            || typeof item.receivedAtUtc !== 'string' || !Number.isFinite(Date.parse(item.receivedAtUtc))
            || Object.keys(item).some(key => !['kind', 'id', 'number', 'title', 'costCenterName', 'roleCode', 'receivedAtUtc'].includes(key)))
            throw invalid();
        const key = `${item.kind}:${String(item.id).toLowerCase()}`;
        if (keys.has(key))
            throw invalid();
        keys.add(key);
    }
    return value as MyApprovalTaskPage355;
}
export async function listMyApprovalTasks355(skip = 0, take = 50, signal?: AbortSignal): Promise<MyApprovalTaskPage355> {
    if (!Number.isSafeInteger(skip) || skip < 0 || !Number.isInteger(take) || take < 1 || take > 100)
        throw invalid();
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/my-tasks?skip=${skip}&take=${take}`), { ...secureReadInit(), signal });
    if (response.status !== 200)
        throw invalid();
    return publicMyApprovalTaskPage355(await response.json(), skip, take);
}
