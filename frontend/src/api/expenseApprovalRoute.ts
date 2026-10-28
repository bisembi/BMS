import { apiUrl, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
import { isPublicExpenseRequestUuid as uuid } from './expenseRequestPublicResponse';
import { publicExpenseDraftBudgetPreview, type ExpenseDraftBudgetInput } from './expenseDraftBudgetPreview';
export type ExpenseApprovalRoute = {
    requestId: string;
    requestVersion: string;
    statusCode: string;
    previewOnly: boolean;
    available: boolean;
    currentStageNumber: number;
    stages: {
        stageNumber: number;
        roleCode: string;
        names: string[];
    }[];
    treasurerNames?: string[];
    unavailableReason?: string;
    nsiSpecialistNames?: string[];
};
const roles = new Set(['BUDGET_OWNER', 'FIN_CONTROLLER', 'FIN_DIRECTOR', 'GENERAL_DIRECTOR', 'PAYROLL_CALCULATOR']);
const states = new Set(['DRAFT', 'RETURNED', 'IN_APPROVAL', 'APPROVED', 'TO_PAY', 'AWAITING_NSI', 'PARTIALLY_PAID', 'PAID', 'CLOSED', 'CANCELLED', 'REJECTED']);
const record = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const publicNames = (v: unknown): v is string[] => Array.isArray(v) && v.length <= 1000
    && v.every(name => typeof name === 'string' && !!name.trim() && name.length <= 500 && !/[\u0000-\u001f\u007f]/u.test(name));
export function publicExpenseApprovalRoute(value: unknown): ExpenseApprovalRoute {
    const fail = () => { throw new Error('Не удалось получить маршрут согласования.'); };
    if (!record(value) || !uuid(value.requestId) || !uuid(value.requestVersion) || typeof value.statusCode !== 'string'
        || !states.has(value.statusCode) || typeof value.previewOnly !== 'boolean' || typeof value.available !== 'boolean'
        || !Number.isSafeInteger(value.currentStageNumber) || (value.currentStageNumber as number) < 0
        || !Array.isArray(value.stages) || value.stages.length > 4)
        return fail();
    for (const [i, stage] of value.stages.entries()) {
        if (!record(stage) || stage.stageNumber !== i + 1 || typeof stage.roleCode !== 'string' || !roles.has(stage.roleCode)
            || !publicNames(stage.names) || stage.names.length === 0)
            return fail();
    }
    if (Object.prototype.hasOwnProperty.call(value, 'treasurerNames') && !publicNames(value.treasurerNames))
        return fail();
    if (Object.prototype.hasOwnProperty.call(value, 'nsiSpecialistNames') && !publicNames(value.nsiSpecialistNames))
        return fail();
    if (Object.prototype.hasOwnProperty.call(value, 'unavailableReason') && (typeof value.unavailableReason !== 'string'
        || value.unavailableReason.length > 128 || /[\u0000-\u001f\u007f]/u.test(value.unavailableReason)))
        return fail();
    if ((value.currentStageNumber as number) > value.stages.length || (!value.available && value.stages.length))
        return fail();
    return value as ExpenseApprovalRoute;
}
export async function getExpenseApprovalRoute(input: ExpenseDraftBudgetInput | {
    id: string;
    version: string;
}, signal: AbortSignal): Promise<ExpenseApprovalRoute> {
    const saved = 'id' in input;
    const response = await fetchWithSafeTransport(apiUrl(saved ? `/api/v1/expense-requests/${input.id}/approval-route`
        : '/api/v1/budget/expense-draft/preview'), saved ? { ...secureReadInit(), signal }
        : { ...secureWriteInit('POST', { 'Content-Type': 'application/json' }, JSON.stringify(input)), signal });
    if (!response.ok)
        throw new Error('Не удалось получить маршрут согласования.');
    const body: unknown = await response.json();
    if (!saved)
        publicExpenseDraftBudgetPreview(body, input);
    const route = publicExpenseApprovalRoute(saved ? body : record(body) ? body.approvalRoute : null);
    const id = saved ? input.id : input.expenseRequestId;
    const version = saved ? input.version : input.expectedVersion;
    if ((id && route.requestId.toLowerCase() !== id.toLowerCase())
        || (version && route.requestVersion.toLowerCase() !== version.toLowerCase())
        || (route.previewOnly === saved && !(saved && route.previewOnly && ['DRAFT', 'RETURNED'].includes(route.statusCode))))
        throw new Error('Маршрут относится к другой версии заявки. Обновите карточку.');
    return route;
}
