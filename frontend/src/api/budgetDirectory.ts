import { apiUrl, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
import { isPublicExpenseRequestText, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
export type BudgetDirectoryOption = {
    id: string;
    code: string;
    name: string;
    isActive: boolean;
    onlyCostDepartment?: boolean;
    purposeConcurrencyStamp?: string | null;
};
export type BudgetDirectoryPageResult = {
    items: BudgetDirectoryOption[];
    skip: number;
    take: number;
    total: number;
};
export type BudgetDirectoryMember = {
    employeeId: string;
    code: string;
    name: string;
    kind: 'OWNER' | 'PARTICIPANT';
    isActive: boolean;
    employeeIsActive: boolean;
};
export type BudgetDirectorySettings = {
    costCenterId: string;
    code: string;
    name: string;
    abbreviation: string | null;
    concurrencyStamp: string | null;
    numberingInitialized: boolean;
    members: BudgetDirectoryMember[];
    onlyCostDepartment?: boolean;
    purposeConcurrencyStamp?: string | null;
};
export type UpdateBudgetDirectoryInput = {
    expectedConcurrencyStamp: string | null;
    abbreviation: string;
    members: {
        employeeId: string;
        kind: 'OWNER' | 'PARTICIPANT';
        isActive: boolean;
    }[];
};
const invalidResponse = 'Не удалось получить корректные настройки бюджетов.';
const uncertain = 'Результат сохранения не подтверждён. Повторите ту же попытку или загрузите текущее состояние.';
class BudgetDirectoryApiError extends Error {
    constructor(readonly status: number) { super('Не удалось выполнить запрос справочника бюджетов.'); }
}
export function budgetDirectoryErrorMessage(error: unknown): string {
    if (!(error instanceof BudgetDirectoryApiError))
        return uncertain;
    if (error.status === 400)
        return 'Проверьте сокращение ЦФО и состав участников.';
    if (error.status === 401)
        return 'Требуется авторизация.';
    if (error.status === 403)
        return 'Настройки бюджетов доступны финансовому контролёру, финансовому директору, генеральному директору и администратору.';
    if (error.status === 404)
        return 'ЦФО не найден или недоступен.';
    if (error.status === 409)
        return 'Настройки изменились либо сокращение уже занято. Ваш ввод сохранён; загрузите текущее состояние.';
    return uncertain;
}
export function isBudgetDirectoryConflict(error: unknown): boolean {
    return error instanceof BudgetDirectoryApiError && error.status === 409;
}
export function isBudgetDirectoryValidationError(error: unknown): boolean {
    return error instanceof BudgetDirectoryApiError && error.status === 400;
}
export function isBudgetDirectoryResultUncertain(error: unknown): boolean {
    return !(error instanceof BudgetDirectoryApiError) || ![400, 401, 403, 404, 409].includes(error.status);
}
function reject(response: Response): never {
    void response.arrayBuffer().catch(() => undefined);
    throw new BudgetDirectoryApiError(response.status);
}
function record(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}
function sameId(a: string, b: string): boolean { return a.toLowerCase() === b.toLowerCase(); }
function prefix(value: unknown): value is string {
    if (typeof value !== 'string')
        return false;
    const normalized = value.normalize('NFC');
    return normalized.length <= 6 && /^\p{L}{2,3}$/u.test(normalized);
}
function fail(): never { throw new Error(invalidResponse); }
async function readSettings(response: Response, id: string): Promise<BudgetDirectorySettings> {
    try {
        return publicBudgetDirectorySettings(await response.json(), id);
    }
    catch (error) {
        if (error instanceof Error && error.name === 'AbortError')
            throw error;
        return fail();
    }
}
function option(value: unknown, employees: boolean): BudgetDirectoryOption {
    if (!record(value) || !isPublicExpenseRequestUuid(value.id)
        || !isPublicExpenseRequestText(value.code, employees ? 20 : 100)
        || !isPublicExpenseRequestText(value.name, employees ? 150 : 500)
        || typeof value.isActive !== 'boolean' || (employees && !value.isActive))
        return fail();
    if (value.onlyCostDepartment !== undefined && typeof value.onlyCostDepartment !== 'boolean')
        return fail();
    if (value.purposeConcurrencyStamp !== undefined && value.purposeConcurrencyStamp !== null
        && !isPublicExpenseRequestUuid(value.purposeConcurrencyStamp))
        return fail();
    return { id: value.id, code: value.code, name: value.name, isActive: value.isActive,
        ...(value.onlyCostDepartment !== undefined ? { onlyCostDepartment: value.onlyCostDepartment } : {}),
        ...(value.purposeConcurrencyStamp !== undefined ? { purposeConcurrencyStamp: value.purposeConcurrencyStamp as string | null } : {}) };
}
export function publicBudgetDirectoryPage(value: unknown, skip: number, take: number, employees: boolean): BudgetDirectoryPageResult {
    if (!record(value) || value.skip !== skip || value.take !== take || !Array.isArray(value.items)
        || typeof value.total !== 'number' || !Number.isSafeInteger(value.total) || value.total < 0 || value.total > 2147483647
        || value.items.length !== Math.min(take, Math.max(0, value.total - skip)))
        return fail();
    const items = value.items.map(item => option(item, employees));
    if (new Set(items.map(item => item.id.toLowerCase())).size !== items.length)
        return fail();
    return { items, skip, take, total: value.total };
}
export function publicBudgetDirectorySettings(value: unknown, expectedId: string): BudgetDirectorySettings {
    if (!record(value) || !isPublicExpenseRequestUuid(value.costCenterId) || !sameId(value.costCenterId, expectedId)
        || !isPublicExpenseRequestText(value.code, 100) || !isPublicExpenseRequestText(value.name, 500)
        || (value.abbreviation !== null && (!prefix(value.abbreviation) || value.abbreviation.normalize('NFC') !== value.abbreviation))
        || (value.concurrencyStamp !== null && !isPublicExpenseRequestUuid(value.concurrencyStamp))
        || typeof value.numberingInitialized !== 'boolean' || !Array.isArray(value.members))
        return fail();
    if (value.onlyCostDepartment !== undefined && typeof value.onlyCostDepartment !== 'boolean')
        return fail();
    if (value.purposeConcurrencyStamp !== undefined && value.purposeConcurrencyStamp !== null
        && !isPublicExpenseRequestUuid(value.purposeConcurrencyStamp))
        return fail();
    const members = value.members.map((member): BudgetDirectoryMember => {
        if (!record(member) || !isPublicExpenseRequestUuid(member.employeeId)
            || !isPublicExpenseRequestText(member.code, 20) || !isPublicExpenseRequestText(member.name, 150)
            || (member.kind !== 'OWNER' && member.kind !== 'PARTICIPANT')
            || typeof member.isActive !== 'boolean' || typeof member.employeeIsActive !== 'boolean')
            return fail();
        return { employeeId: member.employeeId, code: member.code, name: member.name, kind: member.kind,
            isActive: member.isActive, employeeIsActive: member.employeeIsActive };
    });
    if (new Set(members.map(member => member.employeeId.toLowerCase())).size !== members.length)
        return fail();
    return { costCenterId: value.costCenterId, code: value.code, name: value.name,
        abbreviation: value.abbreviation, concurrencyStamp: value.concurrencyStamp,
        numberingInitialized: value.numberingInitialized, members,
        ...(value.onlyCostDepartment !== undefined ? { onlyCostDepartment: value.onlyCostDepartment } : {}),
        ...(value.purposeConcurrencyStamp !== undefined ? { purposeConcurrencyStamp: value.purposeConcurrencyStamp as string | null } : {}) };
}
export type SetCostCenterPurpose457Input = {
    expectedConcurrencyStamp: string | null;
    onlyCostDepartment: boolean;
};
export async function setCostCenterPurpose457(id: string, input: SetCostCenterPurpose457Input, key: string) {
    const url = idPath(id) + '/purpose';
    if (!input || (input.expectedConcurrencyStamp !== null && !isPublicExpenseRequestUuid(input.expectedConcurrencyStamp))
        || typeof input.onlyCostDepartment !== 'boolean' || !isPublicExpenseRequestText(key, 127)
        || key !== key.trim())
        throw new BudgetDirectoryApiError(400);
    const body = { expectedConcurrencyStamp: input.expectedConcurrencyStamp, onlyCostDepartment: input.onlyCostDepartment };
    const response = await fetchWithSafeTransport(apiUrl(url), secureWriteInit('PUT', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, JSON.stringify(body)));
    if (response.status !== 200)
        reject(response);
    const result = await readSettings(response, id);
    if (result.onlyCostDepartment !== body.onlyCostDepartment || !result.purposeConcurrencyStamp
        || (body.expectedConcurrencyStamp !== null && sameId(result.purposeConcurrencyStamp, body.expectedConcurrencyStamp)))
        return fail();
    return result;
}
function idPath(id: string): string {
    if (!isPublicExpenseRequestUuid(id))
        throw new BudgetDirectoryApiError(400);
    return `/api/v1/budget/directory/cost-centers/${encodeURIComponent(id)}`;
}
async function list(query: string, skip: number, take: number, employees: boolean, signal?: AbortSignal): Promise<BudgetDirectoryPageResult> {
    if (typeof query !== 'string' || query.trim().length > 200 || /[\u0000-\u001f\u007f-\u009f]/u.test(query)
        || !Number.isSafeInteger(skip) || skip < 0 || skip > 2147483647
        || !Number.isInteger(take) || take < 1 || take > 100)
        throw new BudgetDirectoryApiError(400);
    const parameters = new URLSearchParams({ query: query.trim(), skip: String(skip), take: String(take) });
    const url = `/api/v1/budget/directory/${employees ? 'employees' : 'cost-centers'}?${parameters}`;
    const response = await fetchWithSafeTransport(apiUrl(url), { ...secureReadInit(), signal });
    if (response.status !== 200)
        reject(response);
    try {
        return publicBudgetDirectoryPage(await response.json(), skip, take, employees);
    }
    catch (error) {
        if (error instanceof Error && error.name === 'AbortError')
            throw error;
        return fail();
    }
}
export function listBudgetDirectoryCostCenters(query: string, skip: number, take: number, signal?: AbortSignal) {
    return list(query, skip, take, false, signal);
}
export function listBudgetDirectoryEmployees(query: string, skip: number, take: number, signal?: AbortSignal) {
    return list(query, skip, take, true, signal);
}
export async function getBudgetDirectory(id: string, signal?: AbortSignal): Promise<BudgetDirectorySettings> {
    const url = idPath(id);
    const response = await fetchWithSafeTransport(apiUrl(url), { ...secureReadInit(), signal });
    if (response.status !== 200)
        reject(response);
    return readSettings(response, id);
}
export async function updateBudgetDirectory(id: string, input: UpdateBudgetDirectoryInput, key: string, signal?: AbortSignal): Promise<BudgetDirectorySettings> {
    const url = idPath(id);
    if (!input || (input.expectedConcurrencyStamp !== null && !isPublicExpenseRequestUuid(input.expectedConcurrencyStamp))
        || !prefix(input.abbreviation) || !Array.isArray(input.members) || input.members.length > 1000
        || !isPublicExpenseRequestText(key, 127)
        || input.members.some(member => !member || !isPublicExpenseRequestUuid(member.employeeId)
            || (member.kind !== 'OWNER' && member.kind !== 'PARTICIPANT') || typeof member.isActive !== 'boolean')
        || new Set(input.members.map(member => member.employeeId.toLowerCase())).size !== input.members.length) {
        throw new BudgetDirectoryApiError(400);
    }
    const body = { expectedConcurrencyStamp: input.expectedConcurrencyStamp, abbreviation: input.abbreviation,
        members: input.members.map(member => ({ employeeId: member.employeeId, kind: member.kind, isActive: member.isActive })) };
    const response = await fetchWithSafeTransport(apiUrl(url), { ...secureWriteInit('PUT', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, JSON.stringify(body)), signal });
    if (response.status !== 200)
        reject(response);
    const result = await readSettings(response, id);
    if (result.concurrencyStamp === null || (body.expectedConcurrencyStamp !== null && sameId(result.concurrencyStamp, body.expectedConcurrencyStamp))
        || result.abbreviation === null
        // Client-side comparison is not the prefix authority; .NET owns canonicalization.
        || result.abbreviation.normalize('NFC').toUpperCase() !== body.abbreviation.normalize('NFC').toUpperCase())
        return fail();
    const requested = new Map(body.members.map(member => [member.employeeId.toLowerCase(), member]));
    const returned = new Map(result.members.map(member => [member.employeeId.toLowerCase(), member]));
    for (const [idKey, member] of requested) {
        const saved = returned.get(idKey);
        if (!saved || saved.kind !== member.kind || saved.isActive !== member.isActive
            || (saved.isActive && !saved.employeeIsActive))
            return fail();
    }
    if (result.members.some(member => !requested.has(member.employeeId.toLowerCase()) && member.isActive))
        return fail();
    return result;
}
