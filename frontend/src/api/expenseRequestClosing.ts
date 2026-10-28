import { apiUrl, fetchWithOneTimeRefresh, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
import { getCurrentSession } from './session';
import { ApiResponseError, readExpenseRequestConflictCode, responseHasInvalidExpenseCsrfToken, type ExpenseRequestConflictCode } from './expenseRequestErrors';
import { adoptPendingFingerprint, clearPendingCommand, selectPendingCommand, type PendingCommand } from './expenseRequestPendingCommands';
import { invalidExpenseRequestClosingResponseMessage, publicExpenseRequestClosingState, type ExpenseRequestClosingState } from './expenseRequestClosingPublicResponse';
import { isPublicExpenseRequestDate, isPublicExpenseRequestMoney, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
import { assertExpenseRequestIdentifier } from './expenseRequestIdentifier';
export type ExpenseRequestClosingCommandCode = 'CLOSE_PARTIAL' | 'CLOSE_FULL' | 'REOPEN';
export type ExpenseRequestClosingLineAllocationInput = {
    lineId: string;
    amount: number;
};
export type CloseExpenseRequestPartiallyInput = {
    expectedVersion: string;
    documentsReceivedDate: string;
    controlAmount: number;
    reason: string;
    lineAllocations?: ExpenseRequestClosingLineAllocationInput[];
};
export type CloseExpenseRequestFullyInput = {
    expectedVersion: string;
    documentsReceivedDate: string;
    reason: string;
};
export type ReopenExpenseRequestInput = {
    expectedVersion: string;
    reason: string;
};
export type ExpenseRequestClosingExecutionResult = {
    state: ExpenseRequestClosingState;
    recovered: boolean;
};
const closingUnavailableMessage = 'Сведения о закрытии заявки сейчас недоступны.';
const invalidClosingInputMessage = 'Проверьте данные закрытия заявки.';
class InvalidExpenseRequestClosingResponseError extends Error {
    constructor() {
        super(invalidExpenseRequestClosingResponseMessage);
        this.name = 'InvalidExpenseRequestClosingResponseError';
    }
}
const closingCommandPaths: Record<ExpenseRequestClosingCommandCode, string> = {
    CLOSE_PARTIAL: 'partial',
    CLOSE_FULL: 'full',
    REOPEN: 'reopen',
};
function normalizeReason(value: string): string {
    const normalized = value.trim();
    if (normalized.length === 0
        || normalized.length > 2000
        || /[\u0000-\u001f\u007f-\u009f]/u.test(normalized)) {
        throw new Error(invalidClosingInputMessage);
    }
    return normalized;
}
function normalizeVersion(value: string): string {
    if (!isPublicExpenseRequestUuid(value))
        throw new Error(invalidClosingInputMessage);
    return value;
}
function normalizePartialInput(input: CloseExpenseRequestPartiallyInput): Record<string, unknown> {
    if (!isPublicExpenseRequestDate(input.documentsReceivedDate)
        || !isPublicExpenseRequestMoney(input.controlAmount, false)) {
        throw new Error(invalidClosingInputMessage);
    }
    let lineAllocations: ExpenseRequestClosingLineAllocationInput[] | undefined;
    if (input.lineAllocations !== undefined) {
        const lineIds = new Set<string>();
        lineAllocations = input.lineAllocations.map((allocation) => {
            if (!isPublicExpenseRequestUuid(allocation.lineId)
                || !isPublicExpenseRequestMoney(allocation.amount)) {
                throw new Error(invalidClosingInputMessage);
            }
            const canonicalId = allocation.lineId.toLocaleLowerCase('en-US');
            if (lineIds.has(canonicalId))
                throw new Error(invalidClosingInputMessage);
            lineIds.add(canonicalId);
            return { lineId: allocation.lineId, amount: allocation.amount };
        }).sort((left, right) => left.lineId.localeCompare(right.lineId, 'en-US'));
    }
    return {
        expectedVersion: normalizeVersion(input.expectedVersion),
        documentsReceivedDate: input.documentsReceivedDate,
        controlAmount: input.controlAmount,
        reason: normalizeReason(input.reason),
        ...(lineAllocations === undefined ? {} : { lineAllocations }),
    };
}
function normalizeFullInput(input: CloseExpenseRequestFullyInput): Record<string, unknown> {
    if (!isPublicExpenseRequestDate(input.documentsReceivedDate)) {
        throw new Error(invalidClosingInputMessage);
    }
    return {
        expectedVersion: normalizeVersion(input.expectedVersion),
        documentsReceivedDate: input.documentsReceivedDate,
        reason: normalizeReason(input.reason),
    };
}
function normalizeReopenInput(input: ReopenExpenseRequestInput): Record<string, unknown> {
    return {
        expectedVersion: normalizeVersion(input.expectedVersion),
        reason: normalizeReason(input.reason),
    };
}
function closingCommandErrorMessage(status: number, code: ExpenseRequestConflictCode | null): string {
    if (status === 403 && code === 'csrf-token-invalid') {
        return 'Сеанс безопасности устарел. Повторите команду закрытия заявки.';
    }
    if (code === 'idempotency-conflict') {
        return 'Ключ операции уже использован с другими данными. Обновите заявку и повторите команду.';
    }
    if (code === 'version-conflict') {
        return 'Заявка была изменена другим пользователем. Обновите данные и повторите команду.';
    }
    if (status === 400)
        return 'Команда закрытия отклонена. Проверьте введённые данные и состояние заявки.';
    if (status === 401)
        return 'Для выполнения команды закрытия требуется авторизация.';
    if (status === 403)
        return 'Недостаточно прав для выполнения команды закрытия.';
    if (status === 404)
        return 'Расходная заявка не найдена или недоступна.';
    if (status === 409)
        return 'Команда конфликтует с текущим состоянием заявки. Обновите данные.';
    if (status === 503)
        return 'Закрытие заявки временно недоступно. Повторите попытку позднее.';
    return 'Не удалось выполнить команду закрытия заявки.';
}
async function resolveClosingCommand(id: string, code: ExpenseRequestClosingCommandCode, command: PendingCommand): Promise<ExpenseRequestClosingState | null> {
    const secureRead = secureReadInit();
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${encodeURIComponent(id)}/closing/${encodeURIComponent(code)}/idempotency`), {
        ...secureRead,
        headers: { ...secureRead.headers, 'Idempotency-Key': command.key },
    });
    if (response.status === 204)
        return null;
    if (response.ok) {
        try {
            return publicExpenseRequestClosingState(await response.json(), id);
        }
        catch {
            throw new InvalidExpenseRequestClosingResponseError();
        }
    }
    const conflictCode = await readExpenseRequestConflictCode(response);
    throw new ApiResponseError(closingCommandErrorMessage(response.status, conflictCode), response.status, conflictCode);
}
async function sendClosingCommand(id: string, code: ExpenseRequestClosingCommandCode, body: Record<string, unknown>): Promise<ExpenseRequestClosingExecutionResult> {
    assertExpenseRequestIdentifier(id);
    const scope = `expense-request:closing:${id.toLocaleLowerCase('en-US')}:${code}`;
    const selected = await selectPendingCommand(scope, body);
    let command = selected.command;
    if (selected.requiresResolution) {
        const recovered = await resolveClosingCommand(id, code, command);
        if (recovered) {
            clearPendingCommand(scope, command.key);
            return { state: recovered, recovered: true };
        }
        command = adoptPendingFingerprint(scope, command, selected.requestedFingerprint);
    }
    const path = closingCommandPaths[code];
    const bodyText = JSON.stringify(body);
    try {
        const response = await fetchWithOneTimeRefresh(() => fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${encodeURIComponent(id)}/closing/${path}`), secureWriteInit('POST', {
            'Content-Type': 'application/json',
            'Idempotency-Key': command.key,
        }, bodyText)), responseHasInvalidExpenseCsrfToken, getCurrentSession);
        if (!response.ok) {
            const conflictCode = await readExpenseRequestConflictCode(response);
            throw new ApiResponseError(closingCommandErrorMessage(response.status, conflictCode), response.status, conflictCode);
        }
        let state: ExpenseRequestClosingState;
        try {
            state = publicExpenseRequestClosingState(await response.json(), id);
        }
        catch {
            throw new InvalidExpenseRequestClosingResponseError();
        }
        clearPendingCommand(scope, command.key);
        return { state, recovered: false };
    }
    catch (error) {
        if (error instanceof ApiResponseError && error.code === 'idempotency-conflict') {
            const recovered = await resolveClosingCommand(id, code, command);
            if (recovered) {
                clearPendingCommand(scope, command.key);
                return { state: recovered, recovered: true };
            }
        }
        if (error instanceof ApiResponseError && error.code === 'version-conflict') {
            clearPendingCommand(scope, command.key);
        }
        if (error instanceof InvalidExpenseRequestClosingResponseError
            || error instanceof ApiResponseError) {
            throw error;
        }
        throw new Error(closingUnavailableMessage);
    }
}
export async function getExpenseRequestClosingState(id: string, signal?: AbortSignal): Promise<ExpenseRequestClosingState> {
    assertExpenseRequestIdentifier(id);
    try {
        const response = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${encodeURIComponent(id)}/closing`), { ...secureReadInit(), signal });
        if (!response.ok)
            throw new Error(closingUnavailableMessage);
        return publicExpenseRequestClosingState(await response.json(), id);
    }
    catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError')
            throw error;
        if (error instanceof Error && error.name === 'AbortError')
            throw error;
        throw new Error(closingUnavailableMessage);
    }
}
export async function closeExpenseRequestPartially(id: string, input: CloseExpenseRequestPartiallyInput): Promise<ExpenseRequestClosingExecutionResult> {
    return sendClosingCommand(id, 'CLOSE_PARTIAL', normalizePartialInput(input));
}
export async function closeExpenseRequestFully(id: string, input: CloseExpenseRequestFullyInput): Promise<ExpenseRequestClosingExecutionResult> {
    return sendClosingCommand(id, 'CLOSE_FULL', normalizeFullInput(input));
}
export async function reopenExpenseRequest(id: string, input: ReopenExpenseRequestInput): Promise<ExpenseRequestClosingExecutionResult> {
    return sendClosingCommand(id, 'REOPEN', normalizeReopenInput(input));
}
