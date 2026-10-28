import { startDiscardingBody } from './expenseRequestErrors';
import { apiUrl, csrfHeaders, fetchWithSafeTransport, secureReadInit } from './http';
import type { AttachmentUploadIntentRejectionCode, AttachmentUploadIntentResult } from '../types';
const maximumFileSizeBytes = 25 * 1024 * 1024;
const executableExtensions = new Set([
    'exe',
    'com',
    'bat',
    'cmd',
    'msi',
    'ps1',
    'vbs',
    'js',
    'jse',
    'wsf',
    'wsh',
    'scr',
    'pif',
    'cpl',
    'hta',
]);
const rejectionCodes: ReadonlySet<string> = new Set([
    'MissingFileName',
    'EmptyFile',
    'FileTooLarge',
    'InvalidFileName',
    'ExecutableExtension',
]);
const invalidAttachmentInputMessage = 'Некорректные параметры файла вложения.';
type AttachmentCandidate = Pick<File, 'name' | 'size'>;
function readAttachmentCandidate(value: unknown): AttachmentCandidate | null {
    if (typeof value !== 'object' || value === null || Array.isArray(value))
        return null;
    try {
        const name = Reflect.get(value, 'name');
        const size = Reflect.get(value, 'size');
        if (typeof name !== 'string'
            || typeof size !== 'number'
            || !Number.isSafeInteger(size)
            || size < 0) {
            return null;
        }
        return { name, size };
    }
    catch {
        return null;
    }
}
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function isAttachmentUploadIntentRejectionCode(value: unknown): value is AttachmentUploadIntentRejectionCode {
    return typeof value === 'string' && rejectionCodes.has(value);
}
function accepted(): AttachmentUploadIntentResult {
    return {
        isAccepted: true,
        rejectionCode: null,
        maximumFileSizeBytes,
    };
}
function rejected(rejectionCode: AttachmentUploadIntentRejectionCode): AttachmentUploadIntentResult {
    return {
        isAccepted: false,
        rejectionCode,
        maximumFileSizeBytes,
    };
}
function publicAttachmentUploadIntent(payload: unknown): AttachmentUploadIntentResult {
    if (!isRecord(payload)) {
        throw new Error('Некорректный ответ проверки файла вложения.');
    }
    const propertyNames = Object.keys(payload).sort();
    if (propertyNames.length !== 3
        || propertyNames[0] !== 'isAccepted'
        || propertyNames[1] !== 'maximumFileSizeBytes'
        || propertyNames[2] !== 'rejectionCode') {
        throw new Error('Некорректный ответ проверки файла вложения.');
    }
    if (typeof payload.isAccepted !== 'boolean'
        || typeof payload.maximumFileSizeBytes !== 'number'
        || payload.maximumFileSizeBytes !== maximumFileSizeBytes) {
        throw new Error('Некорректный ответ проверки файла вложения.');
    }
    const rejectionCode = payload.rejectionCode;
    if (payload.isAccepted) {
        if (rejectionCode !== null) {
            throw new Error('Некорректный ответ проверки файла вложения.');
        }
        return accepted();
    }
    if (!isAttachmentUploadIntentRejectionCode(rejectionCode)) {
        throw new Error('Некорректный ответ проверки файла вложения.');
    }
    return rejected(rejectionCode);
}
export async function validateAttachmentUploadIntent(file: AttachmentCandidate, signal?: AbortSignal): Promise<AttachmentUploadIntentResult> {
    const candidate = readAttachmentCandidate(file);
    if (candidate === null) {
        throw new Error(invalidAttachmentInputMessage);
    }
    // This route is a POST that does not go through `secureWriteInit`, so the
    // antiforgery header has to be named here as well. It is the only write in
    // the client that builds its own init.
    const init = secureReadInit();
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/expense-requests/attachments/upload-intent'), {
        ...init,
        signal,
        method: 'POST',
        headers: {
            ...init.headers,
            ...csrfHeaders(),
            'Content-Type': 'application/json',
        },
        body: JSON.stringify({
            fileName: candidate.name,
            fileSizeBytes: candidate.size,
        }),
    });
    if (!response.ok) {
        // This route never reaches `readExpenseRequestConflictCode`, so its refusal
        // body would otherwise stay unread and hold the request open after the user
        // has already been told the file was refused. Started, never awaited: the
        // status is already known, so a body that stalls forever cannot delay the
        // fixed message, and a failed read is swallowed rather than surfaced.
        startDiscardingBody(response);
        throw new Error(`Не удалось проверить файл вложения (код ${response.status}).`);
    }
    return publicAttachmentUploadIntent(await response.json());
}
