/// Expense request reads: one request, the list, the export and the history.
import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { ExcelExportLimitError } from './excelExportFailure';
import { responseOrError } from './expenseRequestCommands';
import { publicExpenseRequestPage } from './expenseRequestListPublicResponse';
import { publicExpenseRequestHistoryPage } from './expenseRequestHistoryPublicResponse';
import { publicSubmissionPreflight } from './expenseRequestPreflightPublicResponse';
import { assertExpenseRequestIdentifier } from './expenseRequestIdentifier';
import { assertExpenseRequestListSort, type ExpenseRequestListSort } from '../utils/expenseRequestListSort';
import { isExpenseRequestStatus } from '../expenseRequestStatus';
import type { ExpenseRequestHistoryPage, ExpenseRequestPage, ExpenseRequestStatus, SavedExpenseRequest, SubmissionPreflight } from '../types';
const invalidHistoryPagingMessage = 'Некорректные параметры страницы истории заявки.';
const invalidListPagingMessage = 'Некорректные параметры страницы списка расходных заявок.';
const invalidExpenseRequestFilterMessage = 'Некорректные параметры фильтра расходных заявок.';
const maximumInt32 = 2147483647;
const publicGuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
const dateOnlyPattern = /^\d{4}-\d{2}-\d{2}$/u;
const utcDateTimePattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?Z$/u;
function isGuid(value: unknown): value is string {
    return typeof value === 'string' && publicGuidPattern.test(value);
}
function isCanonicalDateOnly(value: unknown): value is string {
    if (typeof value !== 'string' || !dateOnlyPattern.test(value))
        return false;
    const parsed = new Date(`${value}T00:00:00Z`);
    return !Number.isNaN(parsed.valueOf()) && parsed.toISOString().slice(0, 10) === value;
}
function isCanonicalUtcDateTime(value: unknown): value is string {
    if (typeof value !== 'string' || !utcDateTimePattern.test(value))
        return false;
    const parsed = new Date(value);
    return !Number.isNaN(parsed.valueOf())
        && parsed.toISOString().slice(0, 19) === value.slice(0, 19);
}
function isCanonicalFilterText(value: unknown): value is string {
    return typeof value === 'string'
        && value.length > 0
        && value.length <= 256
        && value.trim() === value
        && !/[\u0000-\u001f\u007f-\u009f]/u.test(value);
}
function assertExpenseRequestFilterInputs(isUrgent: boolean | null | undefined, plannedPaymentDate: string | null | undefined, organizationId: string | null | undefined, status: ExpenseRequestStatus | null | undefined, costCenterId: string | null | undefined, counterpartyId: string | null | undefined, initiatorExternalId: string | null | undefined, createdFromUtc: string | null | undefined, createdBeforeUtc: string | null | undefined, onlyMyTasks: boolean | null | undefined): void {
    const valid = (isUrgent == null || typeof isUrgent === 'boolean')
        && (plannedPaymentDate == null || isCanonicalDateOnly(plannedPaymentDate))
        && (organizationId == null || isGuid(organizationId))
        && (status == null || isExpenseRequestStatus(status))
        && (costCenterId == null || isGuid(costCenterId))
        && (counterpartyId == null || isGuid(counterpartyId))
        && (initiatorExternalId == null || isCanonicalFilterText(initiatorExternalId))
        && (createdFromUtc == null || isCanonicalUtcDateTime(createdFromUtc))
        && (createdBeforeUtc == null || isCanonicalUtcDateTime(createdBeforeUtc))
        && (onlyMyTasks == null || typeof onlyMyTasks === 'boolean');
    if (!valid)
        throw new Error(invalidExpenseRequestFilterMessage);
    if (createdFromUtc != null && createdBeforeUtc != null
        && Date.parse(createdFromUtc) >= Date.parse(createdBeforeUtc)) {
        throw new Error(invalidExpenseRequestFilterMessage);
    }
}
function assertHistoryPaging(skip: number, take: number): void {
    if (!Number.isSafeInteger(skip)
        || skip < 0
        || skip > maximumInt32
        || !Number.isSafeInteger(take)
        || take < 1
        || take > 50) {
        throw new Error(invalidHistoryPagingMessage);
    }
}
function assertListPaging(skip: number, take: number): void {
    if (!Number.isSafeInteger(skip)
        || skip < 0
        || skip > maximumInt32
        || !Number.isSafeInteger(take)
        || take < 1
        || take > 200) {
        throw new Error(invalidListPagingMessage);
    }
}
export async function getExpenseRequest(id: string): Promise<SavedExpenseRequest> {
    assertExpenseRequestIdentifier(id);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${encodeURIComponent(id)}`), secureReadInit());
    return responseOrError(response, id, { requireEditAuthority: true });
}
export async function getExpenseRequestSubmissionPreflight(id: string, signal?: AbortSignal): Promise<SubmissionPreflight> {
    assertExpenseRequestIdentifier(id);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${encodeURIComponent(id)}/submission-preflight`), { ...secureReadInit(), method: 'GET', ...(signal ? { signal } : {}) });
    if (!response.ok) {
        const fallback = response.status === 401
            ? 'Для проверки готовности требуется авторизация.'
            : response.status === 404
                ? 'Расходная заявка не найдена или недоступна.'
                : response.status === 503
                    ? 'Проверка готовности временно недоступна. Повторите попытку позднее.'
                    : `Не удалось проверить готовность заявки (код ${response.status}).`;
        throw new Error(fallback);
    }
    return publicSubmissionPreflight(await response.json(), id);
}
export async function listExpenseRequests(skip = 0, take = 50, isUrgent?: boolean, plannedPaymentDate?: string, organizationId?: string, status?: ExpenseRequestStatus, costCenterId?: string, counterpartyId?: string, initiatorExternalId?: string, createdFromUtc?: string, createdBeforeUtc?: string, onlyMyTasks?: boolean, signal?: AbortSignal, includeBusinessColumns = false, sort?: ExpenseRequestListSort, search?: string, initiatorEmployeeId?: string, hideDrafts = false): Promise<ExpenseRequestPage> {
    if (typeof hideDrafts !== 'boolean')
        throw new Error(invalidExpenseRequestFilterMessage);
    assertExpenseRequestListSort(sort);
    assertExpenseSearch(search);
    if (initiatorEmployeeId !== undefined && (!isGuid(initiatorEmployeeId)
        || initiatorEmployeeId === '00000000-0000-0000-0000-000000000000'))
        throw new Error(invalidExpenseRequestFilterMessage);
    assertListPaging(skip, take);
    if (typeof includeBusinessColumns !== 'boolean')
        throw new Error(invalidExpenseRequestFilterMessage);
    assertExpenseRequestFilterInputs(isUrgent, plannedPaymentDate, organizationId, status, costCenterId, counterpartyId, initiatorExternalId, createdFromUtc, createdBeforeUtc, onlyMyTasks);
    const parameters = new URLSearchParams({ skip: String(skip), take: String(take) });
    parameters.set('includeInitiatorDisplayName', 'true');
    parameters.set('includeRequestDate', 'true');
    parameters.set('retainPaidBudget', 'true');
    if (hideDrafts)
        parameters.set('hideDrafts', 'true');
    if (sort) {
        parameters.set('sortBy', sort.by);
        parameters.set('sortDirection', sort.direction);
    }
    if (includeBusinessColumns)
        parameters.set('includeBusinessColumns', 'true');
    if (isUrgent != null)
        parameters.set('isUrgent', String(isUrgent));
    if (plannedPaymentDate != null)
        parameters.set('plannedPaymentDate', plannedPaymentDate);
    if (organizationId != null)
        parameters.set('organizationId', organizationId);
    if (status != null)
        parameters.set('status', status);
    if (costCenterId != null)
        parameters.set('costCenterId', costCenterId);
    if (counterpartyId != null)
        parameters.set('counterpartyId', counterpartyId);
    if (initiatorExternalId != null)
        parameters.set('initiatorExternalId', initiatorExternalId);
    if (onlyMyTasks === true)
        parameters.set('onlyMyTasks', 'true');
    if (createdFromUtc != null)
        parameters.set('createdFromUtc', createdFromUtc);
    if (createdBeforeUtc != null)
        parameters.set('createdBeforeUtc', createdBeforeUtc);
    if (search)
        parameters.set('search', search);
    if (initiatorEmployeeId !== undefined)
        parameters.set('initiatorEmployeeId', initiatorEmployeeId);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests?${parameters.toString()}`), { ...secureReadInit(), signal });
    if (!response.ok) {
        throw new Error(`Ошибка API заявок: ${response.status}`);
    }
    return publicExpenseRequestPage(await response.json(), includeBusinessColumns);
}
export type ExpenseRequestExportDownload = {
    blob: Blob;
    fileName: string;
};
export async function getExpenseRequestExport(isUrgent?: boolean, plannedPaymentDate?: string, organizationId?: string, status?: ExpenseRequestStatus, costCenterId?: string, counterpartyId?: string, initiatorExternalId?: string, createdFromUtc?: string, createdBeforeUtc?: string, onlyMyTasks?: boolean, search?: string, initiatorEmployeeId?: string, hideDrafts = false): Promise<ExpenseRequestExportDownload> {
    if (typeof hideDrafts !== 'boolean')
        throw new Error(invalidExpenseRequestFilterMessage);
    assertExpenseSearch(search);
    if (initiatorEmployeeId !== undefined && (!isGuid(initiatorEmployeeId)
        || initiatorEmployeeId === '00000000-0000-0000-0000-000000000000'))
        throw new Error(invalidExpenseRequestFilterMessage);
    assertExpenseRequestFilterInputs(isUrgent, plannedPaymentDate, organizationId, status, costCenterId, counterpartyId, initiatorExternalId, createdFromUtc, createdBeforeUtc, onlyMyTasks);
    const parameters = new URLSearchParams();
    if (hideDrafts)
        parameters.set('hideDrafts', 'true');
    if (isUrgent != null)
        parameters.set('isUrgent', String(isUrgent));
    if (plannedPaymentDate != null)
        parameters.set('plannedPaymentDate', plannedPaymentDate);
    if (organizationId != null)
        parameters.set('organizationId', organizationId);
    if (status != null)
        parameters.set('status', status);
    if (costCenterId != null)
        parameters.set('costCenterId', costCenterId);
    if (counterpartyId != null)
        parameters.set('counterpartyId', counterpartyId);
    if (initiatorExternalId != null)
        parameters.set('initiatorExternalId', initiatorExternalId);
    if (onlyMyTasks === true)
        parameters.set('onlyMyTasks', 'true');
    if (createdFromUtc != null)
        parameters.set('createdFromUtc', createdFromUtc);
    if (createdBeforeUtc != null)
        parameters.set('createdBeforeUtc', createdBeforeUtc);
    if (search)
        parameters.set('search', search);
    if (initiatorEmployeeId !== undefined)
        parameters.set('initiatorEmployeeId', initiatorEmployeeId);
    const query = parameters.size === 0 ? '' : `?${parameters.toString()}`;
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/export.xlsx${query}`), secureReadInit());
    if (response.status === 422)
        throw new ExcelExportLimitError();
    if (!response.ok) {
        throw new Error(`Не удалось сформировать Excel (код ${response.status}).`);
    }
    return {
        blob: await response.blob(),
        fileName: exportFileName(response.headers.get('content-disposition')),
    };
}
/// W260: the download name is the one value a facade takes from a server header
/// and hands to the filesystem, through the anchor's download attribute.
///
/// The exporter builds it as expense-requests-<yyyyMMdd-HHmmss>Z.xlsx, so that
/// exact shape is what is accepted. Anything else — a different name, extra
/// path segments, another extension, a header that is absent or malformed —
/// falls back to the fixed default rather than being trusted. The rule is read
/// off ExpenseRequestExcelExporter and introduces no new naming policy.
const exportFileNamePattern = /^expense-requests-\d{8}-\d{6}Z\.xlsx$/u;
const defaultExportFileName = 'expense-requests.xlsx';
function exportFileName(contentDisposition: string | null): string {
    if (contentDisposition === null)
        return defaultExportFileName;
    const match = /filename="?([^";]+)"?/iu.exec(contentDisposition);
    const candidate = match?.[1]?.trim();
    return candidate !== undefined && exportFileNamePattern.test(candidate)
        ? candidate
        : defaultExportFileName;
}
export async function getExpenseRequestHistory(id: string, skip = 0, take = 20): Promise<ExpenseRequestHistoryPage> {
    assertExpenseRequestIdentifier(id);
    assertHistoryPaging(skip, take);
    const parameters = new URLSearchParams({
        skip: String(skip),
        take: String(take),
    });
    const historyRead = secureReadInit();
    historyRead.headers['X-Bms-History-Authors'] = 'employee-names';
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${encodeURIComponent(id)}/history?${parameters.toString()}`), historyRead);
    if (!response.ok) {
        if (response.status === 401) {
            throw new Error('Для просмотра истории требуется авторизация.');
        }
        if (response.status === 404) {
            throw new Error('Расходная заявка не найдена или недоступна.');
        }
        throw new Error(`Не удалось загрузить историю заявки (код ${response.status}).`);
    }
    return publicExpenseRequestHistoryPage(await response.json(), skip, take);
}
function assertExpenseSearch(value: string | undefined): void {
    if (value !== undefined && (typeof value !== 'string' || value.length > 200 || value !== value.trim() || /[\u0000-\u001f\u007f]/u.test(value)))
        throw new Error('Некорректный поиск по номеру или назначению заявки.');
}
