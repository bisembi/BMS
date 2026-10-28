import { apiUrl, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
import { isPublicExpenseRequestText, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
import { CASH_FLOW_ACTIVITY_TYPES, type CashFlowActivityType, type ReferenceDataItem } from '../types';
export type CashFlowAccrualSettings = {
    id: string;
    code: string;
    name: string;
    isActive: boolean;
    isGroup: boolean;
    formsManagerialAccrual: boolean;
    expenseItem: ReferenceDataItem | null;
    version: string;
    canEdit: boolean;
    costCenter?: ReferenceDataItem | null;
    activityType?: CashFlowActivityType | null;
    groupName?: string | null;
    flowDirection?: 'Payment' | 'Receipt' | null;
};
export type CashFlowAccrualCommand = {
    expectedVersion: string;
    formsManagerialAccrual: boolean;
    expenseItemId: string | null;
    costCenterId?: string | null;
    costCenterIdSpecified?: boolean;
    activityType?: CashFlowActivityType | null;
    groupName?: string | null;
    classificationSpecified?: boolean;
    name?: string | null;
    nameSpecified?: boolean;
    flowDirection?: 'Payment' | 'Receipt' | null;
    flowDirectionSpecified?: boolean;
};
class SettingsApiError extends Error {
    constructor(readonly status: number) { super('Не удалось выполнить запрос настроек начисления.'); }
}
function rejectSettingsResponse(response: Response): never {
    // Start consuming an error response instead of leaving its fetch stream
    // unread. Neither its bytes nor a body-read failure become UI diagnostics;
    // the known HTTP status is returned immediately, even if the body stalls.
    void response.arrayBuffer().catch(() => undefined);
    throw new SettingsApiError(response.status);
}
const invalidResponse = 'Не удалось получить корректные настройки начисления. Повторите загрузку.';
export const uncertainSettingsMessage = 'Результат сохранения не подтверждён. Повторите ту же команду или загрузите текущее состояние.';
export function cashFlowAccrualErrorMessage(error: unknown): string {
    if (!(error instanceof SettingsApiError))
        return uncertainSettingsMessage;
    if (error.status === 400)
        return 'Проверьте название статьи, настройку начисления и выбранную статью затрат.';
    if (error.status === 401)
        return 'Для работы с настройками требуется авторизация.';
    if (error.status === 403)
        return 'Недостаточно прав для изменения настройки начисления.';
    if (error.status === 404)
        return 'Статья не найдена либо недоступна.';
    if (error.status === 409)
        return 'Настройка уже изменена. Ваш ввод сохранён; загрузите текущее состояние перед новой правкой.';
    return uncertainSettingsMessage;
}
export function isCashFlowAccrualResultUncertain(error: unknown): boolean {
    return !(error instanceof SettingsApiError) || ![400, 401, 403, 404, 409].includes(error.status);
}
export function cashFlowAccrualReadErrorMessage(error: unknown): string {
    return error instanceof SettingsApiError && [401, 403, 404].includes(error.status)
        ? cashFlowAccrualErrorMessage(error) : invalidResponse;
}
function record(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function text(value: unknown, limit: number): value is string {
    return isPublicExpenseRequestText(value, limit) && value.trim() === value && value.length > 0;
}
function sameId(a: string, b: string) { return a.toLowerCase() === b.toLowerCase(); }
export function publicCashFlowAccrualSettings(value: unknown, id: string): CashFlowAccrualSettings {
    if (record(value) && value.flowDirection !== undefined && value.flowDirection !== null
        && value.flowDirection !== 'Payment' && value.flowDirection !== 'Receipt')
        throw new Error(invalidResponse);
    if (!record(value) || !isPublicExpenseRequestUuid(value.id) || !sameId(value.id, id)
        || !isPublicExpenseRequestUuid(value.version) || !text(value.code, 20) || !text(value.name, 150)
        || typeof value.isActive !== 'boolean' || typeof value.isGroup !== 'boolean'
        || typeof value.canEdit !== 'boolean' || typeof value.formsManagerialAccrual !== 'boolean'
        || (value.isGroup && value.canEdit))
        throw new Error(invalidResponse);
    let expenseItem: ReferenceDataItem | null = null;
    if (value.expenseItem !== null) {
        const item = value.expenseItem;
        if (!record(item) || !isPublicExpenseRequestUuid(item.id) || !text(item.code, 9) || !text(item.name, 250)
            || (item.parentId !== null && item.parentId !== undefined))
            throw new Error(invalidResponse);
        expenseItem = { id: item.id, code: item.code, name: item.name };
    }
    if (value.formsManagerialAccrual !== (expenseItem !== null))
        throw new Error(invalidResponse);
    let costCenter: ReferenceDataItem | null = null;
    if (value.costCenter != null) {
        const center = value.costCenter;
        if (!record(center) || !isPublicExpenseRequestUuid(center.id) || !text(center.code, 100)
            || !text(center.name, 500) || value.isGroup)
            throw new Error(invalidResponse);
        costCenter = { id: center.id, code: center.code, name: center.name };
    }
    if (value.activityType != null && !CASH_FLOW_ACTIVITY_TYPES.some(option => option.value === value.activityType))
        throw new Error(invalidResponse);
    if (value.groupName != null && !text(value.groupName, 150))
        throw new Error(invalidResponse);
    return { id: value.id, code: value.code, name: value.name, isActive: value.isActive, isGroup: value.isGroup,
        canEdit: value.canEdit, version: value.version, formsManagerialAccrual: value.formsManagerialAccrual, expenseItem,
        ...(value.costCenter === undefined ? {} : { costCenter }),
        ...(value.activityType === undefined ? {} : { activityType: value.activityType as CashFlowActivityType | null }),
        ...(value.groupName === undefined ? {} : { groupName: value.groupName as string | null }),
        ...(value.flowDirection === undefined ? {} : { flowDirection: value.flowDirection as 'Payment' | 'Receipt' | null }) };
}
function path(id: string) {
    if (!isPublicExpenseRequestUuid(id))
        throw new SettingsApiError(400);
    return `/api/v1/reference-data/cash-flow-items/${encodeURIComponent(id)}/accrual-settings`;
}
export async function getCashFlowAccrualSettings(id: string, signal?: AbortSignal): Promise<CashFlowAccrualSettings> {
    const url = path(id);
    const response = await fetchWithSafeTransport(apiUrl(url), { ...secureReadInit(), signal });
    if (!response.ok)
        rejectSettingsResponse(response);
    return publicCashFlowAccrualSettings(await response.json(), id);
}
export async function updateCashFlowAccrualSettings(id: string, command: CashFlowAccrualCommand, key: string, signal?: AbortSignal): Promise<CashFlowAccrualSettings> {
    const url = path(id);
    if (!command || !isPublicExpenseRequestUuid(command.expectedVersion)
        || typeof command.formsManagerialAccrual !== 'boolean'
        || (command.formsManagerialAccrual ? !isPublicExpenseRequestUuid(command.expenseItemId) : command.expenseItemId !== null)
        || (command.costCenterIdSpecified !== undefined && typeof command.costCenterIdSpecified !== 'boolean')
        || (command.costCenterIdSpecified === true && command.costCenterId !== null && !isPublicExpenseRequestUuid(command.costCenterId))
        || (command.costCenterId != null && command.costCenterIdSpecified !== true)
        || (command.classificationSpecified !== undefined && typeof command.classificationSpecified !== 'boolean')
        || (command.nameSpecified !== undefined && typeof command.nameSpecified !== 'boolean')
        || (command.flowDirectionSpecified !== undefined && typeof command.flowDirectionSpecified !== 'boolean')
        || (command.flowDirectionSpecified === true
            ? command.flowDirection !== null && command.flowDirection !== 'Payment' && command.flowDirection !== 'Receipt'
            : command.flowDirection != null)
        || (command.nameSpecified === true ? !text(command.name, 150) : command.name != null)
        || (command.classificationSpecified === true
            ? !CASH_FLOW_ACTIVITY_TYPES.some(option => option.value === command.activityType)
                || (command.groupName !== null && !text(command.groupName, 150))
            : command.activityType != null || command.groupName != null)
        || typeof key !== 'string' || !text(key, 127))
        throw new SettingsApiError(400);
    // A caller's extra properties never cross the wire.
    const body = { expectedVersion: command.expectedVersion,
        formsManagerialAccrual: command.formsManagerialAccrual, expenseItemId: command.expenseItemId,
        ...(command.flowDirectionSpecified === true ? { flowDirection: command.flowDirection, flowDirectionSpecified: true } : {}),
        ...(command.costCenterIdSpecified === true ? { costCenterId: command.costCenterId, costCenterIdSpecified: true } : {}),
        ...(command.nameSpecified === true ? { name: command.name, nameSpecified: true } : {}),
        ...(command.classificationSpecified === true ? { activityType: command.activityType,
            groupName: command.groupName, classificationSpecified: true } : {}) };
    const response = await fetchWithSafeTransport(apiUrl(url), { ...secureWriteInit('PUT', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, JSON.stringify(body)), signal });
    if (!response.ok)
        rejectSettingsResponse(response);
    const result = publicCashFlowAccrualSettings(await response.json(), id);
    if (!result.canEdit || result.formsManagerialAccrual !== command.formsManagerialAccrual
        || (command.nameSpecified === true && result.name !== command.name)
        || (command.flowDirectionSpecified === true && (result.flowDirection === undefined || result.flowDirection !== command.flowDirection))
        || (result.expenseItem?.id.toLowerCase() ?? null) !== (command.expenseItemId?.toLowerCase() ?? null)
        || (command.costCenterIdSpecified === true
            && (result.costCenter?.id.toLowerCase() ?? null) !== (command.costCenterId?.toLowerCase() ?? null))
        || (command.classificationSpecified === true && (result.activityType !== command.activityType
            || result.groupName === undefined
            || (result.groupName?.toLocaleLowerCase() ?? null) !== (command.groupName?.toLocaleLowerCase() ?? null)))) {
        throw new Error(invalidResponse);
    }
    return result;
}
