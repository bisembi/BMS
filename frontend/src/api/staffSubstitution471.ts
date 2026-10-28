import { apiUrl, fetchWithSafeTransport, fetchWithOneTimeRefresh, secureReadInit, secureWriteInit } from './http';
import { getCurrentSession } from './session';
import { isPublicExpenseRequestUuid as uuid, isPublicExpenseRequestText as text } from './expenseRequestPublicResponse';
import { isBmsRoleCode, profileForRole } from '../utils/activeRole';
export type StaffInput471 = {
    deputyEmployeeId: string;
    roleCode: string;
    from: string;
    through: string | null;
};
export type StaffDoc471 = {
    id: string;
    number: string;
    principalEmployeeId: string;
    principalName: string;
    deputyEmployeeId: string;
    deputyName: string;
    roleCode: string;
    roleName: string;
    from: string;
    through: string | null;
    status: string;
    version: number;
    updatedAtUtc: string;
    commands: string[];
};
export type StaffOptions471 = {
    employeeId: string;
    employeeName: string;
    roles: {
        code: string;
        name: string;
    }[];
    employees: {
        id: string;
        name: string;
    }[];
};
export type StaffPage471 = {
    items: StaffDoc471[];
    total: number;
    skip: number;
    take: number;
};
export type StaffNews471 = {
    id: string;
    number: string;
    principalName: string;
    deputyName: string;
    roleName: string;
    from: string;
    through: string | null;
    status: string;
    atUtc: string;
};
export type StaffAction471 = {
    id: string;
    atUtc: string;
    actorName: string;
    principalName: string;
    roleName: string;
    objectType: string;
    objectId: string;
    action: string;
};
export type StaffCommand471 = {
    commandId: string;
    expectedVersion: number;
    input?: StaffInput471;
};
const record = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const date = (v: unknown): v is string => typeof v === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(v) && Number.isFinite(Date.parse(v)) && new Date(v + 'T00:00:00Z').toISOString().slice(0, 10) === v;
const timestamp = (v: unknown): v is string => typeof v === 'string' && Number.isFinite(Date.parse(v));
const role = (v: unknown): v is string => isBmsRoleCode(v) && profileForRole(v) === 'BUSINESS';
const invalid = () => Error('Некорректный ответ сервера замещений. Данные не заменены.');
const states = ['DRAFT', 'PENDING', 'CONFIRMED', 'REJECTED', 'REVOKED', 'EXPIRED'];
export const staffStatus471: Readonly<Record<string, string>> = { DRAFT: 'Черновик', PENDING: 'Ожидает подтверждения', CONFIRMED: 'Подтверждено', REJECTED: 'Отказано', REVOKED: 'Отозвано', EXPIRED: 'Срок завершён' };
export function staffDoc471(v: unknown): StaffDoc471 {
    if (!record(v) || !uuid(v.id) || !uuid(v.principalEmployeeId) || !uuid(v.deputyEmployeeId) || v.principalEmployeeId === v.deputyEmployeeId
        || !text(v.number, 100) || !text(v.principalName, 256) || !text(v.deputyName, 256) || !role(v.roleCode) || !text(v.roleName, 256)
        || !date(v.from) || (v.through != null && (!date(v.through) || v.through < v.from)) || !states.includes(String(v.status))
        || !Number.isSafeInteger(v.version) || (v.version as number) < 1 || !timestamp(v.updatedAtUtc) || !Array.isArray(v.commands)
        || new Set(v.commands).size !== v.commands.length || v.commands.some(c => !['update', 'submit', 'confirm', 'reject', 'revoke'].includes(String(c))))
        throw invalid();
    return { ...v, through: v.through ?? null } as StaffDoc471;
}
export function staffOptions471(v: unknown): StaffOptions471 {
    if (!record(v) || !uuid(v.employeeId) || !text(v.employeeName, 256) || !Array.isArray(v.roles) || v.roles.length > 12
        || v.roles.some(r => !record(r) || !role(r.code) || !text(r.name, 256)) || new Set(v.roles.map(r => r.code)).size !== v.roles.length
        || !Array.isArray(v.employees) || v.employees.length > 2000 || v.employees.some(e => !record(e) || !uuid(e.id) || e.id === v.employeeId || !text(e.name, 256))
        || new Set(v.employees.map(e => e.id)).size !== v.employees.length)
        throw invalid();
    return v as StaffOptions471;
}
export class StaffError471 extends Error {
    constructor(public status: number, message: string) { super(message); }
}
async function request(path: string, signal?: AbortSignal, body?: StaffCommand471): Promise<unknown> {
    const json = body === undefined ? undefined : JSON.stringify(body);
    const send = () => fetchWithSafeTransport(apiUrl('/api/v1/staff-substitutions' + path), { ...(body === undefined ? secureReadInit() : secureWriteInit('POST', { 'Content-Type': 'application/json' }, json)), signal });
    const response = body === undefined ? await send() : await fetchWithOneTimeRefresh(send, async (r) => {
        if (r.status !== 403)
            return false;
        try {
            const p: unknown = await r.clone().json();
            return record(p) && p.code === 'csrf-token-invalid';
        }
        catch {
            return false;
        }
    }, getCurrentSession);
    if (!response.ok)
        throw new StaffError471(response.status, response.status === 409 ? 'Документ или права изменились. Обновите документ.' : response.status === 403 ? 'Действие недоступно. Для кадрового документа выберите свою роль.' : response.status === 404 ? 'Документ не найден или недоступен.' : 'Не удалось подтвердить результат. Проверьте документ или повторите ту же отправку.');
    return response.json();
}
export async function getStaffOptions471(signal?: AbortSignal) { return staffOptions471(await request('/options', signal)); }
export async function getStaffDoc471(id: string, signal?: AbortSignal) {
    if (!uuid(id))
        throw invalid();
    return staffDoc471(await request('/' + id, signal));
}
export async function listStaff471(skip: number, take: number, signal?: AbortSignal): Promise<StaffPage471> {
    const v = await request(`?skip=${skip}&take=${take}`, signal);
    if (!record(v) || v.skip !== skip || v.take !== take || !Number.isSafeInteger(v.total) || (v.total as number) < 0 || !Array.isArray(v.items)
        || v.items.length !== Math.min(take, Math.max(0, (v.total as number) - skip)))
        throw invalid();
    return { skip, take, total: v.total as number, items: v.items.map(staffDoc471) };
}
export async function staffCommand471(id: string, action: string, body: StaffCommand471) {
    if (!uuid(id) || !uuid(body.commandId) || !Number.isSafeInteger(body.expectedVersion) || body.expectedVersion < 0 || !['create', 'update', 'submit', 'confirm', 'reject', 'revoke'].includes(action))
        throw invalid();
    return staffDoc471(await request('/' + id + '/' + action, undefined, body));
}
export async function pendingStaff471(signal?: AbortSignal): Promise<StaffDoc471[]> {
    const v = await request('/pending', signal);
    if (!Array.isArray(v) || v.length > 100)
        throw invalid();
    return v.map(staffDoc471);
}
export async function staffNews471(signal?: AbortSignal): Promise<StaffNews471[]> {
    const v = await request('/news', signal);
    if (!Array.isArray(v) || v.length > 20 || v.some(n => !record(n) || !uuid(n.id) || !text(n.number, 100)
        || !text(n.principalName, 256) || !text(n.deputyName, 256) || !text(n.roleName, 256) || !date(n.from) || (n.through != null && !date(n.through))
        || !states.includes(String(n.status)) || !timestamp(n.atUtc)))
        throw invalid();
    return v as StaffNews471[];
}
export async function staffActions471(id: string, signal?: AbortSignal): Promise<StaffAction471[]> {
    if (!uuid(id))
        throw invalid();
    const v = await request('/' + id + '/actions', signal);
    if (!Array.isArray(v) || v.length > 100 || v.some(n => !record(n) || !uuid(n.id)
        || !timestamp(n.atUtc) || !['actorName', 'principalName', 'roleName', 'objectType', 'objectId', 'action'].every(k => text(n[k], 256))))
        throw invalid();
    return v as StaffAction471[];
}
export async function staffPendingSummary471(_skip: number, _take: number, signal: AbortSignal) { return { total: (await pendingStaff471(signal)).length }; }
