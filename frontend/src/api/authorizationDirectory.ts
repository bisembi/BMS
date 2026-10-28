import { apiUrl, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
import { isPublicExpenseRequestText, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
export type AuthorizationDirectoryEmployee = {
    employeeId: string;
    code: string;
    name: string;
    isActive: boolean;
    actorExternalId: string | null;
    actorMappingIsActive: boolean;
    roles: string[];
    concurrencyStamp: string;
};
export type AuthorizationDirectoryPage = {
    items: AuthorizationDirectoryEmployee[];
    skip: number;
    take: number;
    total: number;
};
export type UpdateAuthorizationDirectoryActorInput = {
    expectedConcurrencyStamp: string;
    actorExternalId: string | null;
    isActive: boolean;
};
export type AuthorizationDirectoryRoleOption = {
    roleId: string;
    code: string;
    name: string;
};
export type ActiveDirectoryEmployeeCandidate = {
    actorExternalId: string;
    accountName: string;
    name: string;
    isActive: boolean;
    employeeId: string | null;
    employeeCode: string | null;
    roles: string[];
    concurrencyStamp: string | null;
};
export type ActiveDirectoryEmployeePage = {
    items: ActiveDirectoryEmployeeCandidate[];
    skip: number;
    take: number;
    total: number;
};
export type CreateAuthorizationDirectoryEmployeeInput = {
    code: string;
    name: string;
    actorExternalId: string | null;
    isActive: boolean;
};
export type AssignAuthorizationDirectoryRoleInput = {
    expectedConcurrencyStamp: string;
    roleId: string;
    isActive: boolean;
};
class AuthorizationDirectoryApiError extends Error {
    constructor(readonly status: number) { super('Не удалось выполнить запрос AD-справочника.'); }
}
const invalidResponse = 'Не удалось получить корректные данные AD-справочника.';
const uncertain = 'Результат сохранения не подтверждён. Повторите ту же попытку или загрузите состояние заново.';
function record(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}
function fail(): never { throw new Error(invalidResponse); }
function stamp(value: unknown): value is string {
    return typeof value === 'string' && /^[0-9A-F]{64}$/u.test(value);
}
function actor(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= 256
        && value.trim() === value && !/[\u0000-\u001f\u007f-\u009f]/u.test(value);
}
function roles(value: unknown): value is string[] {
    if (!Array.isArray(value) || value.length > 100)
        return false;
    const names = value.map(item => item as unknown);
    return names.every(item => typeof item === 'string' && isPublicExpenseRequestText(item, 100))
        && new Set(names).size === names.length;
}
function uuid(value: unknown): value is string { return isPublicExpenseRequestUuid(value); }
export function publicAuthorizationDirectoryEmployee(value: unknown): AuthorizationDirectoryEmployee {
    if (!record(value) || !isPublicExpenseRequestUuid(value.employeeId)
        || !isPublicExpenseRequestText(value.code, 20)
        || !isPublicExpenseRequestText(value.name, 150)
        || typeof value.isActive !== 'boolean'
        || (value.actorExternalId !== null && !actor(value.actorExternalId))
        || typeof value.actorMappingIsActive !== 'boolean' || !roles(value.roles) || !stamp(value.concurrencyStamp)
        || (value.actorMappingIsActive !== (value.actorExternalId !== null)))
        return fail();
    return {
        employeeId: value.employeeId, code: value.code, name: value.name,
        isActive: value.isActive, actorExternalId: value.actorExternalId,
        actorMappingIsActive: value.actorMappingIsActive, roles: value.roles, concurrencyStamp: value.concurrencyStamp,
    };
}
function publicRole(value: unknown): AuthorizationDirectoryRoleOption {
    if (!record(value) || !uuid(value.roleId) || !isPublicExpenseRequestText(value.code, 30)
        || !isPublicExpenseRequestText(value.name, 100))
        return fail();
    return { roleId: value.roleId, code: value.code, name: value.name };
}
export function publicAuthorizationDirectoryRoles(value: unknown): AuthorizationDirectoryRoleOption[] {
    if (!Array.isArray(value))
        return fail();
    const result = value.map(publicRole);
    if (new Set(result.map(item => item.roleId.toLowerCase())).size !== result.length)
        return fail();
    return result;
}
function publicActiveDirectoryEmployee(value: unknown): ActiveDirectoryEmployeeCandidate {
    if (!record(value) || !actor(value.actorExternalId)
        || !isPublicExpenseRequestText(value.accountName, 256)
        || !isPublicExpenseRequestText(value.name, 150)
        || typeof value.isActive !== 'boolean'
        || (value.employeeId !== null && !uuid(value.employeeId))
        || (value.employeeCode !== null && !isPublicExpenseRequestText(value.employeeCode, 20))
        || !roles(value.roles)
        || (value.concurrencyStamp !== null && !stamp(value.concurrencyStamp)))
        return fail();
    return {
        actorExternalId: value.actorExternalId, accountName: value.accountName, name: value.name,
        isActive: value.isActive, employeeId: value.employeeId, employeeCode: value.employeeCode,
        roles: value.roles, concurrencyStamp: value.concurrencyStamp,
    };
}
export function publicActiveDirectoryEmployeePage(value: unknown, skip: number, take: number): ActiveDirectoryEmployeePage {
    if (!record(value) || !Array.isArray(value.items) || value.skip !== skip || value.take !== take
        || typeof value.total !== 'number' || !Number.isSafeInteger(value.total) || value.total < 0
        || value.total > 2147483647
        || value.items.length !== Math.min(take, Math.max(0, value.total - skip)))
        return fail();
    const items = value.items.map(publicActiveDirectoryEmployee);
    if (new Set(items.map(item => item.actorExternalId.toLowerCase())).size !== items.length)
        return fail();
    return { items, skip, take, total: value.total };
}
function publicPage(value: unknown, skip: number, take: number): AuthorizationDirectoryPage {
    if (!record(value) || !Array.isArray(value.items) || value.skip !== skip || value.take !== take
        || typeof value.total !== 'number' || !Number.isSafeInteger(value.total) || value.total < 0
        || value.total > 2147483647
        || value.items.length !== Math.min(take, Math.max(0, value.total - skip)))
        return fail();
    const items = value.items.map(publicAuthorizationDirectoryEmployee);
    if (new Set(items.map(item => item.employeeId.toLowerCase())).size !== items.length)
        return fail();
    return { items, skip, take, total: value.total };
}
function reject(response: Response): never {
    void response.arrayBuffer().catch(() => undefined);
    throw new AuthorizationDirectoryApiError(response.status);
}
function validPage(query: string, skip: number, take: number): boolean {
    return typeof query === 'string' && query.trim().length <= 200
        && !/[\u0000-\u001f\u007f-\u009f]/u.test(query)
        && Number.isSafeInteger(skip) && skip >= 0 && skip <= 2147483647
        && Number.isInteger(take) && take >= 1 && take <= 100;
}
export function authorizationDirectoryErrorMessage(error: unknown): string {
    if (!(error instanceof AuthorizationDirectoryApiError))
        return uncertain;
    if (error.status === 400)
        return 'Проверьте имя AD-учётной записи и версию записи.';
    if (error.status === 401)
        return 'Требуется авторизация.';
    if (error.status === 403)
        return 'Изменение AD-связей доступно финансовому контролёру, финансовому директору, генеральному директору и системному администратору.';
    if (error.status === 404)
        return 'Сотрудник не найден или недоступен.';
    if (error.status === 409)
        return 'Запись изменилась или учётная запись уже связана с другим сотрудником.';
    return uncertain;
}
export function authorizationDirectoryCreateErrorMessage(error: unknown): string {
    if (!(error instanceof AuthorizationDirectoryApiError))
        return uncertain;
    if (error.status === 400)
        return 'Проверьте код, ФИО и доменную учётную запись сотрудника.';
    if (error.status === 401)
        return 'Требуется авторизация.';
    if (error.status === 403)
        return 'Добавление сотрудников доступно финансовому контролёру, финансовому директору, генеральному директору и системному администратору.';
    if (error.status === 409)
        return 'Код сотрудника или доменная учётная запись уже используются.';
    return uncertain;
}
export function isAuthorizationDirectoryConflict(error: unknown): boolean {
    return error instanceof AuthorizationDirectoryApiError && error.status === 409;
}
export function isAuthorizationDirectoryValidationError(error: unknown): boolean {
    return error instanceof AuthorizationDirectoryApiError && error.status === 400;
}
export function isAuthorizationDirectoryResultUncertain(error: unknown): boolean {
    return !(error instanceof AuthorizationDirectoryApiError)
        || ![400, 401, 403, 404, 409].includes(error.status);
}
export async function listAuthorizationDirectoryEmployees(query: string, skip: number, take: number, signal?: AbortSignal): Promise<AuthorizationDirectoryPage> {
    if (!validPage(query, skip, take))
        throw new AuthorizationDirectoryApiError(400);
    const parameters = new URLSearchParams({ query: query.trim(), skip: String(skip), take: String(take) });
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/reference-data/authorization-directory/employees?${parameters}`), { ...secureReadInit(), signal });
    if (response.status !== 200)
        reject(response);
    try {
        return publicPage(await response.json(), skip, take);
    }
    catch (error) {
        if (error instanceof Error && error.name === 'AbortError')
            throw error;
        return fail();
    }
}
export async function createAuthorizationDirectoryEmployee(input: CreateAuthorizationDirectoryEmployeeInput, key: string, signal?: AbortSignal): Promise<AuthorizationDirectoryEmployee> {
    if (!input || !isPublicExpenseRequestText(input.code, 20)
        || !isPublicExpenseRequestText(input.name, 150)
        || (input.actorExternalId !== null && !actor(input.actorExternalId))
        || typeof input.isActive !== 'boolean'
        || !isPublicExpenseRequestText(key, 127))
        throw new AuthorizationDirectoryApiError(400);
    const body = {
        code: input.code.trim(),
        name: input.name.trim(),
        actorExternalId: input.actorExternalId,
        isActive: input.isActive,
    };
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/reference-data/authorization-directory/employees'), { ...secureWriteInit('POST', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, JSON.stringify(body)), signal });
    if (response.status !== 200)
        reject(response);
    const result = publicAuthorizationDirectoryEmployee(await response.json());
    if (result.code !== body.code || result.name !== body.name
        || result.isActive !== input.isActive
        || result.actorExternalId !== input.actorExternalId
        || result.actorMappingIsActive !== (input.actorExternalId !== null && input.isActive))
        return fail();
    return result;
}
export async function getAuthorizationDirectoryEmployee(employeeId: string, signal?: AbortSignal): Promise<AuthorizationDirectoryEmployee> {
    if (!isPublicExpenseRequestUuid(employeeId))
        throw new AuthorizationDirectoryApiError(400);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/reference-data/authorization-directory/employees/${encodeURIComponent(employeeId)}`), { ...secureReadInit(), signal });
    if (response.status !== 200)
        reject(response);
    try {
        return publicAuthorizationDirectoryEmployee(await response.json());
    }
    catch (error) {
        if (error instanceof Error && error.name === 'AbortError')
            throw error;
        return fail();
    }
}
export async function updateAuthorizationDirectoryActor(employeeId: string, input: UpdateAuthorizationDirectoryActorInput, key: string, signal?: AbortSignal): Promise<AuthorizationDirectoryEmployee> {
    if (!isPublicExpenseRequestUuid(employeeId) || !input || !stamp(input.expectedConcurrencyStamp)
        || (input.actorExternalId !== null && !actor(input.actorExternalId))
        || typeof input.isActive !== 'boolean' || (input.isActive && input.actorExternalId === null)
        || !isPublicExpenseRequestText(key, 127))
        throw new AuthorizationDirectoryApiError(400);
    const body = {
        expectedConcurrencyStamp: input.expectedConcurrencyStamp,
        actorExternalId: input.actorExternalId,
        isActive: input.isActive,
    };
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/reference-data/authorization-directory/employees/${encodeURIComponent(employeeId)}/actor-mapping`), { ...secureWriteInit('PUT', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, JSON.stringify(body)), signal });
    if (response.status !== 200)
        reject(response);
    const result = publicAuthorizationDirectoryEmployee(await response.json());
    if (result.employeeId.toLowerCase() !== employeeId.toLowerCase()
        || result.actorExternalId !== input.actorExternalId
        || result.actorMappingIsActive !== input.isActive)
        return fail();
    return result;
}
export async function listAuthorizationDirectoryRoles(signal?: AbortSignal): Promise<AuthorizationDirectoryRoleOption[]> {
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/reference-data/authorization-directory/roles'), { ...secureReadInit(), signal });
    if (response.status !== 200)
        reject(response);
    return publicAuthorizationDirectoryRoles(await response.json());
}
export async function listActiveDirectoryEmployees(query: string, skip: number, take: number, signal?: AbortSignal): Promise<ActiveDirectoryEmployeePage> {
    if (!validPage(query, skip, take))
        throw new AuthorizationDirectoryApiError(400);
    const parameters = new URLSearchParams({ query: query.trim(), skip: String(skip), take: String(take) });
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/reference-data/authorization-directory/active-ad-employees?${parameters}`), { ...secureReadInit(), signal });
    if (response.status !== 200)
        reject(response);
    return publicActiveDirectoryEmployeePage(await response.json(), skip, take);
}
export async function createAuthorizationDirectoryEmployeeFromAd(actorExternalId: string, roleId: string, key: string, signal?: AbortSignal): Promise<AuthorizationDirectoryEmployee> {
    if (!actor(actorExternalId) || !uuid(roleId) || !isPublicExpenseRequestText(key, 127)) {
        throw new AuthorizationDirectoryApiError(400);
    }
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/reference-data/authorization-directory/employees/from-ad'), { ...secureWriteInit('POST', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, JSON.stringify({ actorExternalId, roleId })), signal });
    if (response.status !== 200)
        reject(response);
    return publicAuthorizationDirectoryEmployee(await response.json());
}
export async function updateAuthorizationDirectoryRole(employeeId: string, input: AssignAuthorizationDirectoryRoleInput, key: string, signal?: AbortSignal): Promise<AuthorizationDirectoryEmployee> {
    if (!uuid(employeeId) || !input || !stamp(input.expectedConcurrencyStamp)
        || !uuid(input.roleId) || typeof input.isActive !== 'boolean'
        || !isPublicExpenseRequestText(key, 127))
        throw new AuthorizationDirectoryApiError(400);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/reference-data/authorization-directory/employees/${encodeURIComponent(employeeId)}/roles`), { ...secureWriteInit('PUT', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, JSON.stringify(input)), signal });
    if (response.status !== 200)
        reject(response);
    return publicAuthorizationDirectoryEmployee(await response.json());
}
