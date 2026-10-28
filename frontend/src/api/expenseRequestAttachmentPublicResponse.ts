import { isPublicExpenseRequestTimestamp, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
export const maximumExpenseRequestAttachmentSizeBytes = 25 * 1024 * 1024;
export const invalidExpenseRequestAttachmentResponseMessage = 'Некорректный ответ API вложений расходной заявки.';
export type ExpenseRequestAttachmentKind = 'SUPPORTING' | 'NEW_CONTRACT' | 'ADDITIONAL';
export type ExpenseRequestAttachment = {
    id: string;
    kind: ExpenseRequestAttachmentKind;
    fileName: string;
    fileSizeBytes: number;
    uploadedAtUtc: string;
    comment: string | null;
    canDelete: boolean;
};
export type ExpenseRequestAttachmentList = {
    requestVersion: string;
    canAdd: boolean;
    requiresComment: boolean;
    items: ExpenseRequestAttachment[];
};
export type ExpenseRequestAttachmentMutation = {
    requestVersion: string;
    attachment: ExpenseRequestAttachment | null;
};
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function hasExactKeys(value: Record<string, unknown>, expected: readonly string[]): boolean {
    const actual = Object.keys(value).sort();
    const sortedExpected = [...expected].sort();
    return actual.length === sortedExpected.length
        && actual.every((key, index) => key === sortedExpected[index]);
}
function isSafeText(value: unknown, maximumLength: number): value is string {
    return typeof value === 'string'
        && value.length > 0
        && value.length <= maximumLength
        && value === value.trim()
        && !/[\u0000-\u001f\u007f-\u009f]/u.test(value);
}
function isSafeFileName(value: unknown): value is string {
    return isSafeText(value, 255)
        && !/[\\/:*?"<>|]/u.test(value)
        && !/[. ]$/u.test(value);
}
function isAttachmentKind(value: unknown): value is ExpenseRequestAttachmentKind {
    return value === 'SUPPORTING'
        || value === 'NEW_CONTRACT'
        || value === 'ADDITIONAL';
}
export function publicExpenseRequestAttachment(value: unknown): ExpenseRequestAttachment {
    if (!isRecord(value)
        || !hasExactKeys(value, [
            'id',
            'kind',
            'fileName',
            'fileSizeBytes',
            'uploadedAtUtc',
            'comment',
            'canDelete',
        ])
        || !isPublicExpenseRequestUuid(value.id)
        || !isAttachmentKind(value.kind)
        || !isSafeFileName(value.fileName)
        || typeof value.fileSizeBytes !== 'number'
        || !Number.isSafeInteger(value.fileSizeBytes)
        || value.fileSizeBytes < 1
        || value.fileSizeBytes > maximumExpenseRequestAttachmentSizeBytes
        || !isPublicExpenseRequestTimestamp(value.uploadedAtUtc)
        || !(value.uploadedAtUtc.endsWith('Z') || value.uploadedAtUtc.endsWith('+00:00'))
        || !(value.comment === null || isSafeText(value.comment, 1000))
        || typeof value.canDelete !== 'boolean') {
        throw new Error(invalidExpenseRequestAttachmentResponseMessage);
    }
    if ((value.kind === 'ADDITIONAL') !== (value.comment !== null)
        || (value.kind === 'ADDITIONAL' && value.canDelete)) {
        throw new Error(invalidExpenseRequestAttachmentResponseMessage);
    }
    return {
        id: value.id,
        kind: value.kind,
        fileName: value.fileName,
        fileSizeBytes: value.fileSizeBytes,
        uploadedAtUtc: value.uploadedAtUtc,
        comment: value.comment,
        canDelete: value.canDelete,
    };
}
export function publicExpenseRequestAttachmentList(payload: unknown): ExpenseRequestAttachmentList {
    if (!isRecord(payload)
        || !hasExactKeys(payload, ['requestVersion', 'canAdd', 'requiresComment', 'items'])
        || !isPublicExpenseRequestUuid(payload.requestVersion)
        || typeof payload.canAdd !== 'boolean'
        || typeof payload.requiresComment !== 'boolean'
        || !Array.isArray(payload.items)) {
        throw new Error(invalidExpenseRequestAttachmentResponseMessage);
    }
    const items = payload.items.map(publicExpenseRequestAttachment);
    const ids = new Set(items.map((item) => item.id.toLocaleLowerCase('en-US')));
    if (ids.size !== items.length
        || (payload.requiresComment && !payload.canAdd)) {
        throw new Error(invalidExpenseRequestAttachmentResponseMessage);
    }
    return {
        requestVersion: payload.requestVersion,
        canAdd: payload.canAdd,
        requiresComment: payload.requiresComment,
        items,
    };
}
export function publicExpenseRequestAttachmentMutation(payload: unknown): ExpenseRequestAttachmentMutation {
    if (!isRecord(payload)
        || !hasExactKeys(payload, ['requestVersion', 'attachment'])
        || !isPublicExpenseRequestUuid(payload.requestVersion)) {
        throw new Error(invalidExpenseRequestAttachmentResponseMessage);
    }
    return {
        requestVersion: payload.requestVersion,
        attachment: payload.attachment === null
            ? null
            : publicExpenseRequestAttachment(payload.attachment),
    };
}
