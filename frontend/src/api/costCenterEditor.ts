import { apiUrl, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
import { isPublicExpenseRequestText, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
export type CostCenterOwner = {
    employeeId: string;
    name: string;
    employeeIsActive: boolean;
    roleIsActive: boolean;
};
export type CostCenterEditorState = {
    id: string;
    code: string;
    name: string;
    groupingName: string | null;
    owners: CostCenterOwner[];
    concurrencyStamp: string;
    onlyCostDepartment?: boolean;
};
export type UpdateCostCenterEditorInput = {
    code: string;
    name: string;
    groupingName: string | null;
    ownerSpecified: boolean;
    ownerEmployeeId: string | null;
    expectedConcurrencyStamp: string;
};
export type CreateCostCenterEditorInput = {
    code: string;
    name: string;
    groupingName: string | null;
    ownerEmployeeId: string | null;
    onlyCostDepartment?: boolean;
};
const invalid = 'Не удалось получить корректные настройки ЦФО.';
class CostCenterEditorError extends Error {
    constructor(readonly status: number) { super(invalid); }
}
const stamp = (v: unknown): v is string => typeof v === 'string' && /^[A-Fa-f0-9]{64}$/u.test(v);
const record = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v);
const text = (v: unknown, max: number): v is string => isPublicExpenseRequestText(v, max) && (v as string).trim() !== '';
const group = (v: unknown): v is string | null => v === null || (typeof v === 'string' && v.length <= 500
    && !/[\u0000-\u001f\u007f-\u009f]/u.test(v));
export function costCenterEditorUncertain(error: unknown) {
    return !(error instanceof CostCenterEditorError) || ![400, 401, 403, 404, 409].includes(error.status);
}
export function costCenterEditorConflict(error: unknown) { return error instanceof CostCenterEditorError && error.status === 409; }
export function costCenterEditorDenied(error: unknown) { return error instanceof CostCenterEditorError && [401, 403, 404].includes(error.status); }
export function costCenterEditorErrorMessage(error: unknown) {
    if (!(error instanceof CostCenterEditorError))
        return 'Результат сохранения не подтверждён. Повторите ту же попытку.';
    if (error.status === 400)
        return 'Проверьте код, название, группировку и выбранного сотрудника.';
    if (error.status === 401 || error.status === 403)
        return 'Нет доступа к изменению ЦФО.';
    if (error.status === 404)
        return 'ЦФО не найден или недоступен.';
    if (error.status === 409)
        return 'ЦФО или назначения изменились. Ваш ввод сохранён. Отмените правки и откройте ЦФО заново.';
    return 'Результат сохранения не подтверждён. Повторите ту же попытку.';
}
export function costCenterCreateErrorMessage(error: unknown) {
    return error instanceof CostCenterEditorError && error.status === 409
        ? 'ЦФО с таким кодом уже существует. Укажите другой код.' : costCenterEditorErrorMessage(error);
}
export async function createCostCenterEditor(input: CreateCostCenterEditorInput, key: string) {
    if (!input || !text(input.code, 100) || !text(input.name, 500) || !group(input.groupingName)
        || (input.ownerEmployeeId !== null && !isPublicExpenseRequestUuid(input.ownerEmployeeId))
        || (input.onlyCostDepartment !== undefined && typeof input.onlyCostDepartment !== 'boolean')
        || (input.onlyCostDepartment === true && input.ownerEmployeeId !== null)
        || !text(key, 127) || key !== key.trim())
        throw new CostCenterEditorError(400);
    const body: CreateCostCenterEditorInput = { code: input.code, name: input.name,
        groupingName: input.groupingName, ownerEmployeeId: input.ownerEmployeeId,
        ...(input.onlyCostDepartment === undefined ? {} : { onlyCostDepartment: input.onlyCostDepartment }) };
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/reference-data/cost-centers/editor'), secureWriteInit('POST', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, JSON.stringify(body)));
    if (response.status !== 200) {
        void response.arrayBuffer().catch(() => undefined);
        throw new CostCenterEditorError(response.status);
    }
    const value: unknown = await response.json();
    if (!record(value) || !isPublicExpenseRequestUuid(value.id))
        throw new Error(invalid);
    const result = publicCostCenterEditor(value, value.id);
    if (result.code !== body.code.trim() || result.name !== body.name.trim()
        || (body.onlyCostDepartment === true && result.onlyCostDepartment !== true)
        || result.groupingName !== (body.groupingName?.trim().normalize('NFC') || null)
        || (body.ownerEmployeeId === null ? result.owners.length !== 0 : result.owners.length !== 1
            || result.owners[0].employeeId.toLowerCase() !== body.ownerEmployeeId.toLowerCase()
            || !result.owners[0].employeeIsActive || !result.owners[0].roleIsActive))
        throw new Error(invalid);
    return result;
}
export function publicCostCenterEditor(value: unknown, expectedId: string): CostCenterEditorState {
    if (!record(value) || !isPublicExpenseRequestUuid(value.id) || value.id.toLowerCase() !== expectedId.toLowerCase()
        || !text(value.code, 100) || !text(value.name, 500) || !group(value.groupingName)
        || !stamp(value.concurrencyStamp) || !Array.isArray(value.owners)
        || (value.onlyCostDepartment !== undefined && typeof value.onlyCostDepartment !== 'boolean'))
        throw new Error(invalid);
    const owners = value.owners.map((o): CostCenterOwner => {
        if (!record(o) || !isPublicExpenseRequestUuid(o.employeeId) || !text(o.name, 150)
            || typeof o.employeeIsActive !== 'boolean' || typeof o.roleIsActive !== 'boolean')
            throw new Error(invalid);
        return { employeeId: o.employeeId, name: o.name, employeeIsActive: o.employeeIsActive, roleIsActive: o.roleIsActive };
    });
    if (new Set(owners.map(o => o.employeeId.toLowerCase())).size !== owners.length)
        throw new Error(invalid);
    return { id: value.id, code: value.code, name: value.name, groupingName: value.groupingName,
        owners, concurrencyStamp: value.concurrencyStamp,
        ...(value.onlyCostDepartment === undefined ? {} : { onlyCostDepartment: value.onlyCostDepartment }) };
}
function url(id: string) {
    if (!isPublicExpenseRequestUuid(id))
        throw new CostCenterEditorError(400);
    return `/api/v1/reference-data/cost-centers/${encodeURIComponent(id)}/editor`;
}
async function responseState(response: Response, id: string) {
    if (response.status !== 200) {
        void response.arrayBuffer().catch(() => undefined);
        throw new CostCenterEditorError(response.status);
    }
    return publicCostCenterEditor(await response.json(), id);
}
export async function getCostCenterEditor(id: string, signal?: AbortSignal) {
    const path = url(id);
    return responseState(await fetchWithSafeTransport(apiUrl(path), { ...secureReadInit(), signal }), id);
}
export async function updateCostCenterEditor(id: string, input: UpdateCostCenterEditorInput, key: string) {
    const path = url(id);
    if (!input || !text(input.code, 100) || !text(input.name, 500) || !group(input.groupingName)
        || !stamp(input.expectedConcurrencyStamp) || typeof input.ownerSpecified !== 'boolean'
        || (input.ownerSpecified ? !isPublicExpenseRequestUuid(input.ownerEmployeeId) : input.ownerEmployeeId !== null)
        || !text(key, 127) || key !== key.trim())
        throw new CostCenterEditorError(400);
    const body: UpdateCostCenterEditorInput = { code: input.code, name: input.name, groupingName: input.groupingName,
        ownerSpecified: input.ownerSpecified, ownerEmployeeId: input.ownerEmployeeId,
        expectedConcurrencyStamp: input.expectedConcurrencyStamp };
    const result = await responseState(await fetchWithSafeTransport(apiUrl(path), secureWriteInit('PUT', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, JSON.stringify(body))), id);
    if (result.code !== body.code.trim() || result.name !== body.name.trim()
        || result.groupingName !== (body.groupingName?.trim().normalize('NFC') || null)
        || (body.ownerSpecified && (result.owners.length !== 1
            || result.owners[0].employeeId.toLowerCase() !== body.ownerEmployeeId!.toLowerCase()
            || !result.owners[0].employeeIsActive || !result.owners[0].roleIsActive)))
        throw new Error(invalid);
    return result;
}
