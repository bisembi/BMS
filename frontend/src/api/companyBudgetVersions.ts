import { apiUrl, fetchWithOneTimeRefresh, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
import { isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
import { getCurrentSession } from './session';
export type CompanyBudgetInstance = {
    id: string;
    number: string;
    costCenterId: string;
    costCenterCode: string;
    costCenterName: string;
    validFrom: string;
    validTo: string;
    status: 'Draft' | 'Approved';
    concurrencyStamp: string;
};
export type CompanyBudgetVersionItem = {
    id: string;
    name: string;
    concurrencyStamp: string;
    isActive: boolean;
    isPublished: boolean;
    isPartial: boolean;
    instanceCount: number;
    canEdit: boolean;
    canActivate: boolean;
    canDelete?: boolean;
    canEditActiveComposition?: boolean;
};
export type CompanyBudgetActiveState = {
    activeVersionId: string;
    activeConcurrencyStamp: string;
};
export type CompanyBudgetVersionPage = CompanyBudgetActiveState & {
    items: CompanyBudgetVersionItem[];
    skip: number;
    take: number;
    total: number;
    canCreate: boolean;
    scopeLimited: boolean;
};
export type CompanyBudgetVersionOptions = CompanyBudgetActiveState & {
    items: CompanyBudgetInstance[];
    skip: number;
    take: number;
    total: number;
    canCreate: boolean;
    canActivate: boolean;
    costCenters: {
        id: string;
        code: string;
        name: string;
    }[];
};
export type CompanyBudgetOverlap = {
    costCenterId: string;
    costCenterName: string;
    budgetMonth: string;
    budgetInstanceIds: string[];
    winnerBudgetInstanceId: string | null;
    warning: string;
};
export type CompanyBudgetVersion = CompanyBudgetVersionItem & CompanyBudgetActiveState & {
    budgetInstanceIds: string[];
    instances: CompanyBudgetInstance[];
    overlaps: CompanyBudgetOverlap[];
    scopeLimited: boolean;
};
export type CompanyBudgetVersionInput = {
    name: string;
    budgetInstanceIds: string[];
    expectedConcurrencyStamp?: string;
};
export type CompanyBudgetActivationInput = {
    expectedConcurrencyStamp: string;
    expectedActiveConcurrencyStamp: string;
};
export type CompanyBudgetActiveCompositionInput = CompanyBudgetActivationInput & {
    budgetInstanceIds: string[];
};
export type CompanyBudgetDeleteInput = {
    expectedConcurrencyStamp: string;
    replacementVersionId?: string;
    expectedReplacementConcurrencyStamp?: string;
    expectedActiveConcurrencyStamp?: string;
};
export type CompanyBudgetDeleteReceipt = {
    id: string;
    name: string;
    deleted: true;
    retainedInstanceCount: number;
};
export type CompanyBudgetVersionHistory = {
    auditId: string;
    occurredAtUtc: string;
    action: string;
    sourceVersionId: string | null;
    sourceVersionName: string | null;
    previousInstanceCount: number | null;
    instanceCount: number;
};
const invalid = 'Некорректный ответ API версий бюджетов.';
const maximumOverlapMemberReferences = 60000;
const maximumCompanyBudgetResponseBytes = 32 * 1024 * 1024;
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const text = (value: unknown, max: number): value is string => typeof value === 'string' && value.length > 0
    && value.length <= max && value.trim() === value && !/[\u0000-\u001f\u007f-\u009f]/u.test(value);
const integer = (value: unknown, max = Number.MAX_SAFE_INTEGER): value is number => typeof value === 'number'
    && Number.isSafeInteger(value) && value >= 0 && value <= max;
const date = (value: unknown): value is string => typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/u.test(value)
    && !Number.isNaN(Date.parse(`${value}T00:00:00Z`)) && new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value;
const ids = (value: unknown, minimum = 0): value is string[] => Array.isArray(value) && value.length >= minimum && value.length <= 1000
    && value.every(isPublicExpenseRequestUuid) && new Set(value.map(id => id.toLowerCase())).size === value.length;
function active(value: Record<string, unknown>): CompanyBudgetActiveState {
    if (!isPublicExpenseRequestUuid(value.activeVersionId) || !isPublicExpenseRequestUuid(value.activeConcurrencyStamp))
        throw new Error(invalid);
    return { activeVersionId: value.activeVersionId, activeConcurrencyStamp: value.activeConcurrencyStamp };
}
function item(value: unknown): CompanyBudgetVersionItem {
    if (!record(value) || !isPublicExpenseRequestUuid(value.id) || !text(value.name, 200)
        || !isPublicExpenseRequestUuid(value.concurrencyStamp) || typeof value.isActive !== 'boolean'
        || typeof value.isPublished !== 'boolean' || typeof value.isPartial !== 'boolean' || !integer(value.instanceCount, 1000)
        || typeof value.canEdit !== 'boolean' || typeof value.canActivate !== 'boolean'
        || (value.canDelete !== undefined && typeof value.canDelete !== 'boolean')
        || (value.canEditActiveComposition !== undefined && typeof value.canEditActiveComposition !== 'boolean')
        || (value.canEditActiveComposition === true && (!value.isActive || !value.isPublished || value.isPartial))
        || (value.isActive && !value.isPublished) || (value.isPublished && value.canEdit)
        || (value.isPartial && (value.canEdit || value.canActivate))
        || (value.canDelete === true && value.isPartial))
        throw new Error(invalid);
    return { id: value.id, name: value.name, concurrencyStamp: value.concurrencyStamp, isActive: value.isActive,
        isPublished: value.isPublished, isPartial: value.isPartial, instanceCount: value.instanceCount, canEdit: value.canEdit,
        canActivate: value.canActivate, canDelete: value.canDelete === true,
        ...(value.canEditActiveComposition !== undefined ? { canEditActiveComposition: value.canEditActiveComposition === true } : {}) };
}
function instance(value: unknown): CompanyBudgetInstance {
    if (!record(value) || !isPublicExpenseRequestUuid(value.id) || !isPublicExpenseRequestUuid(value.costCenterId)
        || !isPublicExpenseRequestUuid(value.concurrencyStamp) || !text(value.number, 100)
        || !text(value.costCenterCode, 100) || !text(value.costCenterName, 500)
        || !date(value.validFrom) || !date(value.validTo) || value.validFrom > value.validTo
        || (value.status !== 'Draft' && value.status !== 'Approved'))
        throw new Error(invalid);
    return { id: value.id, number: value.number, costCenterId: value.costCenterId,
        costCenterCode: value.costCenterCode, costCenterName: value.costCenterName,
        validFrom: value.validFrom, validTo: value.validTo, status: value.status, concurrencyStamp: value.concurrencyStamp };
}
function page(value: unknown, skip: number, take: number): Record<string, unknown> {
    if (!record(value) || !integer(skip) || !integer(take, 100) || take < 1 || value.skip !== skip || value.take !== take
        || !integer(value.total) || !Array.isArray(value.items) || value.items.length > take
        || value.items.length > Math.max(0, value.total - skip) || typeof value.canCreate !== 'boolean')
        throw new Error(invalid);
    return value;
}
function unique<T extends {
    id: string;
}>(items: T[]): T[] {
    if (new Set(items.map(row => row.id.toLowerCase())).size !== items.length)
        throw new Error(invalid);
    return items;
}
export function publicCompanyBudgetVersionPage(value: unknown, skip: number, take: number): CompanyBudgetVersionPage {
    const raw = page(value, skip, take);
    const state = active(raw);
    const items = unique((raw.items as unknown[]).map(item));
    if (typeof raw.scopeLimited !== 'boolean' || (raw.scopeLimited && (raw.canCreate
        || items.some(row => row.canEdit || row.canActivate || row.canDelete || row.canEditActiveComposition))))
        throw new Error(invalid);
    if (items.some(row => row.isActive !== (row.id.toLowerCase() === state.activeVersionId.toLowerCase())
        || (row.isPartial && !raw.scopeLimited)))
        throw new Error(invalid);
    return { ...state, items, skip, take, total: raw.total as number, canCreate: raw.canCreate as boolean, scopeLimited: raw.scopeLimited };
}
export function publicCompanyBudgetVersionOptions(value: unknown, skip: number, take: number): CompanyBudgetVersionOptions {
    const raw = page(value, skip, take);
    if (typeof raw.canActivate !== 'boolean' || (raw.canActivate && !raw.canCreate)
        || !Array.isArray(raw.costCenters) || raw.costCenters.length > 10000)
        throw new Error(invalid);
    const costCenters = unique(raw.costCenters.map(row => {
        if (!record(row) || !isPublicExpenseRequestUuid(row.id) || !text(row.code, 100) || !text(row.name, 500))
            throw new Error(invalid);
        return { id: row.id, code: row.code, name: row.name };
    }));
    const items = unique((raw.items as unknown[]).map(instance));
    const centersById = new Map(costCenters.map(center => [center.id.toLowerCase(), center]));
    if (items.some(row => {
        const center = centersById.get(row.costCenterId.toLowerCase());
        return !center || center.code !== row.costCenterCode || center.name !== row.costCenterName;
    }))
        throw new Error(invalid);
    return { ...active(raw), items, skip, take, total: raw.total as number, canCreate: raw.canCreate as boolean,
        canActivate: raw.canActivate, costCenters };
}
export function publicCompanyBudgetVersion(value: unknown, requestedId?: string): CompanyBudgetVersion {
    if (!record(value) || typeof value.scopeLimited !== 'boolean' || !ids(value.budgetInstanceIds) || !Array.isArray(value.instances)
        || value.instances.length > 1000 || !Array.isArray(value.overlaps) || value.overlaps.length > 12000)
        throw new Error(invalid);
    // Reject aggregate expansion before parsing instances/warning metadata or performing membership work.
    let overlapMemberReferences = 0;
    for (const row of value.overlaps) {
        if (!record(row) || !Array.isArray(row.budgetInstanceIds) || row.budgetInstanceIds.length < 2
            || row.budgetInstanceIds.length > 1000)
            throw new Error(invalid);
        overlapMemberReferences += row.budgetInstanceIds.length;
        if (overlapMemberReferences > maximumOverlapMemberReferences)
            throw new Error(invalid);
    }
    const base = item(value);
    const state = active(value);
    const instances = unique(value.instances.map(instance));
    const selected = new Set(value.budgetInstanceIds.map(id => id.toLowerCase()));
    const instancesById = new Map(instances.map(row => [row.id.toLowerCase(), row]));
    if ((requestedId && base.id.toLowerCase() !== requestedId.toLowerCase())
        || base.instanceCount !== selected.size || instances.length !== selected.size
        || instances.some(row => !selected.has(row.id.toLowerCase()))
        || base.isActive !== (base.id.toLowerCase() === state.activeVersionId.toLowerCase())
        || (base.isActive && instances.some(row => row.status !== 'Approved'))
        || (base.isPartial && !value.scopeLimited)
        || (value.scopeLimited && (base.canEdit || base.canActivate || base.canDelete || base.canEditActiveComposition)))
        throw new Error(invalid);
    const overlaps = value.overlaps.map(row => {
        if (!record(row) || !isPublicExpenseRequestUuid(row.costCenterId) || !text(row.costCenterName, 500)
            || !date(row.budgetMonth) || !row.budgetMonth.endsWith('-01') || !ids(row.budgetInstanceIds, 2)
            || row.budgetInstanceIds.some(id => !selected.has(id.toLowerCase()))
            || row.budgetInstanceIds.some(id => {
                const source = instancesById.get(id.toLowerCase());
                return !source || source.costCenterId.toLowerCase() !== String(row.costCenterId).toLowerCase()
                    || source.costCenterName !== row.costCenterName || source.validFrom > String(row.budgetMonth)
                    || source.validTo < String(row.budgetMonth);
            })
            || (row.winnerBudgetInstanceId !== null && (!isPublicExpenseRequestUuid(row.winnerBudgetInstanceId)
                || !row.budgetInstanceIds.some(id => id.toLowerCase() === String(row.winnerBudgetInstanceId).toLowerCase())))
            || !text(row.warning, 2000))
            throw new Error(invalid);
        return { costCenterId: row.costCenterId, costCenterName: row.costCenterName, budgetMonth: row.budgetMonth,
            budgetInstanceIds: row.budgetInstanceIds, winnerBudgetInstanceId: row.winnerBudgetInstanceId as string | null, warning: row.warning };
    });
    if (new Set(overlaps.map(row => `${row.costCenterId.toLowerCase()}:${row.budgetMonth}`)).size !== overlaps.length)
        throw new Error(invalid);
    return { ...base, ...state, scopeLimited: value.scopeLimited, budgetInstanceIds: [...value.budgetInstanceIds], instances, overlaps };
}
export class CompanyBudgetVersionApiError extends Error {
    constructor(readonly status: number) { super('Не удалось выполнить запрос версии бюджета.'); }
}
export function companyBudgetVersionErrorMessage(error: unknown): string {
    if (error instanceof CompanyBudgetVersionApiError) {
        if (error.status === 401)
            return 'Для работы с версиями бюджетов требуется авторизация.';
        if (error.status === 403)
            return 'Недостаточно прав для этого действия с версией бюджета.';
        if (error.status === 404)
            return 'Версия бюджета не найдена или недоступна.';
        if (error.status === 409)
            return 'Версия, состав или активный бюджет изменились. Перечитайте текущее состояние.';
        if (error.status === 400)
            return 'Проверьте название, состав и состояние документов. Активировать можно только утверждённые бюджеты.';
    }
    return 'Не удалось подтвердить результат запроса. Повторите попытку.';
}
export const isCompanyBudgetResultUncertain = (error: unknown) => !(error instanceof CompanyBudgetVersionApiError)
    || ![400, 401, 403, 404, 409].includes(error.status);
const route = '/api/v1/budget/company-versions';
async function readCompanyBudgetJson(response: Response): Promise<unknown> {
    const declared = response.headers.get('Content-Length');
    if (declared !== null && (!/^\d+$/u.test(declared) || !Number.isSafeInteger(Number(declared))
        || Number(declared) > maximumCompanyBudgetResponseBytes)) {
        void response.body?.cancel().catch(() => undefined);
        throw new Error(invalid);
    }
    if (!response.body)
        throw new Error(invalid);
    const reader = response.body.getReader();
    try {
        const decoder = new TextDecoder('utf-8', { fatal: true });
        const pieces: string[] = [];
        let bytes = 0;
        let buffered = '';
        for (;;) {
            const chunk = await reader.read();
            if (chunk.done)
                break;
            bytes += chunk.value.byteLength;
            if (bytes > maximumCompanyBudgetResponseBytes) {
                void reader.cancel().catch(() => undefined);
                throw new Error(invalid);
            }
            buffered += decoder.decode(chunk.value, { stream: true });
            if (buffered.length >= 64 * 1024) {
                pieces.push(buffered);
                buffered = '';
            }
        }
        pieces.push(buffered + decoder.decode());
        return JSON.parse(pieces.join('')) as unknown;
    }
    catch {
        void reader.cancel().catch(() => undefined);
        throw new Error(invalid);
    }
    finally {
        reader.releaseLock();
    }
}
async function read(path: string, signal?: AbortSignal): Promise<unknown> {
    const response = await fetchWithSafeTransport(apiUrl(path), { ...secureReadInit(), signal });
    if (response.status !== 200)
        throw new CompanyBudgetVersionApiError(response.status);
    return readCompanyBudgetJson(response);
}
export async function listCompanyBudgetVersions(skip = 0, take = 25, signal?: AbortSignal): Promise<CompanyBudgetVersionPage> {
    if (!integer(skip) || !integer(take, 100) || take < 1)
        throw new CompanyBudgetVersionApiError(400);
    return publicCompanyBudgetVersionPage(await read(`${route}?${new URLSearchParams({ skip: String(skip), take: String(take) })}`, signal), skip, take);
}
export async function getCompanyBudgetVersionOptions(filters: {
    skip?: number;
    take?: number;
    query?: string;
    costCenterId?: string;
} = {}, signal?: AbortSignal): Promise<CompanyBudgetVersionOptions> {
    const { skip = 0, take = 25, query = '', costCenterId } = filters;
    if (!integer(skip) || !integer(take, 100) || take < 1 || (query !== '' && !text(query, 200))
        || (costCenterId !== undefined && !isPublicExpenseRequestUuid(costCenterId)))
        throw new CompanyBudgetVersionApiError(400);
    const params = new URLSearchParams({ skip: String(skip), take: String(take) });
    if (query)
        params.set('query', query);
    if (costCenterId)
        params.set('costCenterId', costCenterId);
    return publicCompanyBudgetVersionOptions(await read(`${route}/options?${params}`, signal), skip, take);
}
export async function getCompanyBudgetVersion(id: string, signal?: AbortSignal): Promise<CompanyBudgetVersion> {
    if (!isPublicExpenseRequestUuid(id))
        throw new CompanyBudgetVersionApiError(400);
    return publicCompanyBudgetVersion(await read(`${route}/${encodeURIComponent(id)}`, signal), id);
}
async function invalidCsrf(response: Response): Promise<boolean> {
    if (response.status !== 403)
        return false;
    try {
        const value = await readCompanyBudgetJson(response.clone());
        return record(value) && value.code === 'csrf-token-invalid';
    }
    catch {
        return false;
    }
}
export async function writeCompanyBudgetVersion(kind: 'create' | 'update' | 'activate' | 'revision' | 'active-composition', id: string | null, input: CompanyBudgetVersionInput | CompanyBudgetActivationInput | CompanyBudgetActiveCompositionInput, key: string): Promise<CompanyBudgetVersion> {
    if (!text(key, 127) || (kind !== 'create' && !isPublicExpenseRequestUuid(id)))
        throw new CompanyBudgetVersionApiError(400);
    let body: CompanyBudgetVersionInput | CompanyBudgetActivationInput | CompanyBudgetActiveCompositionInput;
    if (kind === 'activate' || kind === 'active-composition') {
        const command = input as CompanyBudgetActiveCompositionInput;
        if (!isPublicExpenseRequestUuid(command.expectedConcurrencyStamp) || !isPublicExpenseRequestUuid(command.expectedActiveConcurrencyStamp))
            throw new CompanyBudgetVersionApiError(400);
        body = { expectedConcurrencyStamp: command.expectedConcurrencyStamp, expectedActiveConcurrencyStamp: command.expectedActiveConcurrencyStamp };
        if (kind === 'active-composition') {
            if (!ids(command.budgetInstanceIds, 1))
                throw new CompanyBudgetVersionApiError(400);
            body = { ...body, budgetInstanceIds: [...command.budgetInstanceIds] };
        }
    }
    else {
        const command = input as CompanyBudgetVersionInput;
        if (!text(command.name, 200) || !ids(command.budgetInstanceIds, 1)
            || ((kind === 'update' || kind === 'revision') && !isPublicExpenseRequestUuid(command.expectedConcurrencyStamp)))
            throw new CompanyBudgetVersionApiError(400);
        body = { name: command.name, budgetInstanceIds: [...command.budgetInstanceIds],
            ...(kind === 'update' || kind === 'revision' ? { expectedConcurrencyStamp: command.expectedConcurrencyStamp } : {}) };
    }
    const bodyText = JSON.stringify(body);
    const path = kind === 'create' ? route : `${route}/${encodeURIComponent(id!)}${kind === 'activate' ? '/activate' : kind === 'revision' ? '/revisions' : kind === 'active-composition' ? '/active-composition' : ''}`;
    const response = await fetchWithOneTimeRefresh(() => fetchWithSafeTransport(apiUrl(path), secureWriteInit(kind === 'update' || kind === 'active-composition' ? 'PUT' : 'POST', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, bodyText)), invalidCsrf, getCurrentSession);
    if (kind === 'revision' ? response.status !== 201 : response.status !== 200 && !(kind === 'create' && response.status === 201))
        throw new CompanyBudgetVersionApiError(response.status);
    const result = publicCompanyBudgetVersion(await readCompanyBudgetJson(response), kind === 'revision' ? undefined : id ?? undefined);
    if (kind === 'revision' && (result.id.toLowerCase() === id!.toLowerCase() || result.isActive || result.isPublished
        || result.isPartial || result.scopeLimited || !result.canEdit))
        throw new Error(invalid);
    if (kind === 'active-composition') {
        const command = body as CompanyBudgetActiveCompositionInput;
        if (!result.isActive || !result.isPublished || !result.canEditActiveComposition || result.isPartial || result.scopeLimited
            || result.concurrencyStamp.toLowerCase() === command.expectedConcurrencyStamp.toLowerCase()
            || result.activeConcurrencyStamp.toLowerCase() === command.expectedActiveConcurrencyStamp.toLowerCase()
            || result.budgetInstanceIds.length !== command.budgetInstanceIds.length
            || result.budgetInstanceIds.some(value => !command.budgetInstanceIds.some(selected => selected.toLowerCase() === value.toLowerCase())))
            throw new Error(invalid);
        return result;
    }
    if (kind === 'activate' ? !result.isActive : result.name !== (body as CompanyBudgetVersionInput).name
        || result.budgetInstanceIds.length !== (body as CompanyBudgetVersionInput).budgetInstanceIds.length
        || result.budgetInstanceIds.some(value => !(body as CompanyBudgetVersionInput).budgetInstanceIds.some(selected => selected.toLowerCase() === value.toLowerCase())))
        throw new Error(invalid);
    return result;
}
export async function getCompanyBudgetVersionHistory(id: string, skip = 0, take = 25, signal?: AbortSignal): Promise<CompanyBudgetVersionHistory[]> {
    if (!isPublicExpenseRequestUuid(id) || !integer(skip) || !integer(take, 100) || take < 1)
        throw new CompanyBudgetVersionApiError(400);
    const value = await read(`${route}/${encodeURIComponent(id)}/history?${new URLSearchParams({ skip: String(skip), take: String(take) })}`, signal);
    if (!Array.isArray(value) || value.length > take)
        throw new Error(invalid);
    const seen = new Set<string>();
    return value.map(row => {
        if (!record(row) || !isPublicExpenseRequestUuid(row.auditId) || seen.has(row.auditId.toLowerCase())
            || typeof row.occurredAtUtc !== 'string' || !/^\d{4}-\d{2}-\d{2}T.+(?:Z|\+00:00)$/u.test(row.occurredAtUtc)
            || Number.isNaN(Date.parse(row.occurredAtUtc)) || !text(row.action, 100) || !integer(row.instanceCount, 1000)
            || (row.previousInstanceCount !== null && !integer(row.previousInstanceCount, 1000))
            || (row.sourceVersionId !== null && (!isPublicExpenseRequestUuid(row.sourceVersionId) || row.sourceVersionId.toLowerCase() === id.toLowerCase()))
            || (row.sourceVersionId === null ? row.sourceVersionName !== null : !text(row.sourceVersionName, 200))
            || (row.action === 'CompanyBudgetVersionRevisionCreated' && row.sourceVersionId === null))
            throw new Error(invalid);
        seen.add(row.auditId.toLowerCase());
        return { auditId: row.auditId, occurredAtUtc: row.occurredAtUtc, action: row.action,
            sourceVersionId: row.sourceVersionId as string | null, sourceVersionName: row.sourceVersionName as string | null,
            previousInstanceCount: row.previousInstanceCount as number | null, instanceCount: row.instanceCount };
    });
}
export function publicCompanyBudgetDeleteReceipt(value: unknown, requestedId: string, expected?: Pick<CompanyBudgetDeleteReceipt, 'name' | 'retainedInstanceCount'>): CompanyBudgetDeleteReceipt {
    if (!record(value) || !isPublicExpenseRequestUuid(value.id) || value.id.toLowerCase() !== requestedId.toLowerCase()
        || !text(value.name, 200) || value.deleted !== true || !integer(value.retainedInstanceCount, 1000)
        || (expected && (value.name !== expected.name || value.retainedInstanceCount !== expected.retainedInstanceCount))) {
        throw new Error(invalid);
    }
    return { id: value.id, name: value.name, deleted: true, retainedInstanceCount: value.retainedInstanceCount };
}
export async function deleteCompanyBudgetVersion(id: string, input: CompanyBudgetDeleteInput, key: string, expected?: Pick<CompanyBudgetDeleteReceipt, 'name' | 'retainedInstanceCount'>): Promise<CompanyBudgetDeleteReceipt> {
    if (!isPublicExpenseRequestUuid(id) || !isPublicExpenseRequestUuid(input.expectedConcurrencyStamp)
        || !text(key, 127) || (expected && (!text(expected.name, 200) || !integer(expected.retainedInstanceCount, 1000)))) {
        throw new CompanyBudgetVersionApiError(400);
    }
    // Project and freeze bytes before awaiting: a retry must send exactly the same CAS command and key.
    const replacementFields = [input.replacementVersionId, input.expectedReplacementConcurrencyStamp, input.expectedActiveConcurrencyStamp];
    if (replacementFields.some(value => value !== undefined)
        && (replacementFields.some(value => !isPublicExpenseRequestUuid(value)) || sameVersion(input.replacementVersionId!, id))) {
        throw new CompanyBudgetVersionApiError(400);
    }
    const bodyText = JSON.stringify({ expectedConcurrencyStamp: input.expectedConcurrencyStamp,
        ...(input.replacementVersionId ? { replacementVersionId: input.replacementVersionId,
            expectedReplacementConcurrencyStamp: input.expectedReplacementConcurrencyStamp,
            expectedActiveConcurrencyStamp: input.expectedActiveConcurrencyStamp } : {}) });
    const expectedReceipt = expected ? { ...expected } : undefined;
    const response = await fetchWithOneTimeRefresh(() => fetchWithSafeTransport(apiUrl(`${route}/${encodeURIComponent(id)}`), secureWriteInit('DELETE', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, bodyText)), invalidCsrf, getCurrentSession);
    if (response.status !== 200)
        throw new CompanyBudgetVersionApiError(response.status);
    return publicCompanyBudgetDeleteReceipt(await readCompanyBudgetJson(response), id, expectedReceipt);
}
const sameVersion = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();
