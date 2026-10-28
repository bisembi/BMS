import { apiUrl, fetchWithOneTimeRefresh, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
import { getCurrentSession } from './session';
import { isPublicExpenseRequestText, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
import { exactMoneyNumber } from '../utils/moneyInput';
export type BudgetInstanceLine = {
    id: string;
    cashFlowItemId: string;
    budgetMonth: string;
    limitAmountKzt: number;
    sortOrder: number;
    comment?: string;
    flowDirection?: BudgetFlowDirection;
};
export type BudgetFlowDirection = 'Payment' | 'Receipt';
export type BudgetInstance = {
    id: string;
    number: string;
    costCenterId: string;
    costCenterCode?: string;
    costCenterName?: string;
    validFrom: string;
    validTo: string;
    status: string;
    ownerEmployeeId: string;
    concurrencyStamp: string;
    approvalOrdinal?: number;
    approvedAtUtc?: string;
    approvedByEmployeeId?: string;
    lines: BudgetInstanceLine[];
    totalLimitAmountKzt: number;
    totalReceiptsAmountKzt?: number;
    totalPaymentsAmountKzt?: number;
    netCashFlowAmountKzt?: number;
    canEdit: boolean;
    /// Older idempotency payloads may omit this newly explicit capability;
    /// absence is fail-closed and never grants an approval button.
    canApprove?: boolean;
};
export type BudgetInstanceListItem = {
    id: string;
    number: string;
    costCenterId: string;
    costCenterCode?: string;
    costCenterName?: string;
    validFrom: string;
    validTo: string;
    status: string;
    lineCount: number;
    totalLimitAmountKzt: number;
    totalReceiptsAmountKzt?: number;
    totalPaymentsAmountKzt?: number;
    netCashFlowAmountKzt?: number;
    concurrencyStamp: string;
    isEffective?: boolean;
    canDelete?: boolean;
    approvalState?: 'Pending' | 'Returned' | 'Rejected' | 'Approved';
    authorName?: string;
    includedVersions?: Array<{
        id: string;
        name: string;
    }>;
};
export type BudgetInstancePage = {
    items: BudgetInstanceListItem[];
    skip: number;
    take: number;
    total: number;
};
export type BudgetInstanceLineInput = {
    cashFlowItemId: string;
    budgetMonth: string;
    limitAmountKzt: number;
    comment?: string;
    flowDirection?: BudgetFlowDirection;
};
export type CreateBudgetInstanceInput = {
    costCenterId: string;
    validFrom: string;
    validTo: string;
    lines: BudgetInstanceLineInput[];
};
export type UpdateBudgetInstanceInput = {
    expectedConcurrencyStamp: string;
    validFrom: string;
    validTo: string;
    lines: BudgetInstanceLineInput[];
};
export type ApproveBudgetInstanceInput = {
    expectedConcurrencyStamp: string;
};
export type DeleteBudgetInstanceInput = {
    costCenterId: string;
    expectedConcurrencyStamp: string;
};
export type BudgetInstanceCostCenter = {
    id: string;
    code: string;
    name: string;
    isActive: boolean;
};
export type BudgetInstanceCreationOptions = {
    canChooseAnyCostCenter: boolean;
    defaultCostCenter: BudgetInstanceCostCenter | null;
};
export type BudgetInstanceCostCenterPage = {
    items: BudgetInstanceCostCenter[];
    skip: number;
    take: number;
    total: number;
};
const publicErrorStatuses = {
    'csrf-token-invalid': 403,
    'budget-article-unavailable': 400,
    'budget-article-cfo-mismatch': 400,
    'budget-in-approval': 400,
    'budget-period-invalid': 400,
    'budget-lines-invalid': 400,
    'budget-numbering-uninitialized': 400,
} as const;
type BudgetInstanceApiErrorCode = keyof typeof publicErrorStatuses;
export type BudgetInstanceErrorDetails = {
    code: BudgetInstanceApiErrorCode;
    rowIndices: readonly number[];
};
class BudgetInstanceApiError extends Error {
    constructor(readonly status: number, readonly code?: BudgetInstanceApiErrorCode, readonly rowIndices: readonly number[] = []) {
        super('Не удалось выполнить запрос экземпляра бюджета.');
    }
}
function publicBudgetInstanceErrorDetails(value: unknown, status: number): BudgetInstanceErrorDetails | undefined {
    if (!record(value) || typeof value.code !== 'string' || !Object.hasOwn(publicErrorStatuses, value.code))
        return undefined;
    if (value.status !== undefined && value.status !== status)
        return undefined;
    const code = value.code as BudgetInstanceApiErrorCode;
    if (publicErrorStatuses[code] !== status)
        return undefined;
    const rowIndices = Array.isArray(value.rowIndices) && value.rowIndices.length === 1
        && Number.isInteger(value.rowIndices[0]) && value.rowIndices[0] >= 0 && value.rowIndices[0] <= 999999
        && (code === 'budget-article-unavailable' || code === 'budget-article-cfo-mismatch' || code === 'budget-lines-invalid')
        ? [value.rowIndices[0] as number] : [];
    return { code, rowIndices };
}
/** Error bodies may be HTML, oversized or never-ending. Read only bounded public metadata, never raw detail. */
async function readPublicError(response: Response): Promise<BudgetInstanceErrorDetails | undefined> {
    const maxBytes = 32 * 1024;
    const declaredLength = response.headers.get('Content-Length');
    if (declaredLength !== null && Number(declaredLength) > maxBytes) {
        void response.body?.cancel().catch(() => undefined);
        return undefined;
    }
    if (!response.body)
        return undefined;
    const reader = response.body.getReader();
    let timer: ReturnType<typeof setTimeout> | undefined;
    const timeout = new Promise<undefined>(resolve => {
        timer = setTimeout(() => { void reader.cancel().catch(() => undefined); resolve(undefined); }, 1500);
    });
    const read = async (): Promise<BudgetInstanceErrorDetails | undefined> => {
        try {
            const chunks: Uint8Array[] = [];
            let size = 0;
            while (true) {
                const chunk = await reader.read();
                if (chunk.done)
                    break;
                size += chunk.value.byteLength;
                if (size > maxBytes) {
                    void reader.cancel().catch(() => undefined);
                    return undefined;
                }
                chunks.push(chunk.value);
            }
            const bytes = new Uint8Array(size);
            let offset = 0;
            for (const chunk of chunks) {
                bytes.set(chunk, offset);
                offset += chunk.byteLength;
            }
            return publicBudgetInstanceErrorDetails(JSON.parse(new TextDecoder().decode(bytes)), response.status);
        }
        catch {
            return undefined;
        }
        finally {
            try {
                reader.releaseLock();
            }
            catch { /* Already cancelled/locked: no public metadata. */ }
        }
    };
    try {
        return await Promise.race([read(), timeout]);
    }
    finally {
        if (timer !== undefined)
            clearTimeout(timer);
    }
}
async function reject(response: Response): Promise<never> {
    let details: BudgetInstanceErrorDetails | undefined;
    try {
        details = await readPublicError(response);
    }
    catch { /* Metadata failure never changes a known HTTP refusal. */ }
    throw new BudgetInstanceApiError(response.status, details?.code, details?.rowIndices);
}
export function budgetInstanceErrorDetails(error: unknown): BudgetInstanceErrorDetails | undefined {
    return error instanceof BudgetInstanceApiError && error.code
        ? { code: error.code, rowIndices: error.rowIndices } : undefined;
}
const invalidResponse = 'Не удалось получить корректный экземпляр бюджета. Повторите загрузку.';
export function isBudgetInstanceResultUncertain(error: unknown): boolean {
    return !(error instanceof BudgetInstanceApiError)
        || ![400, 401, 403, 404, 409].includes(error.status);
}
export function isBudgetInstanceConflict(error: unknown): boolean {
    return error instanceof BudgetInstanceApiError && error.status === 409;
}
export function isBudgetInstanceValidationError(error: unknown): boolean {
    return error instanceof BudgetInstanceApiError && error.status === 400;
}
export function budgetInstanceErrorMessage(error: unknown): string {
    if (!(error instanceof BudgetInstanceApiError)) {
        return 'Результат операции не подтверждён. Повторите ту же попытку или проверьте текущее состояние.';
    }
    if (error.status === 400) {
        if (error.code === 'budget-article-unavailable')
            return 'Статья строки отсутствует в доступном справочнике. Обновите справочник или выберите другую статью.';
        if (error.code === 'budget-article-cfo-mismatch')
            return 'Статья строки недоступна для ЦФО бюджета. Проверьте активность и назначение статьи.';
        if (error.code === 'budget-in-approval')
            return 'Бюджет находится на согласовании. Дождитесь завершения или попросите согласующего вернуть бюджет на доработку, затем загрузите текущее состояние.';
        if (error.code === 'budget-period-invalid')
            return 'Период бюджета некорректен. Проверьте начало, окончание и месяцы строк.';
        if (error.code === 'budget-lines-invalid')
            return 'Строки лимитов отклонены. Проверьте статьи, месяцы, суммы и длину комментариев (не более 2 000 символов).';
        if (error.code === 'budget-numbering-uninitialized')
            return 'Нумерация для ЦФО не настроена. Обратитесь к администратору: нужно настроить сокращение и счётчик бюджета.';
        return 'Проверьте период и строки лимитов. Для создания также должно быть настроено сокращение ЦФО.';
    }
    if (error.status === 401)
        return 'Для работы с экземплярами бюджета требуется авторизация.';
    if (error.code === 'csrf-token-invalid') {
        return 'Сеанс безопасности устарел. Обновите страницу и повторите операцию.';
    }
    if (error.status === 403)
        return 'Недостаточно прав участника бюджета для этого ЦФО.';
    if (error.status === 404)
        return 'Экземпляр бюджета не найден или недоступен.';
    if (error.status === 409) {
        return 'Черновик изменён либо ключ попытки уже использован. Ваш ввод сохранён; загрузите текущее состояние.';
    }
    return 'Результат операции не подтверждён. Повторите ту же попытку или проверьте текущее состояние.';
}
function record(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function text(value: unknown, limit: number): value is string {
    return isPublicExpenseRequestText(value, limit) && value.trim() === value && value.length > 0;
}
/** Budget operations are intentionally multiline. Domain permits CR/LF/TAB,
 * rejects every other C0/C1 control, and stores a trimmed comment up to 2 000 chars. */
function budgetCommentText(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= 2000 && value.trim() === value
        && !/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/u.test(value);
}
// Only older serialized idempotency responses may omit the pair. Never
// synthesize a CFO label from its UUID or the number allocated to a document.
function publicCostCenterLabel(value: Record<string, unknown>, required = false): {
    costCenterCode?: string;
    costCenterName?: string;
} {
    if (!required && value.costCenterCode == null && value.costCenterName == null)
        return {};
    if (!text(value.costCenterCode, 100) || !text(value.costCenterName, 500)) {
        throw new Error(invalidResponse);
    }
    return { costCenterCode: value.costCenterCode, costCenterName: value.costCenterName };
}
// Validate the decimal representation, not value * 100: binary multiplication
// can drift even for valid large amounts with exactly two decimal places.
// Preserve the original wire amount; use exact safe integer cents only to check it.
function money(value: unknown): value is number {
    return typeof value === 'number' && value >= 0 && signedMoneyCents(value) !== null;
}
function moneyCents(value: number): number | null {
    return typeof value === 'number' && value >= 0 ? signedMoneyCents(value) : null;
}
function signedMoneyCents(value: unknown): number | null {
    if (typeof value !== 'number' || !Number.isFinite(value))
        return null;
    const canonical = String(Math.abs(value));
    if (exactMoneyNumber(canonical, true) === null)
        return null;
    const [whole, fraction = ''] = canonical.split('.');
    const cents = Number(BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0')));
    return value < 0 ? -cents : cents;
}
function month(value: unknown): value is string {
    return calendarDate(value) && value.endsWith('-01');
}
function calendarDate(value: unknown): value is string {
    if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/u.test(value)) {
        return false;
    }
    if (Number(value.slice(0, 4)) < 1) {
        return false;
    }
    const parsed = new Date(`${value}T00:00:00.000Z`);
    return !Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
}
type PeriodBounds = {
    validFrom: string;
    validTo: string;
};
function periodBounds(validFrom: unknown, validTo: unknown): PeriodBounds | null {
    if (!calendarDate(validFrom)
        || !calendarDate(validTo)
        || !validFrom.endsWith('-01')
        || validFrom > validTo) {
        return null;
    }
    const lastDay = new Date(`${validTo.slice(0, 7)}-01T00:00:00.000Z`);
    lastDay.setUTCMonth(lastDay.getUTCMonth() + 1, 0);
    return validTo === lastDay.toISOString().slice(0, 10) ? { validFrom, validTo } : null;
}
function publicLine(value: unknown, bounds: PeriodBounds): BudgetInstanceLine {
    if (!record(value)
        || !isPublicExpenseRequestUuid(value.id)
        || !isPublicExpenseRequestUuid(value.cashFlowItemId)
        || !month(value.budgetMonth)
        || value.budgetMonth < bounds.validFrom
        || value.budgetMonth > bounds.validTo
        || !money(value.limitAmountKzt)
        || typeof value.sortOrder !== 'number'
        || !Number.isInteger(value.sortOrder)
        || value.sortOrder < 0
        || (value.comment !== undefined
            && !budgetCommentText(value.comment))
        || (value.flowDirection !== undefined
            && value.flowDirection !== 'Payment' && value.flowDirection !== 'Receipt')) {
        throw new Error(invalidResponse);
    }
    return {
        id: value.id,
        cashFlowItemId: value.cashFlowItemId,
        budgetMonth: value.budgetMonth,
        limitAmountKzt: value.limitAmountKzt,
        sortOrder: value.sortOrder,
        ...(typeof value.comment === 'string' ? { comment: value.comment } : {}),
        ...(value.flowDirection === 'Payment' || value.flowDirection === 'Receipt'
            ? { flowDirection: value.flowDirection } : {}),
    };
}
export function publicBudgetInstance(value: unknown): BudgetInstance {
    const bounds = record(value) ? periodBounds(value.validFrom, value.validTo) : null;
    if (!record(value)
        || !isPublicExpenseRequestUuid(value.id)
        || !text(value.number, 40)
        || !isPublicExpenseRequestUuid(value.costCenterId)
        || bounds === null
        || (value.status !== 'Draft' && value.status !== 'Approved')
        || !isPublicExpenseRequestUuid(value.ownerEmployeeId)
        || !isPublicExpenseRequestUuid(value.concurrencyStamp)
        || !Array.isArray(value.lines)
        || value.lines.length === 0
        || !money(value.totalLimitAmountKzt)
        || typeof value.canEdit !== 'boolean'
        || (value.canApprove !== undefined && typeof value.canApprove !== 'boolean')
        || (value.approvalOrdinal !== undefined
            && (typeof value.approvalOrdinal !== 'number'
                || !Number.isSafeInteger(value.approvalOrdinal)
                || value.approvalOrdinal < 1))
        || (value.approvedAtUtc !== undefined
            && (typeof value.approvedAtUtc !== 'string' || Number.isNaN(Date.parse(value.approvedAtUtc))))
        || (value.approvedByEmployeeId !== undefined
            && !isPublicExpenseRequestUuid(value.approvedByEmployeeId))) {
        throw new Error(invalidResponse);
    }
    const lines = value.lines.map((lineValue) => publicLine(lineValue, bounds));
    const lineIds = new Set<string>();
    let totalCents = 0;
    let receiptCents = 0;
    for (const line of lines) {
        const normalizedLineId = line.id.toLowerCase();
        const direction = line.flowDirection ?? 'Payment';
        if (lineIds.has(normalizedLineId)) {
            throw new Error(invalidResponse);
        }
        lineIds.add(normalizedLineId);
        const lineCents = moneyCents(line.limitAmountKzt);
        if (lineCents === null) {
            throw new Error(invalidResponse);
        }
        if (direction === 'Payment') {
            if (!Number.isSafeInteger(totalCents + lineCents))
                throw new Error(invalidResponse);
            totalCents += lineCents;
        }
        else {
            if (!Number.isSafeInteger(receiptCents + lineCents))
                throw new Error(invalidResponse);
            receiptCents += lineCents;
        }
    }
    const declaredCents = moneyCents(value.totalLimitAmountKzt);
    if (declaredCents === null || declaredCents !== totalCents) {
        throw new Error(invalidResponse);
    }
    const paymentCents = totalCents;
    if ((value.totalReceiptsAmountKzt !== undefined
        && moneyCents(value.totalReceiptsAmountKzt as number) !== receiptCents)
        || (value.totalPaymentsAmountKzt !== undefined
            && moneyCents(value.totalPaymentsAmountKzt as number) !== paymentCents)
        || (value.netCashFlowAmountKzt !== undefined
            && signedMoneyCents(value.netCashFlowAmountKzt) !== receiptCents - paymentCents)) {
        throw new Error(invalidResponse);
    }
    return {
        id: value.id,
        number: value.number,
        costCenterId: value.costCenterId,
        validFrom: bounds.validFrom,
        validTo: bounds.validTo,
        status: value.status,
        ownerEmployeeId: value.ownerEmployeeId,
        concurrencyStamp: value.concurrencyStamp,
        ...(typeof value.approvalOrdinal === 'number' ? { approvalOrdinal: value.approvalOrdinal } : {}),
        ...(typeof value.approvedAtUtc === 'string' ? { approvedAtUtc: value.approvedAtUtc } : {}),
        ...(typeof value.approvedByEmployeeId === 'string' ? { approvedByEmployeeId: value.approvedByEmployeeId } : {}),
        lines,
        totalLimitAmountKzt: value.totalLimitAmountKzt,
        ...(typeof value.totalReceiptsAmountKzt === 'number'
            ? { totalReceiptsAmountKzt: value.totalReceiptsAmountKzt } : {}),
        ...(typeof value.totalPaymentsAmountKzt === 'number'
            ? { totalPaymentsAmountKzt: value.totalPaymentsAmountKzt } : {}),
        ...(typeof value.netCashFlowAmountKzt === 'number'
            ? { netCashFlowAmountKzt: value.netCashFlowAmountKzt } : {}),
        canEdit: value.canEdit,
        ...(typeof value.canApprove === 'boolean' ? { canApprove: value.canApprove } : {}),
        ...publicCostCenterLabel(value),
    };
}
function publicListItem(value: unknown): BudgetInstanceListItem {
    const bounds = record(value) ? periodBounds(value.validFrom, value.validTo) : null;
    if (!record(value)
        || !isPublicExpenseRequestUuid(value.id)
        || !text(value.number, 40)
        || !isPublicExpenseRequestUuid(value.costCenterId)
        || bounds === null
        || (value.status !== 'Draft' && value.status !== 'Approved')
        || typeof value.lineCount !== 'number'
        || !Number.isInteger(value.lineCount)
        || value.lineCount < 1
        || !money(value.totalLimitAmountKzt)
        || !isPublicExpenseRequestUuid(value.concurrencyStamp)
        || (value.isEffective !== undefined && typeof value.isEffective !== 'boolean')
        || (value.canDelete !== undefined && typeof value.canDelete !== 'boolean')
        || (value.approvalState !== undefined && !['Pending', 'Returned', 'Rejected', 'Approved'].includes(String(value.approvalState)))
        || (value.canDelete === true && value.approvalState === 'Pending')
        || (value.isEffective === true && value.status !== 'Approved')
        || (value.canDelete === true && value.status !== 'Draft')) {
        throw new Error(invalidResponse);
    }
    if (value.authorName !== undefined && !text(value.authorName, 150))
        throw new Error(invalidResponse);
    let includedVersions: BudgetInstanceListItem['includedVersions'];
    if (value.includedVersions !== undefined) {
        if (!Array.isArray(value.includedVersions))
            throw new Error(invalidResponse);
        const ids = new Set<string>();
        includedVersions = value.includedVersions.map(version => {
            if (!record(version) || !isPublicExpenseRequestUuid(version.id) || !text(version.name, 200)
                || Object.keys(version).some(key => key !== 'id' && key !== 'name')
                || ids.has(version.id.toLowerCase()))
                throw new Error(invalidResponse);
            ids.add(version.id.toLowerCase());
            return { id: version.id, name: version.name };
        });
    }
    return {
        id: value.id,
        number: value.number,
        costCenterId: value.costCenterId,
        validFrom: bounds.validFrom,
        validTo: bounds.validTo,
        status: value.status,
        lineCount: value.lineCount,
        totalLimitAmountKzt: value.totalLimitAmountKzt,
        ...(money(value.totalReceiptsAmountKzt) ? { totalReceiptsAmountKzt: value.totalReceiptsAmountKzt } : {}),
        ...(money(value.totalPaymentsAmountKzt) ? { totalPaymentsAmountKzt: value.totalPaymentsAmountKzt } : {}),
        ...(signedMoneyCents(value.netCashFlowAmountKzt) !== null
            ? { netCashFlowAmountKzt: value.netCashFlowAmountKzt as number } : {}),
        concurrencyStamp: value.concurrencyStamp,
        ...(typeof value.isEffective === 'boolean' ? { isEffective: value.isEffective } : {}),
        ...(typeof value.canDelete === 'boolean' ? { canDelete: value.canDelete } : {}),
        ...(typeof value.approvalState === 'string' ? { approvalState: value.approvalState as BudgetInstanceListItem['approvalState'] } : {}),
        ...(typeof value.authorName === 'string' ? { authorName: value.authorName } : {}),
        ...(includedVersions !== undefined ? { includedVersions } : {}),
        ...publicCostCenterLabel(value, true),
    };
}
export function publicBudgetInstancePage(value: unknown, skip: number, take: number): BudgetInstancePage {
    if (!record(value)
        || !Number.isSafeInteger(skip)
        || skip < 0
        || !Number.isSafeInteger(take)
        || take < 1
        || !Array.isArray(value.items)
        || value.skip !== skip
        || value.take !== take
        || typeof value.total !== 'number'
        || !Number.isSafeInteger(value.total)
        || value.total < 0
        || value.items.length > take
        || value.items.length > Math.max(0, value.total - skip)) {
        throw new Error(invalidResponse);
    }
    const items = value.items.map(publicListItem);
    if (new Set(items.map((item) => item.id.toLowerCase())).size !== items.length) {
        throw new Error(invalidResponse);
    }
    return {
        items,
        skip,
        take,
        total: value.total,
    };
}
export async function listBudgetInstances(filters: {
    costCenterId?: string;
    periodFrom?: string;
    periodTo?: string;
    versionId?: string;
    skip?: number;
    take?: number;
} = {}, signal?: AbortSignal): Promise<BudgetInstancePage> {
    const skip = filters.skip ?? 0;
    const take = filters.take ?? 25;
    if (filters.versionId !== undefined && !isPublicExpenseRequestUuid(filters.versionId))
        throw new BudgetInstanceApiError(400);
    const params = new URLSearchParams();
    params.set('skip', String(skip));
    params.set('take', String(take));
    if (filters.costCenterId)
        params.set('costCenterId', filters.costCenterId);
    if (filters.periodFrom !== undefined)
        params.set('periodFrom', filters.periodFrom);
    if (filters.periodTo !== undefined)
        params.set('periodTo', filters.periodTo);
    if (filters.versionId !== undefined)
        params.set('versionId', filters.versionId);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/budget/instances?${params}`), { ...secureReadInit(), signal });
    if (response.status !== 200)
        await reject(response);
    return publicBudgetInstancePage(await response.json(), skip, take);
}
export async function getBudgetInstance(id: string, signal?: AbortSignal): Promise<BudgetInstance> {
    if (!isPublicExpenseRequestUuid(id))
        throw new BudgetInstanceApiError(400);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/budget/instances/${encodeURIComponent(id)}`), { ...secureReadInit(), signal });
    if (response.status !== 200)
        await reject(response);
    const result = publicBudgetInstance(await response.json());
    if (result.id.toLowerCase() !== id.toLowerCase())
        throw new Error(invalidResponse);
    // Fresh reads cannot be historical idempotency payloads. The public parser
    // stays backward-compatible only for stored create/update responses.
    if (result.costCenterCode === undefined || result.costCenterName === undefined) {
        throw new Error(invalidResponse);
    }
    return result;
}
function paging(skip: number, take: number): boolean {
    return Number.isSafeInteger(skip) && skip >= 0 && skip <= 2147483647
        && Number.isInteger(take) && take >= 1 && take <= 100;
}
export function publicBudgetInstanceCostCenterPage(value: unknown, skip: number, take: number): BudgetInstanceCostCenterPage {
    if (!record(value) || !paging(skip, take) || value.skip !== skip || value.take !== take
        || !Array.isArray(value.items) || typeof value.total !== 'number'
        || !Number.isSafeInteger(value.total) || value.total < 0 || value.total > 2147483647
        || value.items.length !== Math.min(take, Math.max(0, value.total - skip))) {
        throw new Error(invalidResponse);
    }
    const items = value.items.map(publicBudgetInstanceCostCenter);
    if (new Set(items.map(item => item.id.toLowerCase())).size !== items.length) {
        throw new Error(invalidResponse);
    }
    return { items, skip, take, total: value.total };
}
export function publicBudgetInstanceCostCenter(value: unknown): BudgetInstanceCostCenter {
    if (!record(value) || !isPublicExpenseRequestUuid(value.id)
        || !text(value.code, 100) || !text(value.name, 500) || value.isActive !== true) {
        throw new Error(invalidResponse);
    }
    return { id: value.id, code: value.code, name: value.name, isActive: true };
}
export async function listBudgetInstanceCostCenters(query: string, skip: number, take: number, validFrom: string, signal?: AbortSignal): Promise<BudgetInstanceCostCenterPage> {
    if (typeof query !== 'string' || query.trim().length > 200
        || /[\u0000-\u001f\u007f-\u009f]/u.test(query)
        || !paging(skip, take) || !month(validFrom)) {
        throw new BudgetInstanceApiError(400);
    }
    const parameters = new URLSearchParams({
        query: query.trim(), skip: String(skip), take: String(take), validFrom,
    });
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/budget/instances/cost-centers?${parameters}`), { ...secureReadInit(), signal });
    if (response.status !== 200)
        await reject(response);
    return publicBudgetInstanceCostCenterPage(await response.json(), skip, take);
}
export async function getCreatorBudgetInstanceCostCenter(validFrom: string, signal?: AbortSignal): Promise<BudgetInstanceCostCenter> {
    if (!month(validFrom))
        throw new BudgetInstanceApiError(400);
    const parameters = new URLSearchParams({ validFrom });
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/budget/instances/creator-cost-center?${parameters}`), { ...secureReadInit(), signal });
    if (response.status !== 200)
        await reject(response);
    return publicBudgetInstanceCostCenter(await response.json());
}
export function publicBudgetInstanceCreationOptions(value: unknown): BudgetInstanceCreationOptions {
    if (!record(value) || typeof value.canChooseAnyCostCenter !== 'boolean'
        || !Object.hasOwn(value, 'defaultCostCenter'))
        throw new Error(invalidResponse);
    return {
        canChooseAnyCostCenter: value.canChooseAnyCostCenter,
        defaultCostCenter: value.defaultCostCenter === null ? null : publicBudgetInstanceCostCenter(value.defaultCostCenter),
    };
}
export async function getBudgetInstanceCreationOptions(validFrom: string, signal?: AbortSignal): Promise<BudgetInstanceCreationOptions> {
    if (!month(validFrom))
        throw new BudgetInstanceApiError(400);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/budget/instances/creation-options?${new URLSearchParams({ validFrom })}`), { ...secureReadInit(), signal });
    if (response.status !== 200)
        await reject(response);
    return publicBudgetInstanceCreationOptions(await response.json());
}
/** All active authorised CFOs; do not truncate the selector to its first page. */
export async function listAllBudgetInstanceCostCenters(validFrom: string, signal?: AbortSignal): Promise<BudgetInstanceCostCenter[]> {
    const items: BudgetInstanceCostCenter[] = [];
    const seen = new Set<string>();
    let total: number | null = null;
    do {
        signal?.throwIfAborted();
        const page = await listBudgetInstanceCostCenters('', items.length, 100, validFrom, signal);
        if (total !== null && total !== page.total)
            throw new Error(invalidResponse);
        total = page.total;
        for (const item of page.items) {
            if (seen.has(item.id.toLowerCase()))
                throw new Error(invalidResponse);
            seen.add(item.id.toLowerCase());
            items.push(item);
        }
        if (page.items.length === 0 && items.length !== total)
            throw new Error(invalidResponse);
    } while (items.length < total);
    return items;
}
function requireKey(key: string): void {
    if (!text(key, 127))
        throw new BudgetInstanceApiError(400);
}
async function responseHasInvalidCsrfToken(response: Response): Promise<boolean> {
    if (response.status !== 403)
        return false;
    try {
        return (await readPublicError(response.clone()))?.code === 'csrf-token-invalid';
    }
    catch {
        return false;
    }
}
/** Project every field before the first await; unexpected runtime fields never leave the client. */
function inputLines(lines: readonly BudgetInstanceLineInput[], validFrom: string, validTo: string): BudgetInstanceLineInput[] {
    if (!Array.isArray(lines) || lines.length < 1)
        throw new BudgetInstanceApiError(400);
    let totalCents = 0;
    return Array.from(lines, line => {
        const comment = typeof line?.comment === 'string' ? line.comment.trim() : line?.comment;
        if (!line || !isPublicExpenseRequestUuid(line.cashFlowItemId)
            || !month(line.budgetMonth) || line.budgetMonth < validFrom || line.budgetMonth > validTo
            || !money(line.limitAmountKzt)
            || (comment !== undefined && comment !== '' && !budgetCommentText(comment))) {
            throw new BudgetInstanceApiError(400);
        }
        const direction = line.flowDirection ?? 'Payment';
        if (direction !== 'Payment' && direction !== 'Receipt')
            throw new BudgetInstanceApiError(400);
        const cents = moneyCents(line.limitAmountKzt);
        if (cents === null || !Number.isSafeInteger(totalCents + cents)) {
            throw new BudgetInstanceApiError(400);
        }
        totalCents += cents;
        return {
            cashFlowItemId: line.cashFlowItemId,
            budgetMonth: line.budgetMonth,
            limitAmountKzt: line.limitAmountKzt,
            ...(direction === 'Receipt' ? { flowDirection: direction } : {}),
            ...(comment ? { comment } : {}),
        };
    });
}
function matchesWrittenDraft(result: BudgetInstance, validFrom: string, validTo: string, lines: readonly BudgetInstanceLineInput[]): boolean {
    if (result.validFrom !== validFrom || result.validTo !== validTo || result.lines.length !== lines.length)
        return false;
    const orders = new Set<number>();
    return result.lines.every(line => {
        const expectedLine = lines[line.sortOrder];
        if (!expectedLine || orders.has(line.sortOrder))
            return false;
        orders.add(line.sortOrder);
        return expectedLine.cashFlowItemId.toLowerCase() === line.cashFlowItemId.toLowerCase()
            && expectedLine.budgetMonth === line.budgetMonth
            && (expectedLine.flowDirection ?? 'Payment') === (line.flowDirection ?? 'Payment')
            && moneyCents(expectedLine.limitAmountKzt) === moneyCents(line.limitAmountKzt)
            && (expectedLine.comment ?? '') === (line.comment ?? '');
    });
}
export async function createBudgetInstance(command: CreateBudgetInstanceInput, key: string, signal?: AbortSignal): Promise<BudgetInstance> {
    requireKey(key);
    if (!command || !isPublicExpenseRequestUuid(command.costCenterId)
        || periodBounds(command.validFrom, command.validTo) === null) {
        throw new BudgetInstanceApiError(400);
    }
    const body = {
        costCenterId: command.costCenterId,
        validFrom: command.validFrom,
        validTo: command.validTo,
        lines: inputLines(command.lines, command.validFrom, command.validTo),
    };
    const bodyText = JSON.stringify(body);
    const response = await fetchWithOneTimeRefresh(() => fetchWithSafeTransport(apiUrl('/api/v1/budget/instances'), {
        ...secureWriteInit('POST', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, bodyText),
        signal,
    }), responseHasInvalidCsrfToken, getCurrentSession);
    if (response.status !== 201 && response.status !== 200)
        await reject(response);
    const result = publicBudgetInstance(await response.json());
    if (result.costCenterId.toLowerCase() !== body.costCenterId.toLowerCase()
        || !matchesWrittenDraft(result, body.validFrom, body.validTo, body.lines)) {
        throw new Error(invalidResponse);
    }
    return result;
}
export async function updateBudgetInstance(id: string, command: UpdateBudgetInstanceInput, key: string, signal?: AbortSignal): Promise<BudgetInstance> {
    requireKey(key);
    if (!command || !isPublicExpenseRequestUuid(id)
        || !isPublicExpenseRequestUuid(command.expectedConcurrencyStamp)
        || periodBounds(command.validFrom, command.validTo) === null) {
        throw new BudgetInstanceApiError(400);
    }
    const body = {
        expectedConcurrencyStamp: command.expectedConcurrencyStamp,
        validFrom: command.validFrom,
        validTo: command.validTo,
        lines: inputLines(command.lines, command.validFrom, command.validTo),
    };
    const bodyText = JSON.stringify(body);
    const response = await fetchWithOneTimeRefresh(() => fetchWithSafeTransport(apiUrl(`/api/v1/budget/instances/${encodeURIComponent(id)}`), {
        ...secureWriteInit('PUT', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, bodyText),
        signal,
    }), responseHasInvalidCsrfToken, getCurrentSession);
    if (response.status !== 200)
        await reject(response);
    const result = publicBudgetInstance(await response.json());
    if (result.id.toLowerCase() !== id.toLowerCase()
        || result.concurrencyStamp.toLowerCase() === body.expectedConcurrencyStamp.toLowerCase()
        || !matchesWrittenDraft(result, body.validFrom, body.validTo, body.lines)) {
        throw new Error(invalidResponse);
    }
    return result;
}
export async function approveBudgetInstance(id: string, command: ApproveBudgetInstanceInput, key: string, signal?: AbortSignal): Promise<BudgetInstance> {
    requireKey(key);
    if (!isPublicExpenseRequestUuid(id)
        || !command
        || !isPublicExpenseRequestUuid(command.expectedConcurrencyStamp)) {
        throw new BudgetInstanceApiError(400);
    }
    const body = { expectedConcurrencyStamp: command.expectedConcurrencyStamp };
    const bodyText = JSON.stringify(body);
    const response = await fetchWithOneTimeRefresh(() => fetchWithSafeTransport(apiUrl(`/api/v1/budget/instances/${encodeURIComponent(id)}/approve`), {
        ...secureWriteInit('POST', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, bodyText),
        signal,
    }), responseHasInvalidCsrfToken, getCurrentSession);
    if (response.status !== 200)
        await reject(response);
    const result = publicBudgetInstance(await response.json());
    if (result.id.toLowerCase() !== id.toLowerCase()
        || result.status !== 'Approved'
        || result.canEdit
        || result.approvedAtUtc === undefined
        || result.approvedByEmployeeId === undefined
        || result.approvalOrdinal === undefined
        || result.concurrencyStamp.toLowerCase() === body.expectedConcurrencyStamp.toLowerCase()) {
        throw new Error(invalidResponse);
    }
    return result;
}
export function budgetInstanceDeletionErrorMessage(error: unknown): string {
    if (error instanceof BudgetInstanceApiError) {
        if (error.code === 'csrf-token-invalid' || error.status === 401)
            return budgetInstanceErrorMessage(error);
        if (error.status === 403)
            return 'Удаление доступно только финансовому директору.';
        if (error.status === 400)
            return 'Можно удалять только черновики с актуальной версией. Проверьте текущее состояние.';
        if (error.status === 404)
            return 'Экземпляр не найден, уже удалён или недоступен.';
        if (error.status === 409)
            return 'Экземпляр изменён. Удаление отклонено; проверьте текущее состояние.';
    }
    return 'Результат удаления не подтверждён. Повторите ту же попытку; новый запрос не создавайте.';
}
export async function deleteBudgetInstance(id: string, command: DeleteBudgetInstanceInput, key: string): Promise<void> {
    requireKey(key);
    if (!isPublicExpenseRequestUuid(id) || !command
        || !isPublicExpenseRequestUuid(command.costCenterId)
        || !isPublicExpenseRequestUuid(command.expectedConcurrencyStamp))
        throw new BudgetInstanceApiError(400);
    // Snapshot before the first await; do not send extra runtime properties.
    const bodyText = JSON.stringify({ costCenterId: command.costCenterId,
        expectedConcurrencyStamp: command.expectedConcurrencyStamp });
    const response = await fetchWithOneTimeRefresh(() => fetchWithSafeTransport(apiUrl(`/api/v1/budget/instances/${encodeURIComponent(id)}`), secureWriteInit('DELETE', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, bodyText)), responseHasInvalidCsrfToken, getCurrentSession);
    if (response.status !== 204)
        await reject(response);
}
