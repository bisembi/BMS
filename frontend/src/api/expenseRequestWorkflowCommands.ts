/// Workflow command discovery and execution. Discovery is a safe projection;
/// the server remains the authority and rechecks every command under a lock.
import { apiUrl, fetchWithOneTimeRefresh, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
import { getCurrentSession } from './session';
import { ApiResponseError, RecoveredPendingSaveError, expenseRequestWorkflowCommandErrorMessage as workflowHttpErrorMessage, readExpenseRequestConflictCode, responseHasInvalidExpenseCsrfToken } from './expenseRequestErrors';
import { adoptPendingFingerprint, clearPendingCommand, selectPendingCommand, type PendingCommand } from './expenseRequestPendingCommands';
import { expenseRequestWorkflowCommandCodes, invalidExpenseRequestWorkflowCommandResponseMessage, publicExpenseRequestWorkflowCommandAvailability, type ExpenseRequestWorkflowCommandAvailabilityResult, type ExpenseRequestWorkflowCommandCode } from './expenseRequestWorkflowCommandPublicResponse';
import { assertExpenseRequestIdentifier, isExpenseRequestIdentifier } from './expenseRequestIdentifier';
import { publicSavedExpenseRequest } from './expenseRequestPublicResponse';
import type { SavedExpenseRequest } from '../types';
const invalidWorkflowCommandCodeMessage = 'Некорректный код команды расходной заявки.';
const invalidWorkflowCommandReasonMessage = 'Некорректная причина команды расходной заявки.';
const invalidNsiIdentifierMessage = 'Идентификатор НСИ должен быть корректным GUID.';
const commandCodeSet: ReadonlySet<string> = new Set(expenseRequestWorkflowCommandCodes);
const commandsRequiringReason: ReadonlySet<ExpenseRequestWorkflowCommandCode> = new Set([
    'RETURN',
    'REJECT',
    'CANCEL',
]);
const cancellableStatuses: ReadonlySet<SavedExpenseRequest['status']> = new Set([
    'DRAFT',
    'RETURNED',
    'IN_APPROVAL',
    'AWAITING_NSI',
    'APPROVED',
    'TO_PAY',
]);
function isWorkflowCommandCode(value: string): value is ExpenseRequestWorkflowCommandCode {
    return commandCodeSet.has(value);
}
function normalizeReason(value: string | null | undefined): string | null {
    if (value == null || value.trim() === '')
        return null;
    if (value.length > 2000 || [...value].some((character) => /[\u0000-\u001f\u007f-\u009f]/u.test(character))) {
        throw new Error(invalidWorkflowCommandReasonMessage);
    }
    return value.trim();
}
function normalizeNsiIdentifier(value: string | null | undefined): string | null {
    if (value == null || value.trim() === '')
        return null;
    if (!isExpenseRequestIdentifier(value))
        throw new Error(invalidNsiIdentifierMessage);
    return value;
}
function commandBody(expectedVersion: string, reason: string | null, counterpartyId: string | null, contractId: string | null): Record<string, unknown> {
    return {
        expectedVersion,
        ...(reason === null ? {} : { reason }),
        ...(counterpartyId === null ? {} : { counterpartyId }),
        ...(contractId === null ? {} : { contractId }),
    };
}
function assertWorkflowCommandInput(id: string, commandCode: string, expectedVersion: string, reason: string | null | undefined, counterpartyId: string | null | undefined, contractId: string | null | undefined): {
    code: ExpenseRequestWorkflowCommandCode;
    body: Record<string, unknown>;
} {
    assertExpenseRequestIdentifier(id);
    if (!isWorkflowCommandCode(commandCode))
        throw new Error(invalidWorkflowCommandCodeMessage);
    if (!isExpenseRequestIdentifier(expectedVersion)) {
        throw new Error('Некорректная версия расходной заявки.');
    }
    const normalizedReason = normalizeReason(reason);
    if (commandsRequiringReason.has(commandCode) && normalizedReason === null) {
        throw new Error(invalidWorkflowCommandReasonMessage);
    }
    const normalizedCounterpartyId = normalizeNsiIdentifier(counterpartyId);
    const normalizedContractId = normalizeNsiIdentifier(contractId);
    if (commandCode !== 'CONFIRM_NSI'
        && (normalizedCounterpartyId !== null || normalizedContractId !== null)) {
        throw new Error(invalidNsiIdentifierMessage);
    }
    // W562-R. One link or both, never none. The server accepts a link only for a
    // candidate the request actually claimed and refuses one for anything else,
    // so demanding both here closed the confirmation for every request that
    // declared a single new object. Which of them is required is the request's
    // business, and the server remains the authority on it.
    if (commandCode === 'CONFIRM_NSI'
        && normalizedCounterpartyId === null
        && normalizedContractId === null) {
        throw new Error(invalidNsiIdentifierMessage);
    }
    return {
        code: commandCode,
        body: commandBody(expectedVersion, normalizedReason, normalizedCounterpartyId, normalizedContractId),
    };
}
async function resolveWorkflowCommand(resolutionUrl: string, command: PendingCommand, expectedRequestId: string): Promise<SavedExpenseRequest | null> {
    const secureRead = secureReadInit();
    const response = await fetchWithSafeTransport(apiUrl(resolutionUrl), {
        ...secureRead,
        method: 'GET',
        headers: {
            ...secureRead.headers,
            'Idempotency-Key': command.key,
        },
    });
    if (response.status === 204)
        return null;
    if (response.ok) {
        return publicSavedExpenseRequest(await response.json(), expectedRequestId);
    }
    const code = await readExpenseRequestConflictCode(response);
    throw new Error(workflowHttpErrorMessage(response.status, code));
}
async function sendWorkflowCommand(id: string, commandCode: ExpenseRequestWorkflowCommandCode, body: Record<string, unknown>): Promise<SavedExpenseRequest> {
    const encodedId = encodeURIComponent(id);
    const encodedCommand = encodeURIComponent(commandCode);
    const scope = `expense-request:workflow:${id.toLocaleLowerCase('en-US')}:${commandCode}`;
    const requestUrl = `/api/v1/expense-requests/${encodedId}/commands/${encodedCommand}`;
    const resolutionUrl = `${requestUrl}/idempotency`;
    const selected = await selectPendingCommand(scope, body);
    let command = selected.command;
    if (selected.requiresResolution) {
        const recovered = await resolveWorkflowCommand(resolutionUrl, command, id);
        if (recovered) {
            clearPendingCommand(scope, command.key);
            throw new RecoveredPendingSaveError(recovered);
        }
        command = adoptPendingFingerprint(scope, command, selected.requestedFingerprint);
    }
    const bodyText = JSON.stringify(body);
    try {
        const response = await fetchWithOneTimeRefresh(() => fetchWithSafeTransport(apiUrl(requestUrl), secureWriteInit('POST', {
            'Content-Type': 'application/json',
            'Idempotency-Key': command.key,
        }, bodyText)), responseHasInvalidExpenseCsrfToken, getCurrentSession);
        if (!response.ok) {
            const code = await readExpenseRequestConflictCode(response);
            throw new ApiResponseError(workflowHttpErrorMessage(response.status, code), response.status, code);
        }
        const saved = publicSavedExpenseRequest(await response.json(), id);
        clearPendingCommand(scope, command.key);
        return saved;
    }
    catch (error) {
        if (error instanceof ApiResponseError && error.code === 'idempotency-conflict') {
            const recovered = await resolveWorkflowCommand(resolutionUrl, command, id);
            if (recovered) {
                clearPendingCommand(scope, command.key);
                throw new RecoveredPendingSaveError(recovered);
            }
        }
        if (error instanceof ApiResponseError && error.code === 'version-conflict') {
            clearPendingCommand(scope, command.key);
        }
        throw error;
    }
}
export async function getExpenseRequestWorkflowCommands(id: string, signal?: AbortSignal): Promise<ExpenseRequestWorkflowCommandAvailabilityResult> {
    assertExpenseRequestIdentifier(id);
    const secureRead = secureReadInit();
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${encodeURIComponent(id)}/commands`), { ...secureRead, signal });
    if (!response.ok) {
        const code = await readExpenseRequestConflictCode(response);
        throw new Error(workflowHttpErrorMessage(response.status, code));
    }
    try {
        return publicExpenseRequestWorkflowCommandAvailability(await response.json());
    }
    catch {
        throw new Error(invalidExpenseRequestWorkflowCommandResponseMessage);
    }
}
export async function executeExpenseRequestWorkflowCommand(id: string, commandCode: string, expectedVersion: string, reason?: string | null, counterpartyId?: string | null, contractId?: string | null): Promise<SavedExpenseRequest> {
    const input = assertWorkflowCommandInput(id, commandCode, expectedVersion, reason, counterpartyId, contractId);
    return sendWorkflowCommand(id, input.code, input.body);
}
