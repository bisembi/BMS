/// Expense request writes. Every command is idempotent: the key is chosen
/// before the request leaves, an ambiguous outcome is resolved against the
/// server, and only then is the pending record cleared.
import { apiUrl, fetchWithOneTimeRefresh, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
import { getCurrentSession } from './session';
import { ApiResponseError, RecoveredPendingSaveError, expenseRequestErrorMessage, readExpenseRequestConflictCode, responseHasInvalidExpenseCsrfToken } from './expenseRequestErrors';
import { adoptPendingFingerprint, clearPendingCommand, expenseRequestCopyPolicyVersion, selectPendingCommand, pendingCreateDraftCommand, pendingExpenseRequestSubmit, createDraftCommandScope, movePendingCreateDraftContext, type PendingCommand } from './expenseRequestPendingCommands';
import { canonicalizeExpenseRequestDraft, nullableGuid, nullableText, parsePositiveMoneyInput } from './expenseRequestCalculations';
import { publicSavedExpenseRequest } from './expenseRequestPublicResponse';
import { isPaymentOperation } from '../utils/expensePaymentOperation';
import { assertExpenseRequestIdentifier, isExpenseRequestIdentifier } from './expenseRequestIdentifier';
import type { ExpenseLineDraft, ExpenseRequestDraft, SavedExpenseRequest } from '../types';
const invalidExpenseRequestDraftMessage = 'Некорректные параметры черновика расходной заявки.';
const invalidExpenseRequestVersionMessage = 'Некорректная версия расходной заявки.';
const draftTextFields = [
    'organizationId',
    'defaultCostCenterId',
    'plannedPaymentDate',
    'currencyCode',
    'counterpartyId',
    'contractId',
    'purpose',
    'comment',
    'justification',
    'urgencyReason',
    'invoiceNumber',
    'invoiceDate',
    'publicOfferName',
    'publicOfferIdentifier',
    'otherBasisExplanation',
    'supportingDocumentMissingExplanation',
    'candidateCounterpartyName',
    'candidateBinIin',
    'candidateCountry',
    'candidateForeignRegistrationNumber',
    'candidateRegistrationNumberMissingExplanation',
    'candidateContractNumber',
    'candidateContractDate',
    'candidateContractSubject',
    'candidateContractTerms',
    'newContractDocumentMissingExplanation',
] as const satisfies readonly (keyof ExpenseRequestDraft)[];
const draftBooleanFields = [
    'isUrgent',
    'isNewCounterparty',
    'isNewContract',
    'supportingDocumentMissing',
    'candidateRegistrationNumberMissing',
    'candidateContractNumberMissing',
    'newContractDocumentMissing',
] as const satisfies readonly (keyof ExpenseRequestDraft)[];
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function isExpenseLineDraft(value: unknown): value is ExpenseLineDraft {
    if (!isRecord(value)
        || typeof value.cashFlowItemId !== 'string'
        || typeof value.costCenterId !== 'string'
        || typeof value.accrualPeriod !== 'string'
        || typeof value.amount !== 'string'
        || (value.id !== undefined && typeof value.id !== 'string')
        || (value.clientRowKey !== undefined && typeof value.clientRowKey !== 'string')) {
        return false;
    }
    return value.vatRateCode === 'Vat16' || value.vatRateCode === 'WithoutVat';
}
function isExpenseRequestDraft(value: unknown): value is ExpenseRequestDraft {
    try {
        if (!isRecord(value)
            || !draftTextFields.every((field) => typeof value[field] === 'string')
            || !draftBooleanFields.every((field) => typeof value[field] === 'boolean')
            || (value.paymentBasisCode !== ''
                && value.paymentBasisCode !== 'Contract'
                && value.paymentBasisCode !== 'InvoiceWithoutContract'
                && value.paymentBasisCode !== 'PublicOffer'
                && value.paymentBasisCode !== 'OtherBasis')
            || (value.candidateResidencyCode !== ''
                && value.candidateResidencyCode !== 'ResidentKz'
                && value.candidateResidencyCode !== 'NonResident')
            || !isPaymentOperation(value.paymentOperation)
            || !Array.isArray(value.lines)) {
            return false;
        }
        return value.lines.every(isExpenseLineDraft);
    }
    catch {
        return false;
    }
}
function assertExpenseRequestDraftInput(value: unknown): asserts value is ExpenseRequestDraft {
    if (!isExpenseRequestDraft(value)) {
        throw new Error(invalidExpenseRequestDraftMessage);
    }
}
function assertExpenseRequestVersion(value: unknown): asserts value is string {
    if (!isExpenseRequestIdentifier(value)) {
        throw new Error(invalidExpenseRequestVersionMessage);
    }
}
function commandIdentifier(value: string): string {
    return value.toLocaleLowerCase('en-US');
}
function requestBody(draft: ExpenseRequestDraft, expectedVersion?: string) {
    const normalized = canonicalizeExpenseRequestDraft(draft);
    return {
        ...(expectedVersion ? { expectedVersion } : {}),
        ...(normalized.paymentOperation ? { paymentOperation: normalized.paymentOperation } : {}),
        organizationId: nullableGuid(normalized.organizationId),
        defaultCostCenterId: nullableGuid(normalized.defaultCostCenterId),
        plannedPaymentDate: normalized.plannedPaymentDate || null,
        currencyCode: normalized.currencyCode,
        counterpartyId: nullableGuid(normalized.counterpartyId),
        contractId: nullableGuid(normalized.contractId),
        purpose: normalized.purpose || null,
        comment: normalized.comment || null,
        businessJustification: normalized.justification || null,
        isUrgent: normalized.isUrgent,
        urgencyReason: normalized.isUrgent ? normalized.urgencyReason || null : null,
        isNewCounterparty: normalized.isNewCounterparty,
        isNewContract: normalized.isNewContract,
        paymentBasisCode: normalized.paymentBasisCode || null,
        invoiceNumber: nullableText(normalized.invoiceNumber),
        invoiceDate: normalized.invoiceDate || null,
        publicOfferName: nullableText(normalized.publicOfferName),
        publicOfferIdentifier: nullableText(normalized.publicOfferIdentifier),
        otherBasisExplanation: nullableText(normalized.otherBasisExplanation),
        supportingDocumentMissing: normalized.supportingDocumentMissing,
        supportingDocumentMissingExplanation: nullableText(normalized.supportingDocumentMissingExplanation),
        candidateCounterpartyName: nullableText(normalized.candidateCounterpartyName),
        candidateResidencyCode: normalized.candidateResidencyCode || null,
        candidateBinIin: nullableText(normalized.candidateBinIin),
        candidateCountry: nullableText(normalized.candidateCountry),
        candidateForeignRegistrationNumber: nullableText(normalized.candidateForeignRegistrationNumber),
        candidateRegistrationNumberMissing: normalized.candidateRegistrationNumberMissing,
        candidateRegistrationNumberMissingExplanation: nullableText(normalized.candidateRegistrationNumberMissingExplanation),
        candidateContractNumber: nullableText(normalized.candidateContractNumber),
        candidateContractNumberMissing: normalized.candidateContractNumberMissing,
        candidateContractDate: normalized.candidateContractDate || null,
        candidateContractSubject: nullableText(normalized.candidateContractSubject),
        candidateContractTerms: nullableText(normalized.candidateContractTerms),
        newContractDocumentMissing: normalized.newContractDocumentMissing,
        newContractDocumentMissingExplanation: nullableText(normalized.newContractDocumentMissingExplanation),
        lines: normalized.lines
            .filter((line) => line.amount.trim() !== '')
            .map((line) => ({
            ...(expectedVersion ? { id: line.id ?? null } : {}),
            cashFlowItemId: nullableGuid(line.cashFlowItemId),
            costCenterId: nullableGuid(line.costCenterId),
            ...(line.accrualCostCenterId ? { accrualCostCenterId: nullableGuid(line.accrualCostCenterId) } : {}),
            accrualPeriod: line.accrualPeriod || null,
            amount: parsePositiveMoneyInput(line.amount),
            vatRateCode: line.vatRateCode,
        })),
    };
}
export async function responseOrError(response: Response, expectedRequestId?: string, options: {
    requireEditAuthority?: boolean;
} = {}): Promise<SavedExpenseRequest> {
    if (!response.ok) {
        const code = await readExpenseRequestConflictCode(response);
        throw new ApiResponseError(expenseRequestErrorMessage(response.status, code), response.status, code);
    }
    return publicSavedExpenseRequest(await response.json(), expectedRequestId, options);
}
async function resolvePendingCommand(resolutionUrl: string, command: PendingCommand, expectedRequestId?: string): Promise<SavedExpenseRequest | null> {
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
    throw new Error(`Не удалось проверить результат предыдущего сохранения (код ${response.status}). Новая команда не отправлена.`);
}
async function sendIdempotentDraftRequest(method: 'POST' | 'PUT', requestUrl: string, resolutionUrl: string, scope: string, body: unknown, expectedRequestId?: string): Promise<SavedExpenseRequest> {
    const selected = await selectPendingCommand(scope, body);
    let command = selected.command;
    if (selected.requiresResolution) {
        const recovered = await resolvePendingCommand(resolutionUrl, command, expectedRequestId);
        if (recovered) {
            clearPendingCommand(scope, command.key);
            throw new RecoveredPendingSaveError(recovered);
        }
        command = adoptPendingFingerprint(scope, command, selected.requestedFingerprint);
    }
    let response: Response | undefined;
    const bodyText = JSON.stringify(body);
    try {
        response = await fetchWithOneTimeRefresh(() => fetchWithSafeTransport(apiUrl(requestUrl), secureWriteInit(method, {
            'Content-Type': 'application/json',
            'Idempotency-Key': command.key,
        }, bodyText)), responseHasInvalidExpenseCsrfToken, getCurrentSession);
        const saved = await responseOrError(response, expectedRequestId);
        clearPendingCommand(scope, command.key);
        return saved;
    }
    catch (error) {
        if (error instanceof ApiResponseError && error.code === 'idempotency-conflict') {
            const recovered = await resolvePendingCommand(resolutionUrl, command, expectedRequestId);
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
async function sendIdempotentCopyRequest(sourceId: string, fullCopy = false): Promise<SavedExpenseRequest> {
    const encodedId = encodeURIComponent(sourceId);
    const normalizedSourceId = commandIdentifier(sourceId);
    const scope = `expense-request:${fullCopy ? 'copy-complete' : 'copy-draft'}:${normalizedSourceId}`;
    const requestUrl = `/api/v1/expense-requests/${encodedId}/${fullCopy ? 'copy-complete' : 'copy'}`;
    const resolutionUrl = `/api/v1/expense-requests/${encodedId}/idempotency/${fullCopy ? 'copy-complete' : 'copy-draft'}`;
    const logicalCommand = {
        sourceId: normalizedSourceId,
        policy: fullCopy ? 'copy-complete-v1' : expenseRequestCopyPolicyVersion,
    };
    const selected = await selectPendingCommand(scope, logicalCommand);
    let command = selected.command;
    if (selected.requiresResolution) {
        const recovered = await resolvePendingCommand(resolutionUrl, command);
        if (recovered) {
            clearPendingCommand(scope, command.key);
            return recovered;
        }
        command = adoptPendingFingerprint(scope, command, selected.requestedFingerprint);
    }
    try {
        const response = await fetchWithOneTimeRefresh(() => fetchWithSafeTransport(apiUrl(requestUrl), secureWriteInit('POST', {
            'Idempotency-Key': command.key,
        })), responseHasInvalidExpenseCsrfToken, getCurrentSession);
        const copied = await responseOrError(response);
        clearPendingCommand(scope, command.key);
        return copied;
    }
    catch (error) {
        if (error instanceof ApiResponseError && error.code === 'idempotency-conflict') {
            const recovered = await resolvePendingCommand(resolutionUrl, command);
            if (recovered) {
                clearPendingCommand(scope, command.key);
                return recovered;
            }
        }
        if (error instanceof ApiResponseError && error.code === 'version-conflict') {
            clearPendingCommand(scope, command.key);
        }
        throw error;
    }
}
export async function recoverPendingCreateDraft(preferredLeaseId?: string): Promise<void> {
    const pending = pendingCreateDraftCommand(preferredLeaseId);
    if (!pending)
        return;
    const recovered = await resolvePendingCommand('/api/v1/expense-requests/idempotency/create-draft', pending.command);
    if (recovered) {
        clearPendingCommand(pending.scope, pending.command.key);
        throw new RecoveredPendingSaveError(recovered);
    }
    // Keep the original key for an orphaned/uncommitted attempt. A late original commit
    // must conflict/recover under that same key rather than become a duplicate document.
    movePendingCreateDraftContext(pending.scope, createDraftCommandScope(preferredLeaseId), pending.command);
}
export async function recoverPendingExpenseRequestSubmit(id: string): Promise<void> {
    assertExpenseRequestIdentifier(id);
    const pending = pendingExpenseRequestSubmit(id);
    if (!pending)
        return;
    const recovered = await resolvePendingCommand(`/api/v1/expense-requests/${encodeURIComponent(id)}/idempotency/submit`, pending.command, id);
    if (recovered) {
        clearPendingCommand(pending.scope, pending.command.key);
        throw new RecoveredPendingSaveError(recovered);
    }
    // A 204 is not proof that an in-flight original will never commit. Keep its
    // key, so any later Submit resolves/replays the same logical command.
}
export async function createDraft(draft: ExpenseRequestDraft, numberLeaseId?: string, attachmentUploadSessionId?: string): Promise<SavedExpenseRequest> {
    assertExpenseRequestDraftInput(draft);
    const body = { ...requestBody(draft), ...(numberLeaseId ? { numberLeaseId } : {}),
        ...(attachmentUploadSessionId ? { attachmentUploadSessionId } : {}) };
    const scope = createDraftCommandScope(numberLeaseId);
    return sendIdempotentDraftRequest('POST', '/api/v1/expense-requests', '/api/v1/expense-requests/idempotency/create-draft', scope, body);
}
export async function updateDraft(id: string, expectedVersion: string, draft: ExpenseRequestDraft): Promise<SavedExpenseRequest> {
    assertExpenseRequestIdentifier(id);
    assertExpenseRequestVersion(expectedVersion);
    assertExpenseRequestDraftInput(draft);
    const body = requestBody(draft, expectedVersion);
    const scope = `expense-request:update-draft:${commandIdentifier(id)}`;
    const encodedId = encodeURIComponent(id);
    return sendIdempotentDraftRequest('PUT', `/api/v1/expense-requests/${encodedId}`, `/api/v1/expense-requests/${encodedId}/idempotency/update-draft`, scope, body, id);
}
export async function submitExpenseRequest(id: string, expectedVersion: string): Promise<SavedExpenseRequest> {
    assertExpenseRequestIdentifier(id);
    assertExpenseRequestVersion(expectedVersion);
    const encodedId = encodeURIComponent(id);
    const scope = `expense-request:submit:${commandIdentifier(id)}`;
    return sendIdempotentDraftRequest('POST', `/api/v1/expense-requests/${encodedId}/submit`, `/api/v1/expense-requests/${encodedId}/idempotency/submit`, scope, { expectedVersion }, id);
}
export async function copyExpenseRequest(sourceId: string): Promise<SavedExpenseRequest> {
    assertExpenseRequestIdentifier(sourceId);
    return sendIdempotentCopyRequest(sourceId);
}
export async function copyCompleteExpenseRequest(sourceId: string): Promise<SavedExpenseRequest> {
    assertExpenseRequestIdentifier(sourceId);
    return sendIdempotentCopyRequest(sourceId, true);
}
export async function deleteExpenseRequestDraft(id: string, expectedVersion: string): Promise<void> {
    assertExpenseRequestIdentifier(id);
    assertExpenseRequestIdentifier(expectedVersion);
    const scope = `expense-request:delete-draft:${commandIdentifier(id)}`;
    const body = { expectedVersion: commandIdentifier(expectedVersion) };
    const selected = await selectPendingCommand(scope, body);
    let command = selected.command;
    const resolve = async () => {
        const read = secureReadInit();
        const result = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${encodeURIComponent(id)}/idempotency/delete-draft`), { ...read, headers: { ...read.headers, 'Idempotency-Key': command.key } });
        if (result.status === 204)
            return false;
        if (result.status === 200) {
            const value = await result.json();
            if (isRecord(value) && value.deleted === true && typeof value.id === 'string'
                && commandIdentifier(value.id) === commandIdentifier(id))
                return true;
        }
        throw new Error('Результат предыдущего удаления не подтверждён. Повторите попытку.');
    };
    if (selected.requiresResolution) {
        if (await resolve()) {
            clearPendingCommand(scope, command.key);
            return;
        }
        command = adoptPendingFingerprint(scope, command, selected.requestedFingerprint);
    }
    const response = await fetchWithOneTimeRefresh(() => fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${encodeURIComponent(id)}`), secureWriteInit('DELETE', {
        'Content-Type': 'application/json', 'Idempotency-Key': command.key,
    }, JSON.stringify(body))), responseHasInvalidExpenseCsrfToken, getCurrentSession);
    if (response.status === 204) {
        clearPendingCommand(scope, command.key);
        return;
    }
    const code = await readExpenseRequestConflictCode(response);
    if (code === 'idempotency-conflict' && await resolve()) {
        clearPendingCommand(scope, command.key);
        return;
    }
    if (response.status === 409 && code !== 'idempotency-conflict')
        clearPendingCommand(scope, command.key);
    throw new ApiResponseError(expenseRequestErrorMessage(response.status, code), response.status, code);
}
