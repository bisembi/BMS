import { ApiResponseError, readExpenseRequestConflictCode, startDiscardingBody } from './expenseRequestErrors';
import { newContractRequiresSaveCode, newContractRequiresSaveMessage, readExpenseAttachmentCode } from './expenseAttachmentProblems';
import { assertExpenseRequestIdentifier } from './expenseRequestIdentifier';
import { adoptPendingFingerprint, clearPendingCommand, selectPendingCommand, type PendingCommand } from './expenseRequestPendingCommands';
import { invalidExpenseRequestAttachmentResponseMessage, maximumExpenseRequestAttachmentSizeBytes, publicExpenseRequestAttachment, publicExpenseRequestAttachmentList, publicExpenseRequestAttachmentMutation, type ExpenseRequestAttachment, type ExpenseRequestAttachmentKind, type ExpenseRequestAttachmentList, type ExpenseRequestAttachmentMutation } from './expenseRequestAttachmentPublicResponse';
import { isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
import { apiUrl, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
const attachmentUnavailableMessage = 'Вложения сейчас недоступны.';
const invalidAttachmentInputMessage = 'Проверьте файл и параметры вложения.';
function attachmentBase(id: string, staged: boolean) {
    return `/api/v1/expense-requests/${staged ? 'upload-sessions/' : ''}${encodeURIComponent(id)}`;
}
export type AddExpenseRequestAttachmentInput = {
    expectedVersion: string;
    kind: ExpenseRequestAttachmentKind;
    file: File;
    comment?: string | null;
};
export type ExpenseRequestAttachmentExecutionResult = {
    mutation: ExpenseRequestAttachmentMutation;
    recovered: boolean;
};
export type ExpenseRequestAttachmentDownload = {
    blob: Blob;
    fileName: string;
};
class InvalidAttachmentResponseError extends Error {
    constructor() {
        super(invalidExpenseRequestAttachmentResponseMessage);
        this.name = 'InvalidAttachmentResponseError';
    }
}
function attachmentErrorMessage(status: number, code: string | null): string {
    if (status === 400 && code === newContractRequiresSaveCode)
        return newContractRequiresSaveMessage;
    if (code === 'idempotency-conflict') {
        return 'Ключ операции уже использован с другими данными. Обновите вложения и повторите.';
    }
    if (code === 'version-conflict') {
        return 'Заявка была изменена другим пользователем. Обновите её и повторите.';
    }
    if (status === 400)
        return 'Операция с вложением отклонена. Проверьте состояние заявки.';
    if (status === 401)
        return 'Для работы с вложениями требуется авторизация.';
    if (status === 403)
        return 'Недостаточно прав для этой операции с вложением.';
    if (status === 404)
        return 'Заявка или вложение не найдены либо недоступны.';
    if (status === 409)
        return 'Вложение конфликтует с текущим состоянием заявки.';
    if (status === 413)
        return 'Файл больше допустимого размера 25 МБ.';
    if (status === 422)
        return 'Файл отклонён проверкой безопасности.';
    if (status === 503)
        return 'Хранилище или проверка вложений временно недоступны.';
    return attachmentUnavailableMessage;
}
function rethrowCancellation(error: unknown): void {
    if (error instanceof DOMException && error.name === 'AbortError')
        throw error;
    if (error instanceof Error && error.name === 'AbortError')
        throw error;
}
function normalizeComment(value: string | null | undefined): string | null {
    if (value === null || value === undefined || value.trim() === '')
        return null;
    const normalized = value.trim();
    if (normalized.length > 1000 || /[\u0000-\u001f\u007f-\u009f]/u.test(normalized)) {
        throw new Error(invalidAttachmentInputMessage);
    }
    return normalized;
}
function validateFile(file: File): void {
    if (!(file instanceof File)
        || !Number.isSafeInteger(file.size)
        || file.size < 1
        || file.size > maximumExpenseRequestAttachmentSizeBytes
        || file.name.length < 1
        || file.name.length > 255
        || file.name !== file.name.trim()
        || /[. ]$/u.test(file.name)
        || /[\\/:*?"<>|\u0000-\u001f\u007f-\u009f]/u.test(file.name)) {
        throw new Error(invalidAttachmentInputMessage);
    }
}
async function fileSha256(file: File): Promise<string> {
    if (!crypto.subtle) {
        throw new Error('Браузер не поддерживает безопасную проверку повторной загрузки.');
    }
    const digest = await crypto.subtle.digest('SHA-256', await file.arrayBuffer());
    return [...new Uint8Array(digest)]
        .map((value) => value.toString(16).padStart(2, '0'))
        .join('');
}
async function readMutation(response: Response): Promise<ExpenseRequestAttachmentMutation> {
    try {
        return publicExpenseRequestAttachmentMutation(await response.json());
    }
    catch {
        throw new InvalidAttachmentResponseError();
    }
}
async function throwResponseError(response: Response): Promise<never> {
    const code = response.status === 400 ? await readExpenseAttachmentCode(response) : await readExpenseRequestConflictCode(response);
    if (response.status === 400 && !response.bodyUsed)
        startDiscardingBody(response);
    throw new ApiResponseError(attachmentErrorMessage(response.status, code), response.status, code);
}
async function requireSuccessfulResponse(response: Response): Promise<Response> {
    if (!response.ok) {
        await throwResponseError(response);
    }
    return response;
}
async function resolveMutation(id: string, path: string, command: PendingCommand, staged = false): Promise<ExpenseRequestAttachmentMutation | null> {
    const init = secureReadInit();
    const response = await fetchWithSafeTransport(apiUrl(`${attachmentBase(id, staged)}/${path}`), {
        ...init,
        headers: { ...init.headers, 'Idempotency-Key': command.key },
    });
    if (response.status === 204)
        return null;
    return readMutation(await requireSuccessfulResponse(response));
}
export async function getExpenseRequestAttachments(id: string, signal?: AbortSignal, staged = false): Promise<ExpenseRequestAttachmentList> {
    assertExpenseRequestIdentifier(id);
    try {
        const response = await requireSuccessfulResponse(await fetchWithSafeTransport(apiUrl(`${attachmentBase(id, staged)}/attachments`), { ...secureReadInit(), signal }));
        try {
            return publicExpenseRequestAttachmentList(await response.json());
        }
        catch {
            throw new InvalidAttachmentResponseError();
        }
    }
    catch (error) {
        rethrowCancellation(error);
        if (error instanceof ApiResponseError || error instanceof InvalidAttachmentResponseError) {
            throw error;
        }
        throw new Error(attachmentUnavailableMessage);
    }
}
export async function addExpenseRequestAttachment(id: string, input: AddExpenseRequestAttachmentInput, staged = false): Promise<ExpenseRequestAttachmentExecutionResult> {
    assertExpenseRequestIdentifier(id);
    if (!isPublicExpenseRequestUuid(input.expectedVersion)
        || !['SUPPORTING', 'NEW_CONTRACT', 'ADDITIONAL'].includes(input.kind)) {
        throw new Error(invalidAttachmentInputMessage);
    }
    validateFile(input.file);
    const comment = normalizeComment(input.comment);
    if ((input.kind === 'ADDITIONAL') !== (comment !== null)) {
        throw new Error(invalidAttachmentInputMessage);
    }
    const fingerprint = {
        expectedVersion: input.expectedVersion,
        kind: input.kind,
        fileName: input.file.name,
        fileSizeBytes: input.file.size,
        contentSha256: await fileSha256(input.file),
        comment,
    };
    const scope = `expense-request:attachment:${staged ? 'staged-' : ''}add:${id.toLocaleLowerCase('en-US')}`;
    const selected = await selectPendingCommand(scope, fingerprint);
    let command = selected.command;
    if (selected.requiresResolution) {
        const recovered = await resolveMutation(id, 'attachments/idempotency/add', command, staged);
        if (recovered) {
            if (recovered.attachment === null)
                throw new InvalidAttachmentResponseError();
            clearPendingCommand(scope, command.key);
            return { mutation: recovered, recovered: true };
        }
        command = adoptPendingFingerprint(scope, command, selected.requestedFingerprint);
    }
    const form = new FormData();
    form.append('expectedVersion', input.expectedVersion);
    form.append('kind', input.kind);
    if (comment !== null)
        form.append('comment', comment);
    form.append('file', input.file, input.file.name);
    try {
        const response = await requireSuccessfulResponse(await fetchWithSafeTransport(apiUrl(`${attachmentBase(id, staged)}/attachments`), secureWriteInit('POST', { 'Idempotency-Key': command.key }, form)));
        const mutation = await readMutation(response);
        if (mutation.attachment === null)
            throw new InvalidAttachmentResponseError();
        clearPendingCommand(scope, command.key);
        return { mutation, recovered: false };
    }
    catch (error) {
        if (error instanceof ApiResponseError && error.code === 'idempotency-conflict') {
            const recovered = await resolveMutation(id, 'attachments/idempotency/add', command, staged);
            if (recovered) {
                if (recovered.attachment === null)
                    throw new InvalidAttachmentResponseError();
                clearPendingCommand(scope, command.key);
                return { mutation: recovered, recovered: true };
            }
        }
        // These are definite refusals, not ambiguous transport/5xx outcomes. No
        // mutation committed; allow correction without discarding the selected File.
        if (error instanceof ApiResponseError && (error.code === 'version-conflict'
            || [400, 401, 403, 404, 413, 422].includes(error.status))) {
            clearPendingCommand(scope, command.key);
        }
        rethrowCancellation(error);
        if (error instanceof ApiResponseError || error instanceof InvalidAttachmentResponseError) {
            throw error;
        }
        throw new Error(attachmentUnavailableMessage);
    }
}
export async function deleteExpenseRequestAttachment(id: string, attachmentId: string, expectedVersion: string, staged = false): Promise<ExpenseRequestAttachmentExecutionResult> {
    assertExpenseRequestIdentifier(id);
    assertExpenseRequestIdentifier(attachmentId);
    if (!isPublicExpenseRequestUuid(expectedVersion)) {
        throw new Error(invalidAttachmentInputMessage);
    }
    const body = { expectedVersion };
    const scope = `expense-request:attachment:${staged ? 'staged-' : ''}delete:${id.toLocaleLowerCase('en-US')}:${attachmentId.toLocaleLowerCase('en-US')}`;
    const selected = await selectPendingCommand(scope, body);
    let command = selected.command;
    const resolutionPath = `attachments/${encodeURIComponent(attachmentId)}/idempotency/delete`;
    if (selected.requiresResolution) {
        const recovered = await resolveMutation(id, resolutionPath, command, staged);
        if (recovered) {
            if (recovered.attachment !== null)
                throw new InvalidAttachmentResponseError();
            clearPendingCommand(scope, command.key);
            return { mutation: recovered, recovered: true };
        }
        command = adoptPendingFingerprint(scope, command, selected.requestedFingerprint);
    }
    try {
        const response = await requireSuccessfulResponse(await fetchWithSafeTransport(apiUrl(`${attachmentBase(id, staged)}/attachments/${encodeURIComponent(attachmentId)}`), secureWriteInit('DELETE', { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, JSON.stringify(body))));
        const mutation = await readMutation(response);
        if (mutation.attachment !== null)
            throw new InvalidAttachmentResponseError();
        clearPendingCommand(scope, command.key);
        return { mutation, recovered: false };
    }
    catch (error) {
        if (error instanceof ApiResponseError && error.code === 'idempotency-conflict') {
            const recovered = await resolveMutation(id, resolutionPath, command, staged);
            if (recovered) {
                if (recovered.attachment !== null)
                    throw new InvalidAttachmentResponseError();
                clearPendingCommand(scope, command.key);
                return { mutation: recovered, recovered: true };
            }
        }
        if (error instanceof ApiResponseError && error.code === 'version-conflict') {
            clearPendingCommand(scope, command.key);
        }
        rethrowCancellation(error);
        if (error instanceof ApiResponseError || error instanceof InvalidAttachmentResponseError) {
            throw error;
        }
        throw new Error(attachmentUnavailableMessage);
    }
}
export async function downloadExpenseRequestAttachment(id: string, attachment: ExpenseRequestAttachment, staged = false): Promise<ExpenseRequestAttachmentDownload> {
    assertExpenseRequestIdentifier(id);
    const safeAttachment = publicExpenseRequestAttachment(attachment);
    assertExpenseRequestIdentifier(safeAttachment.id);
    try {
        const response = await requireSuccessfulResponse(await fetchWithSafeTransport(apiUrl(`${attachmentBase(id, staged)}/attachments/${encodeURIComponent(safeAttachment.id)}/content`), secureReadInit()));
        const blob = await response.blob();
        if (blob.size !== safeAttachment.fileSizeBytes
            || blob.size < 1
            || blob.size > maximumExpenseRequestAttachmentSizeBytes) {
            throw new InvalidAttachmentResponseError();
        }
        return { blob, fileName: safeAttachment.fileName };
    }
    catch (error) {
        rethrowCancellation(error);
        if (error instanceof ApiResponseError || error instanceof InvalidAttachmentResponseError) {
            throw error;
        }
        throw new Error(attachmentUnavailableMessage);
    }
}
