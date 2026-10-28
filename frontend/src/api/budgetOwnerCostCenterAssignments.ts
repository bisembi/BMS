import { apiUrl, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
import { isDevelopment } from './runtime';
import type { BudgetOwnerCostCenterAssignmentDirectory } from '../types';
const unsafePublicTextPattern = /[\u0000-\u001f\u007f-\u009f]/u;
const malformedResponseMessage = 'Некорректный ответ API связей владельцев бюджета и ЦФО.';
export type BudgetOwnerCostCenterAssignmentInput = {
    employeeCode: string;
    costCenterCode: string;
};
class BudgetOwnerAssignmentApiError extends Error {
    constructor(readonly status: number) { super('Не удалось изменить связь владельца бюджета и ЦФО.'); }
}
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function isSafePublicText(value: string, maximumLength: number): boolean {
    return value.length > 0
        && value.length <= maximumLength
        && value.trim() === value
        && !unsafePublicTextPattern.test(value);
}
/// Narrows the response to the four approved labels. Database identifiers,
/// source users and any future diagnostic fields are discarded rather than
/// becoming accidental UI data.
export function publicBudgetOwnerCostCenterAssignmentDirectory(payload: unknown): BudgetOwnerCostCenterAssignmentDirectory {
    if (!isRecord(payload)
        || payload.catalog !== 'budget-owner-cost-center-assignments'
        || (payload.source !== 'bms_web'
            && !(isDevelopment && payload.source === 'development-mock'))
        || payload.refreshedAtUtc !== null
        || !Array.isArray(payload.items)) {
        throw new Error(malformedResponseMessage);
    }
    const assignmentKeys = new Set<string>();
    const items = payload.items.map((value) => {
        if (!isRecord(value)
            || typeof value.employeeCode !== 'string'
            || !isSafePublicText(value.employeeCode, 100)
            || typeof value.employeeName !== 'string'
            || !isSafePublicText(value.employeeName, 500)
            || typeof value.costCenterCode !== 'string'
            || !isSafePublicText(value.costCenterCode, 100)
            || typeof value.costCenterName !== 'string'
            || !isSafePublicText(value.costCenterName, 500)) {
            throw new Error(malformedResponseMessage);
        }
        const key = `${value.employeeCode}\u0000${value.costCenterCode}`.toLocaleLowerCase('en-US');
        if (assignmentKeys.has(key)) {
            throw new Error(malformedResponseMessage);
        }
        assignmentKeys.add(key);
        return {
            employeeCode: value.employeeCode,
            employeeName: value.employeeName,
            costCenterCode: value.costCenterCode,
            costCenterName: value.costCenterName,
        };
    });
    return {
        catalog: 'budget-owner-cost-center-assignments',
        source: payload.source,
        refreshedAtUtc: null,
        items,
    };
}
export async function getBudgetOwnerCostCenterAssignments(): Promise<BudgetOwnerCostCenterAssignmentDirectory> {
    let response: Response;
    try {
        response = await fetchWithSafeTransport(apiUrl('/api/v1/reference-data/budget-owner-cost-center-assignments'), secureReadInit());
    }
    catch {
        throw new Error('Не удалось загрузить связи владельцев бюджета и ЦФО.');
    }
    if (!response.ok) {
        throw new Error('Не удалось загрузить связи владельцев бюджета и ЦФО.');
    }
    try {
        return publicBudgetOwnerCostCenterAssignmentDirectory(await response.json());
    }
    catch {
        throw new Error('Не удалось загрузить связи владельцев бюджета и ЦФО.');
    }
}
function publicAssignmentItem(value: unknown): BudgetOwnerCostCenterAssignmentInput {
    if (!isRecord(value)
        || typeof value.employeeCode !== 'string' || !isSafePublicText(value.employeeCode, 100)
        || typeof value.employeeName !== 'string' || !isSafePublicText(value.employeeName, 500)
        || typeof value.costCenterCode !== 'string' || !isSafePublicText(value.costCenterCode, 100)
        || typeof value.costCenterName !== 'string' || !isSafePublicText(value.costCenterName, 500)) {
        throw new Error(malformedResponseMessage);
    }
    return { employeeCode: value.employeeCode, costCenterCode: value.costCenterCode };
}
export function budgetOwnerAssignmentErrorMessage(error: unknown, action: 'создать' | 'изменить' | 'удалить'): string {
    if (!(error instanceof BudgetOwnerAssignmentApiError))
        return `Результат операции «${action}» не подтверждён.`;
    if (error.status === 400)
        return 'Укажите сотрудника и ЦФО.';
    if (error.status === 401)
        return 'Требуется авторизация.';
    if (error.status === 403)
        return 'Изменение связей доступно финансовому контролёру, финансовому директору, генеральному директору и системному администратору.';
    if (error.status === 404)
        return 'Сотрудник или ЦФО не найдены.';
    if (error.status === 409)
        return 'Такая связь уже существует.';
    return `Результат операции «${action}» не подтверждён.`;
}
async function writeAssignment(method: 'POST' | 'PUT' | 'DELETE', input: BudgetOwnerCostCenterAssignmentInput, old?: BudgetOwnerCostCenterAssignmentInput, key?: string): Promise<BudgetOwnerCostCenterAssignmentInput> {
    if (!input || !isSafePublicText(input.employeeCode, 100) || !isSafePublicText(input.costCenterCode, 100))
        throw new BudgetOwnerAssignmentApiError(400);
    const params = new URLSearchParams();
    if (old) {
        params.set('employeeCode', old.employeeCode);
        params.set('costCenterCode', old.costCenterCode);
    }
    const suffix = params.size ? `?${params.toString()}` : '';
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    if (key)
        headers['Idempotency-Key'] = key;
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/reference-data/budget-owner-cost-center-assignments${suffix}`), {
        ...secureWriteInit(method, headers, method === 'DELETE' ? undefined : JSON.stringify(input)),
    });
    if (response.status !== 200)
        throw new BudgetOwnerAssignmentApiError(response.status);
    try {
        const value = publicAssignmentItem(await response.json());
        return value;
    }
    catch {
        throw new Error('Не удалось получить подтверждение изменения связи.');
    }
}
export function createBudgetOwnerCostCenterAssignment(input: BudgetOwnerCostCenterAssignmentInput, key: string) {
    return writeAssignment('POST', input, undefined, key);
}
export function updateBudgetOwnerCostCenterAssignment(old: BudgetOwnerCostCenterAssignmentInput, input: BudgetOwnerCostCenterAssignmentInput) {
    return writeAssignment('PUT', input, old);
}
export function deleteBudgetOwnerCostCenterAssignment(input: BudgetOwnerCostCenterAssignmentInput) {
    return writeAssignment('DELETE', input, input);
}
