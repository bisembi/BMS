/// Read-only reference directories. Every catalogue is projected down to the
/// public flat shape before it reaches the caller, so server-side fields that
/// are not part of the directory contract never leave this module.
import { apiUrl, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
import type { CounterpartyResidencyCode, PaymentBasisCode, ReferenceCodeCatalog, ReferenceDataCatalog, ReferenceDataItem, ReferenceDataPage, CashFlowActivityType } from '../types';
import { CASH_FLOW_ACTIVITY_TYPES } from '../types';
const publicGuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
const emptyPublicGuid = '00000000-0000-0000-0000-000000000000';
// 50 UUIDs produce a 2,049-byte query string, exceeding IIS's default
// 2,048-byte limit before the request reaches the API. Keep bounded GETs
// below that limit without relaxing proxy limits or catalogue validation.
const cashFlowItemResolveBatchSize = 40;
const maximumCashFlowItemSelections = 10000;
const unsafePublicTextPattern = /[\u0000-\u001f\u007f-\u009f]/u;
const paymentBasisCodes: ReadonlySet<PaymentBasisCode> = new Set([
    'Contract',
    'InvoiceWithoutContract',
    'PublicOffer',
    'OtherBasis',
]);
const counterpartyResidencyCodes: ReadonlySet<CounterpartyResidencyCode> = new Set([
    'ResidentKz',
    'NonResident',
]);
const invalidCostCenterAsOfMessage = 'Некорректная дата среза ЦФО.';
export type CreateExpenseItemInput = {
    name: string;
};
export type UpdateOrganizationInput = {
    code: string;
    name: string;
};
export type CreateOrganizationInput = UpdateOrganizationInput;
export type ReferenceDataMaintenanceInput = {
    flowDirection?: 'Payment' | 'Receipt' | null;
    flowDirectionSpecified?: boolean;
    /** All directories except currencies expose a user-entered code. */
    code?: string;
    name: string;
    /** Counterparties expose their public BIN; the internal code is generated. */
    bin?: string | null;
    /** Cash-flow items accept a group name; the server resolves its internal parent. */
    parentName?: string | null;
    /** Cash-flow items are assigned one of the three fixed activity types. */
    activityType?: CashFlowActivityType | null;
    parentId?: string | null;
    /** Distinguishes an explicit "Без группы" selection from an omitted field. */
    parentIdSpecified?: boolean;
    isGroup?: boolean;
    counterpartyId?: string;
    organizationId?: string;
    // W58_APPROVED_R14_CURRENCY_FIELDS_BEGIN
    contractDate?: string;
    currencyId?: string;
    numericCode?: string;
};
export type ReferenceDataMaintenanceDirectory = 'organizations' | 'cost-centers' | 'cash-flow-items' | 'expense-items' | 'counterparties' | 'contracts' | 'currencies';
class ExpenseItemDirectoryApiError extends Error {
    constructor(readonly status: number) {
        super('Не удалось выполнить запрос справочника статей затрат.');
    }
}
class OrganizationDirectoryApiError extends Error {
    constructor(readonly status: number, readonly code?: ReferenceDataMaintenanceErrorCode) {
        super('Не удалось изменить организацию.');
    }
}
type ReferenceDataMaintenanceErrorCode = 'csrf-token-invalid';
import { ReferenceDataMaintenanceApiError } from './referenceDataMaintenanceErrors';
function isSafePublicText(value: string, maximumLength: number): boolean {
    return value.length > 0
        && value.length <= maximumLength
        && value.trim() === value
        && !unsafePublicTextPattern.test(value);
}
const cashFlowActivityTypeValues: ReadonlySet<string> = new Set(CASH_FLOW_ACTIVITY_TYPES.map(option => option.value));
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function publicMaintenanceErrorCode(value: unknown): ReferenceDataMaintenanceErrorCode | undefined {
    return isRecord(value) && value.code === 'csrf-token-invalid'
        ? 'csrf-token-invalid'
        : undefined;
}
function isReferenceGuid(value: string): boolean {
    return publicGuidPattern.test(value)
        && value.toLocaleLowerCase('en-US') !== emptyPublicGuid;
}
function assertCostCenterAsOf(asOf: unknown): asserts asOf is string | undefined {
    if (asOf === undefined)
        return;
    if (typeof asOf !== 'string' || !/^\d{4}-\d{2}-\d{2}$/u.test(asOf)) {
        throw new Error(invalidCostCenterAsOfMessage);
    }
    const parsed = new Date(`${asOf}T00:00:00Z`);
    if (Number.isNaN(parsed.valueOf()) || parsed.toISOString().slice(0, 10) !== asOf) {
        throw new Error(invalidCostCenterAsOfMessage);
    }
}
function publicReferenceDataCatalog(payload: unknown): ReferenceDataCatalog {
    if (!isRecord(payload)) {
        throw new Error('Некорректный ответ API справочников.');
    }
    const source = payload;
    if (typeof source.catalog !== 'string'
        || !isSafePublicText(source.catalog, 64)
        || typeof source.source !== 'string'
        || !isSafePublicText(source.source, 64)
        || (source.refreshedAtUtc !== null && typeof source.refreshedAtUtc !== 'string')
        || (typeof source.refreshedAtUtc === 'string'
            && (source.refreshedAtUtc.length > 64 || Number.isNaN(Date.parse(source.refreshedAtUtc))))
        || !Array.isArray(source.items)) {
        throw new Error('Некорректный ответ API справочников.');
    }
    const itemIds = new Set<string>();
    const items = source.items.map((value): ReferenceDataItem => {
        if (!isRecord(value)) {
            throw new Error('Некорректный ответ API справочников.');
        }
        const item = value;
        if (item.displayOrder != null && (typeof item.displayOrder !== 'number'
            || !Number.isSafeInteger(item.displayOrder) || item.displayOrder < 0 || item.displayOrder > 2147483647))
            throw new Error('Некорректный порядок статьи ДДС.');
        if (item.flowDirection !== undefined && item.flowDirection !== null
            && item.flowDirection !== 'Payment' && item.flowDirection !== 'Receipt') {
            throw new Error('Некорректное направление статьи ДДС.');
        }
        if (typeof item.id !== 'string'
            || !publicGuidPattern.test(item.id)
            || item.id.toLocaleLowerCase('en-US') === emptyPublicGuid
            || itemIds.has(item.id.toLocaleLowerCase('en-US'))
            || typeof item.code !== 'string'
            || !isSafePublicText(item.code, 100)
            || typeof item.name !== 'string'
            || !isSafePublicText(item.name, 500)
            || (item.parentId !== undefined && item.parentId !== null
                && (typeof item.parentId !== 'string'
                    || !publicGuidPattern.test(item.parentId)
                    || item.parentId.toLocaleLowerCase('en-US') === emptyPublicGuid
                    || item.parentId.toLocaleLowerCase('en-US') === item.id.toLocaleLowerCase('en-US')))) {
            throw new Error('Некорректный ответ API справочников.');
        }
        if (item.bin !== undefined && item.bin !== null
            && (typeof item.bin !== 'string' || !/^\d{12}$/u.test(item.bin))) {
            throw new Error('Некорректный ответ API справочников.');
        }
        if (item.sourceNames !== undefined && (!Array.isArray(item.sourceNames) || item.sourceNames.length > 3
            || item.sourceNames.some(name => !['Организация A', 'Организация B', 'Организация C'].includes(name))
            || new Set(item.sourceNames).size !== item.sourceNames.length)) {
            throw new Error('Некорректный источник контрагента.');
        }
        // W58_APPROVED_R14_CURRENCY_FIELDS_BEGIN
        if (item.numericCode !== undefined && item.numericCode !== null
            && (typeof item.numericCode !== 'string' || !/^\d{3}$/u.test(item.numericCode))) {
            throw new Error('Некорректный ответ API справочников.');
        }
        // W58_APPROVED_R14_CURRENCY_FIELDS_END
        if (item.currencyCode != null && (typeof item.currencyCode !== 'string' || !/^[A-Z]{3}$/u.test(item.currencyCode)))
            throw new Error('Некорректный ответ API справочников.');
        if (item.activityType !== undefined && item.activityType !== null
            && (typeof item.activityType !== 'string' || !cashFlowActivityTypeValues.has(item.activityType))) {
            throw new Error('Некорректный ответ API справочников.');
        }
        if (item.parentName != null && (typeof item.parentName !== 'string'
            || !isSafePublicText(item.parentName, 500) || !item.parentId))
            throw new Error('Некорректный ответ API справочников.');
        if (item.groupingName != null && (typeof item.groupingName !== 'string' || !isSafePublicText(item.groupingName, 500)))
            throw new Error('Некорректный ответ API справочников.');
        if (item.ownerNames !== undefined && (!Array.isArray(item.ownerNames)
            || item.ownerNames.some(name => typeof name !== 'string' || !isSafePublicText(name, 150))))
            throw new Error('Некорректный ответ API справочников.');
        itemIds.add(item.id.toLocaleLowerCase('en-US'));
        if (item.costCenterId != null && (typeof item.costCenterId !== 'string'
            || !publicGuidPattern.test(item.costCenterId) || item.costCenterId.toLowerCase() === emptyPublicGuid))
            throw new Error('Некорректный ответ API справочников.');
        if (item.costCenterName != null && (typeof item.costCenterName !== 'string'
            || !isSafePublicText(item.costCenterName, 500)))
            throw new Error('Некорректный ответ API справочников.');
        let relatedCashFlowItems: ReferenceDataItem['relatedCashFlowItems'];
        if (item.relatedCashFlowItems !== undefined) {
            if (source.catalog !== 'expense-items' || !Array.isArray(item.relatedCashFlowItems))
                throw new Error('Некорректные связи статьи затрат с ДДС.');
            const codes = new Set<string>();
            relatedCashFlowItems = item.relatedCashFlowItems.map(value => {
                if (!isRecord(value) || typeof value.code !== 'string' || !isSafePublicText(value.code, 100)
                    || typeof value.name !== 'string' || !isSafePublicText(value.name, 500)
                    || codes.has(value.code))
                    throw new Error('Некорректные связи статьи затрат с ДДС.');
                codes.add(value.code);
                // Only public labels survive decoding; no internal mapping IDs or settings.
                return { code: value.code, name: value.name };
            });
        }
        return {
            id: item.id,
            code: item.code,
            name: item.name,
            ...(relatedCashFlowItems === undefined ? {} : { relatedCashFlowItems }),
            ...(item.currencyCode === undefined ? {} : { currencyCode: item.currencyCode as string | null }),
            ...(item.displayOrder === undefined ? {} : { displayOrder: item.displayOrder as number | null }),
            ...(item.flowDirection === undefined ? {} : { flowDirection: item.flowDirection as 'Payment' | 'Receipt' | null }),
            ...(item.bin === undefined ? {} : { bin: item.bin }),
            ...(item.sourceNames === undefined ? {} : { sourceNames: item.sourceNames as string[] }),
            // W58_APPROVED_R14_CURRENCY_FIELDS_BEGIN
            ...(item.numericCode === undefined ? {} : { numericCode: item.numericCode }),
            // W58_APPROVED_R14_CURRENCY_FIELDS_END
            ...(item.parentId === undefined ? {} : { parentId: item.parentId }),
            ...(item.parentName === undefined ? {} : { parentName: item.parentName as string | null }),
            ...(item.groupingName === undefined ? {} : { groupingName: item.groupingName as string | null }),
            ...(item.ownerNames === undefined ? {} : { ownerNames: item.ownerNames as string[] }),
            ...(item.activityType === undefined ? {} : { activityType: item.activityType as CashFlowActivityType | null }),
            ...(item.costCenterId === undefined ? {} : { costCenterId: item.costCenterId as string | null }),
            ...(item.costCenterName === undefined ? {} : { costCenterName: item.costCenterName as string | null }),
        };
    });
    return {
        catalog: source.catalog,
        source: source.source,
        refreshedAtUtc: source.refreshedAtUtc,
        items,
    };
}
function publicOrganizationResponse(value: unknown): ReferenceDataItem {
    const item = publicReferenceDataCatalog({
        catalog: 'organizations',
        source: 'bms_web',
        refreshedAtUtc: null,
        items: [value],
    }).items[0];
    if (item === undefined)
        throw new Error('Некорректный ответ API справочника организаций.');
    return item;
}
async function getReferenceData(path: string, signal?: AbortSignal): Promise<ReferenceDataCatalog> {
    const response = await fetchWithSafeTransport(apiUrl(path), { ...secureReadInit(), ...(signal ? { signal } : {}) });
    if (!response.ok) {
        throw new Error(`Не удалось загрузить справочник (HTTP ${response.status}).`);
    }
    return publicReferenceDataCatalog(await response.json());
}
function publicReferenceCodeCatalog(payload: unknown, expectedCatalog: string, allowedCodes: ReadonlySet<string>, expectedSource: 'fixed' | 'development-mock' = 'fixed'): ReferenceCodeCatalog {
    if (!isRecord(payload)) {
        throw new Error('Некорректный ответ API справочников.');
    }
    const source = payload;
    if (source.catalog !== expectedCatalog
        || source.source !== expectedSource
        || source.refreshedAtUtc !== null
        || !Array.isArray(source.items)) {
        throw new Error('Некорректный ответ API справочников.');
    }
    const codes = new Set<string>();
    const items = source.items.map((value): ReferenceCodeCatalog['items'][number] => {
        if (!isRecord(value)) {
            throw new Error('Некорректный ответ API справочников.');
        }
        const item = value;
        const propertyNames = Object.keys(item).sort();
        if (propertyNames.length !== 2
            || propertyNames[0] !== 'code'
            || propertyNames[1] !== 'name'
            || typeof item.code !== 'string'
            || !allowedCodes.has(item.code)
            || codes.has(item.code)
            || typeof item.name !== 'string'
            || !isSafePublicText(item.name, 500)) {
            throw new Error('Некорректный ответ API справочников.');
        }
        codes.add(item.code);
        return { code: item.code, name: item.name };
    });
    return {
        catalog: expectedCatalog,
        source: expectedSource,
        refreshedAtUtc: null,
        items,
    };
}
async function getReferenceCodeData(path: string, expectedCatalog: string, allowedCodes: ReadonlySet<string>): Promise<ReferenceCodeCatalog> {
    const response = await fetchWithSafeTransport(apiUrl(path), secureReadInit());
    if (!response.ok) {
        throw new Error(`Не удалось загрузить справочник (HTTP ${response.status}).`);
    }
    return publicReferenceCodeCatalog(await response.json(), expectedCatalog, allowedCodes);
}
export async function getOrganizations(): Promise<ReferenceDataCatalog> {
    const catalog = await getReferenceData('/api/v1/reference-data/organizations');
    if (catalog.catalog !== 'organizations'
        || catalog.source !== 'bms_web'
        || catalog.refreshedAtUtc !== null) {
        throw new Error('Не удалось загрузить организации.');
    }
    return catalog;
}
export function organizationUpdateErrorMessage(error: unknown): string {
    if (!(error instanceof OrganizationDirectoryApiError)) {
        return 'Результат сохранения организации не подтверждён. Повторите попытку.';
    }
    if (error.code === 'csrf-token-invalid') {
        return 'Для изменения справочника нужен защищённый HTTPS-сеанс. Откройте HTTPS-адрес вашей установки BMS, обновите страницу и повторите операцию.';
    }
    if (error.status === 400)
        return 'Укажите код и наименование организации.';
    if (error.status === 401)
        return 'Требуется авторизация.';
    if (error.status === 403) {
        return 'Изменение организаций доступно финансовому контролёру, финансовому директору, генеральному директору и системному администратору.';
    }
    if (error.status === 404)
        return 'Организация не найдена. Обновите справочник.';
    if (error.status === 409)
        return 'Организация с таким кодом уже существует.';
    return 'Результат сохранения организации не подтверждён. Повторите попытку.';
}
export function organizationCreateErrorMessage(error: unknown): string {
    if (!(error instanceof OrganizationDirectoryApiError)) {
        return 'Результат создания организации не подтверждён. Повторите ту же попытку.';
    }
    if (error.code === 'csrf-token-invalid') {
        return 'Для изменения справочника нужен защищённый HTTPS-сеанс. Откройте HTTPS-адрес вашей установки BMS, обновите страницу и повторите операцию.';
    }
    if (error.status === 400)
        return 'Укажите код длиной до 100 символов и наименование до 500 символов.';
    if (error.status === 401)
        return 'Требуется авторизация.';
    if (error.status === 403) {
        return 'Добавление организаций доступно финансовому контролёру, финансовому директору, генеральному директору и системному администратору.';
    }
    if (error.status === 409)
        return 'Организация с таким кодом уже существует.';
    return 'Результат создания организации не подтверждён. Повторите ту же попытку.';
}
export function isOrganizationCreateUncertain(error: unknown): boolean {
    return !(error instanceof OrganizationDirectoryApiError)
        || ![400, 401, 403, 409].includes(error.status);
}
export async function createOrganization(input: CreateOrganizationInput, key: string, signal?: AbortSignal): Promise<ReferenceDataItem> {
    if (!input
        || !isSafePublicText(input.code, 100)
        || !isSafePublicText(input.name, 500)
        || !isSafePublicText(key, 127)) {
        throw new OrganizationDirectoryApiError(400);
    }
    const body = { code: input.code.trim(), name: input.name.trim() };
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/reference-data/organizations'), {
        ...secureWriteInit('POST', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, JSON.stringify(body)),
        signal,
    });
    if (response.status !== 200) {
        let code: ReferenceDataMaintenanceErrorCode | undefined;
        try {
            code = publicMaintenanceErrorCode(await response.json());
        }
        catch {
            // Keep the public error fixed when an intermediary returns an empty or
            // non-JSON body; diagnostics must never reach the UI.
        }
        throw new OrganizationDirectoryApiError(response.status, code);
    }
    try {
        const item = publicOrganizationResponse(await response.json());
        if (item === undefined || item.code !== body.code || item.name !== body.name
            || item.parentId !== undefined && item.parentId !== null) {
            throw new Error('Unexpected organization response.');
        }
        return item;
    }
    catch (error) {
        if (error instanceof Error && error.name === 'AbortError')
            throw error;
        throw new Error('Не удалось получить подтверждение создания организации.');
    }
}
export async function updateOrganization(id: string, input: UpdateOrganizationInput, signal?: AbortSignal): Promise<ReferenceDataItem> {
    if (!isReferenceGuid(id)
        || !input
        || !isSafePublicText(input.code, 100)
        || !isSafePublicText(input.name, 500)) {
        throw new OrganizationDirectoryApiError(400);
    }
    const body = { code: input.code.trim(), name: input.name.trim() };
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/reference-data/organizations/${encodeURIComponent(id)}`), {
        ...secureWriteInit('PUT', { 'Content-Type': 'application/json' }, JSON.stringify(body)),
        signal,
    });
    if (response.status !== 200) {
        let code: ReferenceDataMaintenanceErrorCode | undefined;
        try {
            code = publicMaintenanceErrorCode(await response.json());
        }
        catch {
            // Keep the public error fixed when an intermediary returns an empty or
            // non-JSON body; diagnostics must never reach the UI.
        }
        throw new OrganizationDirectoryApiError(response.status, code);
    }
    try {
        const item = publicOrganizationResponse(await response.json());
        if (item === undefined || item.id.toLocaleLowerCase('en-US') !== id.toLocaleLowerCase('en-US')
            || item.code !== body.code || item.name !== body.name
            || item.parentId !== undefined && item.parentId !== null) {
            throw new Error('Unexpected organization response.');
        }
        return item;
    }
    catch (error) {
        if (error instanceof Error && error.name === 'AbortError')
            throw error;
        throw new Error('Не удалось получить подтверждение изменения организации.');
    }
}
function assertMaintenanceDirectory(value: string): asserts value is ReferenceDataMaintenanceDirectory {
    if (!['organizations', 'cost-centers', 'cash-flow-items', 'expense-items',
        'counterparties', 'contracts', 'currencies'].includes(value)) {
        throw new ReferenceDataMaintenanceApiError(400);
    }
}
function assertMaintenanceInput(input: ReferenceDataMaintenanceInput): void {
    if (!input || typeof input !== 'object')
        throw new ReferenceDataMaintenanceApiError(400);
    if ((input.flowDirectionSpecified !== undefined && typeof input.flowDirectionSpecified !== 'boolean')
        || (input.flowDirectionSpecified === true
            ? input.flowDirection !== null && input.flowDirection !== 'Payment' && input.flowDirection !== 'Receipt'
            : input.flowDirection != null))
        throw new ReferenceDataMaintenanceApiError(400);
    if (!input || typeof input.name !== 'string' || !isSafePublicText(input.name, 500)
        || (input.code !== undefined
            && (typeof input.code !== 'string' || !isSafePublicText(input.code, 100)))) {
        throw new ReferenceDataMaintenanceApiError(400);
    }
    if (input.bin !== undefined && input.bin !== null
        && (typeof input.bin !== 'string' || !/^\d{12}$/u.test(input.bin))) {
        throw new ReferenceDataMaintenanceApiError(400);
    }
    if (input.parentName !== undefined && input.parentName !== null
        && (typeof input.parentName !== 'string' || !isSafePublicText(input.parentName, 500))) {
        throw new ReferenceDataMaintenanceApiError(400);
    }
    if (input.activityType !== undefined && input.activityType !== null
        && (typeof input.activityType !== 'string' || !cashFlowActivityTypeValues.has(input.activityType))) {
        throw new ReferenceDataMaintenanceApiError(400);
    }
    // W58_APPROVED_R14_CURRENCY_FIELDS_BEGIN
    for (const value of [input.parentId, input.counterpartyId, input.organizationId, input.currencyId]) {
        if (value !== undefined && value !== null && !isReferenceGuid(value)) {
            throw new ReferenceDataMaintenanceApiError(400);
        }
    }
    if (input.parentIdSpecified !== undefined && typeof input.parentIdSpecified !== 'boolean') {
        throw new ReferenceDataMaintenanceApiError(400);
    }
    if (input.contractDate !== undefined && !/^\d{4}-\d{2}-\d{2}$/u.test(input.contractDate)) {
        throw new ReferenceDataMaintenanceApiError(400);
    }
    if (input.numericCode !== undefined
        && (typeof input.numericCode !== 'string' || !/^\d{3}$/u.test(input.numericCode))) {
        throw new ReferenceDataMaintenanceApiError(400);
    }
    // W58_APPROVED_R14_CURRENCY_FIELDS_END
}
function maintenanceErrorMessage(error: unknown, action: 'создать' | 'изменить' | 'удалить'): string {
    if (!(error instanceof ReferenceDataMaintenanceApiError)) {
        return `Результат операции «${action}» не подтверждён. Повторите попытку.`;
    }
    if (error.code === 'csrf-token-invalid') {
        return 'Для изменения справочника нужен защищённый HTTPS-сеанс. Откройте HTTPS-адрес вашей установки BMS, обновите страницу и повторите операцию.';
    }
    if (error.status === 400)
        return 'Проверьте код, наименование и обязательные поля справочника.';
    if (error.status === 401)
        return 'Требуется авторизация.';
    if (error.status === 403)
        return 'Изменение справочников доступно финансовому контролёру, финансовому директору, генеральному директору и системному администратору.';
    if (error.status === 404)
        return 'Элемент справочника не найден. Обновите список.';
    if (error.status === 409)
        return 'Операция конфликтует с существующим кодом или связанными данными.';
    return `Результат операции «${action}» не подтверждён. Повторите попытку.`;
}
export const referenceDataCreateErrorMessage = (error: unknown) => maintenanceErrorMessage(error, 'создать');
export const referenceDataUpdateErrorMessage = (error: unknown) => maintenanceErrorMessage(error, 'изменить');
export const referenceDataDeleteErrorMessage = (error: unknown) => maintenanceErrorMessage(error, 'удалить');
async function maintenanceRequest(method: 'POST' | 'PUT' | 'DELETE', directoryId: ReferenceDataMaintenanceDirectory, id: string | undefined, input: ReferenceDataMaintenanceInput | undefined, key?: string, signal?: AbortSignal): Promise<ReferenceDataItem> {
    assertMaintenanceDirectory(directoryId);
    if (id !== undefined && !isReferenceGuid(id))
        throw new ReferenceDataMaintenanceApiError(400);
    if (input)
        assertMaintenanceInput(input);
    if (method === 'POST' && (!key || !isSafePublicText(key, 127)))
        throw new ReferenceDataMaintenanceApiError(400);
    const path = `/api/v1/reference-data/maintenance/${encodeURIComponent(directoryId)}${id ? `/${encodeURIComponent(id)}` : ''}`;
    const headers: Record<string, string> = {};
    if (input) {
        headers['Content-Type'] = 'application/json';
    }
    if (key)
        headers['Idempotency-Key'] = key;
    const response = await fetchWithSafeTransport(apiUrl(path), {
        ...secureWriteInit(method, headers, input ? JSON.stringify(input) : undefined),
        signal,
    });
    if (response.status !== 200) {
        let code: ReferenceDataMaintenanceErrorCode | undefined;
        try {
            code = publicMaintenanceErrorCode(await response.json());
        }
        catch {
            // Keep the public error fixed when an intermediary returns an empty or
            // non-JSON body; diagnostics must never reach the UI.
        }
        throw new ReferenceDataMaintenanceApiError(response.status, code);
    }
    try {
        const item = publicReferenceDataCatalog({
            catalog: directoryId, source: 'bms_web', refreshedAtUtc: null, items: [await response.json()],
        }).items[0];
        if (!item)
            throw new Error('Empty reference-data response.');
        return item;
    }
    catch (error) {
        if (error instanceof Error && error.name === 'AbortError')
            throw error;
        throw new Error('Не удалось получить подтверждение операции.');
    }
}
export function createReferenceDataItem(directoryId: ReferenceDataMaintenanceDirectory, input: ReferenceDataMaintenanceInput, key: string, signal?: AbortSignal): Promise<ReferenceDataItem> {
    return maintenanceRequest('POST', directoryId, undefined, input, key, signal);
}
export function updateReferenceDataItem(directoryId: ReferenceDataMaintenanceDirectory, id: string, input: ReferenceDataMaintenanceInput, signal?: AbortSignal): Promise<ReferenceDataItem> {
    return maintenanceRequest('PUT', directoryId, id, input, undefined, signal);
}
export function deleteReferenceDataItem(directoryId: ReferenceDataMaintenanceDirectory, id: string, signal?: AbortSignal): Promise<ReferenceDataItem> {
    return maintenanceRequest('DELETE', directoryId, id, undefined, undefined, signal);
}
export async function getCostCenters(asOf?: string): Promise<ReferenceDataCatalog> {
    assertCostCenterAsOf(asOf);
    const parameters = new URLSearchParams();
    if (asOf)
        parameters.set('asOf', asOf);
    const suffix = parameters.size > 0 ? `?${parameters.toString()}` : '';
    const catalog = await getReferenceData(`/api/v1/reference-data/cost-centers${suffix}`);
    if (catalog.catalog !== 'cost-centers'
        || catalog.source !== 'bms_web'
        || catalog.refreshedAtUtc !== null) {
        throw new Error('Не удалось загрузить ЦФО.');
    }
    return catalog;
}
export type CashFlowItemOptionsRequest = {
    query?: string;
    selectedIds?: readonly string[];
    loadAll?: boolean;
};
/// The published limits of the windowed catalogue route. They are repeated
/// here rather than discovered from a rejection: a request the server would
/// refuse is not worth sending.
export const cashFlowItemQueryLimits = {
    maximumTermLength: 100,
    maximumPageSize: 100,
    defaultPageSize: 50,
} as const;
export type CashFlowItemQuery = {
    query?: string;
    skip?: number;
    take?: number;
};
function normalizedCashFlowItemOptions(request: CashFlowItemOptionsRequest): {
    query: string;
    selectedIds: string[];
    loadAll: boolean;
} {
    if ((request.query !== undefined && typeof request.query !== 'string')
        || (request.loadAll !== undefined && typeof request.loadAll !== 'boolean')
        || !Array.isArray(request.selectedIds ?? [])
        || (request.selectedIds?.length ?? 0) > maximumCashFlowItemSelections) {
        throw new Error('Некорректный запрос статей ДДС.');
    }
    const query = (request.query ?? '').trim();
    if (query.length > cashFlowItemQueryLimits.maximumTermLength) {
        throw new Error('Некорректный запрос статей ДДС.');
    }
    const seen = new Set<string>();
    const selectedIds: string[] = [];
    for (const candidate of request.selectedIds ?? []) {
        if (typeof candidate !== 'string' || !isReferenceGuid(candidate)) {
            throw new Error('Некорректный запрос статей ДДС.');
        }
        const key = candidate.toLocaleLowerCase('en-US');
        if (!seen.has(key)) {
            seen.add(key);
            selectedIds.push(candidate);
        }
    }
    return { query, selectedIds, loadAll: request.loadAll === true };
}
async function resolveCashFlowItems(ids: readonly string[]): Promise<ReferenceDataItem[]> {
    const parameters = new URLSearchParams();
    for (const id of ids)
        parameters.append('ids', id);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/reference-data/cash-flow-items/resolve?${parameters.toString()}`), secureReadInit());
    if (!response.ok) {
        throw new Error('Не удалось загрузить статьи ДДС.');
    }
    const catalog = publicReferenceDataCatalog(await response.json());
    const requestedIds = new Set(ids.map((id) => id.toLocaleLowerCase('en-US')));
    if (catalog.catalog !== 'cash-flow-items'
        || catalog.source !== 'bms_web'
        || catalog.refreshedAtUtc !== null
        || catalog.items.some((item) => !requestedIds.has(item.id.toLocaleLowerCase('en-US')))) {
        throw new Error('Не удалось загрузить статьи ДДС.');
    }
    return catalog.items;
}
/// A bounded option window for form controls, plus any already-selected rows
/// that may sit outside that window. Selected ids are resolved in fixed-size
/// batches, so reopening a large draft never falls back to one request per row
/// or to the legacy whole-catalogue response.
export async function getCashFlowItems(request: CashFlowItemOptionsRequest = {}): Promise<ReferenceDataCatalog> {
    const normalized = normalizedCashFlowItemOptions(request);
    try {
        if (normalized.loadAll)
            return await getAllCashFlowItems();
        const window = await queryCashFlowItems({
            query: normalized.query,
            skip: 0,
            take: cashFlowItemQueryLimits.defaultPageSize,
        });
        const items = [...window.items];
        const seen = new Set(items.map((item) => item.id.toLocaleLowerCase('en-US')));
        for (let offset = 0; offset < normalized.selectedIds.length; offset += cashFlowItemResolveBatchSize) {
            const resolved = await resolveCashFlowItems(normalized.selectedIds.slice(offset, offset + cashFlowItemResolveBatchSize));
            for (const item of resolved) {
                const key = item.id.toLocaleLowerCase('en-US');
                if (!seen.has(key)) {
                    seen.add(key);
                    items.push(item);
                }
            }
        }
        return {
            catalog: 'cash-flow-items',
            source: 'bms_web',
            refreshedAtUtc: null,
            items,
        };
    }
    catch {
        throw new Error('Не удалось загрузить статьи ДДС.');
    }
}
/// Loads only active cash-flow groups for the reference-data editor. The
/// endpoint is manager-only and avoids downloading the full leaf catalogue.
export async function getCashFlowItemGroups(): Promise<ReferenceDataCatalog> {
    try {
        const catalog = await getReferenceData('/api/v1/reference-data/cash-flow-items/groups');
        if (catalog.catalog !== 'cash-flow-item-groups'
            || catalog.source !== 'bms_web'
            || catalog.refreshedAtUtc !== null) {
            throw new Error('Unexpected cash-flow-item-groups catalog contract.');
        }
        return catalog;
    }
    catch {
        throw new Error('Не удалось загрузить группы статей ДДС.');
    }
}
/// Loads the complete active, non-group cash-flow catalogue for forms that
/// deliberately expose one scrollable selector instead of a bounded search
/// window. The server applies the same active/non-group scope as the windowed
/// route; this does not grant access to inactive or group rows.
export async function getAllCashFlowItems(): Promise<ReferenceDataCatalog> {
    try {
        return await getReferenceData('/api/v1/reference-data/cash-flow-items');
    }
    catch {
        throw new Error('Не удалось загрузить статьи ДДС.');
    }
}
/// Projects the windowed answer down to the public page shape.
///
/// The items go through the same projection as every other catalogue, so a
/// field the directory contract does not publish cannot arrive through the new
/// route either. The three numbers are checked as well: a total that is not a
/// whole number, or a window the server did not echo back, means the answer is
/// not the one that was asked for. Cross-field consistency matters too: a
/// page cannot contain more rows than remain after `skip`, even when `take`
/// itself would allow them.
export function publicReferenceDataPage(payload: unknown, skip: number, take: number): ReferenceDataPage {
    if (!isRecord(payload)) {
        throw new Error('Некорректный ответ API справочников.');
    }
    const source = payload;
    if (!isWholeNumber(source.total)
        || source.total < 0
        || !isWholeNumber(source.skip)
        || !isWholeNumber(source.take)
        || source.skip !== skip
        || source.take !== take) {
        throw new Error('Некорректный ответ API справочников.');
    }
    const { items: baseItems } = publicReferenceDataCatalog({
        catalog: 'cash-flow-items',
        source: 'bms_web',
        refreshedAtUtc: null,
        items: source.items,
    });
    const rawItems = source.items as unknown[];
    const items = baseItems.map((item, index): ReferenceDataItem => {
        const raw = rawItems[index];
        if (!isRecord(raw)
            || !Object.prototype.hasOwnProperty.call(raw, 'parentCode')
            || !Object.prototype.hasOwnProperty.call(raw, 'parentName')) {
            throw new Error('Некорректный ответ API справочников.');
        }
        // Older servers omit both fields. Do not invent a disabled flag for such
        // a response; once either field is present, require a consistent pair.
        let settings: Pick<ReferenceDataItem, 'formsManagerialAccrual' | 'accrualExpenseItemName'> = {};
        if (Object.prototype.hasOwnProperty.call(raw, 'costCenterId')
            || Object.prototype.hasOwnProperty.call(raw, 'costCenterName')) {
            if (!Object.prototype.hasOwnProperty.call(raw, 'costCenterId')
                || !Object.prototype.hasOwnProperty.call(raw, 'costCenterName')
                || ((raw.costCenterId === null) !== (raw.costCenterName === null)))
                throw new Error('Некорректный ответ API справочников.');
        }
        if (Object.prototype.hasOwnProperty.call(raw, 'formsManagerialAccrual')
            || Object.prototype.hasOwnProperty.call(raw, 'accrualExpenseItemName')) {
            if (typeof raw.formsManagerialAccrual !== 'boolean'
                || (raw.formsManagerialAccrual
                    ? typeof raw.accrualExpenseItemName !== 'string' || !isSafePublicText(raw.accrualExpenseItemName, 500)
                    : raw.accrualExpenseItemName !== null)) {
                throw new Error('Некорректный ответ API справочников.');
            }
            settings = { formsManagerialAccrual: raw.formsManagerialAccrual,
                accrualExpenseItemName: raw.accrualExpenseItemName as string | null };
        }
        const hasParent = item.parentId !== null && item.parentId !== undefined;
        if (hasParent) {
            if (typeof raw.parentCode !== 'string'
                || !isSafePublicText(raw.parentCode, 100)
                || typeof raw.parentName !== 'string'
                || !isSafePublicText(raw.parentName, 500)) {
                throw new Error('Некорректный ответ API справочников.');
            }
            return {
                ...item,
                ...settings,
                parentCode: raw.parentCode,
                parentName: raw.parentName,
            };
        }
        if (raw.parentCode !== null || raw.parentName !== null) {
            throw new Error('Некорректный ответ API справочников.');
        }
        return { ...item, ...settings, parentCode: null, parentName: null };
    });
    const remaining = Math.max(0, source.total - skip);
    if (items.length > Math.min(take, remaining)) {
        throw new Error('Некорректный ответ API справочников.');
    }
    return { items, total: source.total, skip, take };
}
function isWholeNumber(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value);
}
/// One page of the cash-flow-item catalogue, searched on the server.
///
/// The whole catalogue is tens of thousands of rows, so the directory asks for
/// a window instead of downloading all of it and filtering in the browser.
export async function queryCashFlowItems(request: CashFlowItemQuery = {}, signal?: AbortSignal): Promise<ReferenceDataPage> {
    const term = (request.query ?? '').trim();
    const skip = request.skip ?? 0;
    const take = request.take ?? cashFlowItemQueryLimits.defaultPageSize;
    if (!Number.isSafeInteger(skip) || skip < 0
        || !Number.isSafeInteger(take) || take < 1 || take > cashFlowItemQueryLimits.maximumPageSize
        || term.length > cashFlowItemQueryLimits.maximumTermLength) {
        throw new Error('Некорректный запрос статей ДДС.');
    }
    const parameters = new URLSearchParams();
    if (term !== '')
        parameters.set('query', term);
    parameters.set('skip', String(skip));
    parameters.set('take', String(take));
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/reference-data/cash-flow-items/query?${parameters.toString()}`), { ...secureReadInit(), ...(signal ? { signal } : {}) });
    if (!response.ok) {
        throw new Error('Не удалось загрузить статьи ДДС.');
    }
    try {
        return publicReferenceDataPage(await response.json(), skip, take);
    }
    catch {
        throw new Error('Не удалось загрузить статьи ДДС.');
    }
}
export async function getAccrualCostCenters(signal?: AbortSignal): Promise<ReferenceDataCatalog> {
    const catalog = await getReferenceData('/api/v1/reference-data/accrual-cost-centers', signal);
    if (catalog.catalog !== 'accrual-cost-centers' || catalog.source !== 'bms_web'
        || catalog.refreshedAtUtc !== null)
        throw new Error('Не удалось загрузить подразделения начисления.');
    return catalog;
}
export async function getOnlyCostDepartments473(signal?: AbortSignal): Promise<ReferenceDataCatalog> {
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/reference-data/cost-departments'), { ...secureReadInit(), signal });
    if (response.status !== 200)
        throw new Error('Не удалось загрузить подразделения затрат.');
    const catalog = publicReferenceDataCatalog(await response.json());
    if (catalog.catalog !== 'cost-departments' || catalog.source !== 'bms_web' || catalog.refreshedAtUtc !== null)
        throw new Error('Не удалось загрузить подразделения затрат.');
    return catalog;
}
export async function getExpenseItems(): Promise<ReferenceDataCatalog> {
    try {
        const catalog = await getReferenceData('/api/v1/reference-data/expense-items');
        if (catalog.catalog !== 'expense-items'
            || catalog.source !== 'bms_web'
            || catalog.refreshedAtUtc !== null
            || catalog.items.some((item) => item.parentId !== null)) {
            throw new Error('Unexpected expense-items catalog contract.');
        }
        return catalog;
    }
    catch {
        throw new Error('Не удалось загрузить статьи затрат.');
    }
}
export function expenseItemCreateErrorMessage(error: unknown): string {
    if (!(error instanceof ExpenseItemDirectoryApiError)) {
        return 'Результат сохранения статьи не подтверждён. Повторите ту же попытку.';
    }
    if (error.status === 400)
        return 'Укажите наименование до 250 символов.';
    if (error.status === 401)
        return 'Требуется авторизация.';
    if (error.status === 403) {
        return 'Добавление статей доступно финансовому контролёру, финансовому директору, генеральному директору и системному администратору.';
    }
    if (error.status === 409)
        return 'Статья с таким кодом уже существует.';
    return 'Результат сохранения статьи не подтверждён. Повторите ту же попытку.';
}
export function isExpenseItemCreateUncertain(error: unknown): boolean {
    return !(error instanceof ExpenseItemDirectoryApiError)
        || ![400, 401, 403, 409].includes(error.status);
}
export async function createExpenseItem(input: CreateExpenseItemInput, key: string, signal?: AbortSignal): Promise<ReferenceDataItem> {
    if (!input
        || !isSafePublicText(input.name, 250)
        || !isSafePublicText(key, 127)) {
        throw new ExpenseItemDirectoryApiError(400);
    }
    const body = { name: input.name.trim() };
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/reference-data/expense-items'), {
        ...secureWriteInit('POST', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, JSON.stringify(body)),
        signal,
    });
    if (response.status !== 200) {
        if (typeof response.arrayBuffer === 'function') {
            void response.arrayBuffer().catch(() => undefined);
        }
        throw new ExpenseItemDirectoryApiError(response.status);
    }
    try {
        const item = publicReferenceDataCatalog({ catalog: 'expense-items', source: 'bms_web', refreshedAtUtc: null, items: [await response.json()] }).items[0];
        if (item === undefined || !/^PL-[0-9]{3,6}$/u.test(item.code) || item.name !== body.name
            || item.parentId !== undefined && item.parentId !== null) {
            throw new Error('Unexpected expense-item response.');
        }
        return item;
    }
    catch (error) {
        if (error instanceof Error && error.name === 'AbortError')
            throw error;
        throw new Error('Не удалось получить подтверждение создания статьи.');
    }
}
export async function getCounterparties(): Promise<ReferenceDataCatalog> {
    try {
        const catalog = await getReferenceData('/api/v1/reference-data/counterparties');
        if (catalog.catalog !== 'counterparties'
            || catalog.source !== 'bms_web'
            || catalog.refreshedAtUtc !== null) {
            throw new Error('Unexpected counterparties catalog contract.');
        }
        return catalog;
    }
    catch {
        throw new Error('Не удалось загрузить контрагентов.');
    }
}
export async function getContracts(counterpartyId: string, organizationId: string): Promise<ReferenceDataCatalog> {
    const normalizedCounterpartyId = typeof counterpartyId === 'string'
        ? counterpartyId.trim()
        : '';
    const normalizedOrganizationId = typeof organizationId === 'string'
        ? organizationId.trim()
        : '';
    if (!isReferenceGuid(normalizedCounterpartyId) || !isReferenceGuid(normalizedOrganizationId)) {
        throw new Error('Для загрузки договоров выберите организацию и контрагента.');
    }
    const parameters = new URLSearchParams({
        counterpartyId: normalizedCounterpartyId,
        organizationId: normalizedOrganizationId,
    });
    try {
        const catalog = await getReferenceData(`/api/v1/reference-data/contracts?${parameters.toString()}`);
        if (catalog.catalog !== 'contracts'
            || catalog.source !== 'bms_web'
            || catalog.refreshedAtUtc !== null) {
            throw new Error('Unexpected contracts catalog contract.');
        }
        return catalog;
    }
    catch {
        throw new Error('Не удалось загрузить договоры.');
    }
}
export async function getCurrencies(): Promise<ReferenceDataCatalog> {
    try {
        const catalog = await getReferenceData('/api/v1/reference-data/currencies');
        if (catalog.catalog !== 'currencies'
            || catalog.source !== 'bms_web'
            || catalog.refreshedAtUtc !== null
            || catalog.items.some((item) => !/^[A-Z]{3}$/u.test(item.code))
            || new Set(catalog.items.map((item) => item.code)).size !== catalog.items.length) {
            throw new Error('Unexpected currencies catalog contract.');
        }
        return catalog;
    }
    catch {
        throw new Error('Не удалось загрузить валюты.');
    }
}
export async function getPaymentBases(): Promise<ReferenceCodeCatalog> {
    return getReferenceCodeData('/api/v1/reference-data/payment-bases', 'payment-bases', paymentBasisCodes);
}
export async function getCounterpartyResidencies(): Promise<ReferenceCodeCatalog> {
    return getReferenceCodeData('/api/v1/reference-data/counterparty-residencies', 'counterparty-residencies', counterpartyResidencyCodes);
}
