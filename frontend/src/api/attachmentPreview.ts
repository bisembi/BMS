import { apiUrl, fetchWithSafeTransport, secureWriteInit } from './http';
import { assertExpenseRequestIdentifier } from './expenseRequestIdentifier';
export type PreviewSection = {
    name: string;
    text: string | null;
    rows: string[][] | null;
};
export type DocumentPreview = {
    notice: string;
    sections: PreviewSection[];
};
export type AttachmentPreview = {
    kind: 'pdf' | 'image';
    blob: Blob;
} | {
    kind: 'text';
    text: string;
} | {
    kind: 'document';
    document: DocumentPreview;
};
const invalid = 'Не удалось безопасно открыть документ. Можно скачать оригинал.';
export const previewUnavailable = 'Просмотр этого формата или размера пока не поддерживается. Можно скачать оригинал.';
function documentPreview(value: unknown): DocumentPreview {
    if (!value || typeof value !== 'object')
        throw new Error(invalid);
    const doc = value as DocumentPreview;
    if (typeof doc.notice !== 'string' || doc.notice.length > 1000 || !Array.isArray(doc.sections)
        || doc.sections.length < 1 || doc.sections.length > 32)
        throw new Error(invalid);
    let characters = 0, cells = 0;
    for (const section of doc.sections) {
        if (!section || typeof section.name !== 'string' || section.name.length > 255
            || (section.text !== null && typeof section.text !== 'string')
            || (section.rows !== null && !Array.isArray(section.rows))
            || (section.rows === null) === (section.text === null))
            throw new Error(invalid);
        characters += section.text?.length ?? 0;
        if (section.rows) {
            if (section.rows.length > 2000)
                throw new Error(invalid);
            for (const row of section.rows) {
                if (!Array.isArray(row) || row.length > 100)
                    throw new Error(invalid);
                cells += row.length;
                for (const cell of row) {
                    if (typeof cell !== 'string')
                        throw new Error(invalid);
                    characters += cell.length;
                }
            }
        }
    }
    if (characters > 500000 || cells > 200000)
        throw new Error(invalid);
    return doc;
}
export async function getAttachmentPreview(id: string, attachmentId: string, staged: boolean, signal: AbortSignal): Promise<AttachmentPreview> {
    assertExpenseRequestIdentifier(id);
    assertExpenseRequestIdentifier(attachmentId);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${staged ? 'upload-sessions/' : ''}${id}/attachments/${attachmentId}/preview`), { ...secureWriteInit('POST', {}), signal });
    if (!response.ok)
        throw new Error(response.status === 415 ? previewUnavailable
            : response.status === 404 || response.status === 403 ? 'Вложение не найдено или доступ к нему изменился.'
                : response.status === 503 ? 'Просмотр временно недоступен. Повторите попытку позже.' : invalid);
    const type = response.headers.get('Content-Type')?.split(';')[0].trim().toLowerCase();
    if (!type || !['application/pdf', 'image/png', 'image/jpeg', 'image/gif', 'image/webp', 'text/plain',
        'application/vnd.bms.document-preview+json'].includes(type))
        throw new Error(invalid);
    const blob = await response.blob();
    if (blob.size < 1 || blob.size > 25 * 1024 * 1024)
        throw new Error(invalid);
    if (type === 'application/pdf')
        return { kind: 'pdf', blob };
    if (type.startsWith('image/'))
        return { kind: 'image', blob };
    if (type === 'text/plain') {
        const text = await blob.text();
        if (text.length > 500000)
            throw new Error(invalid);
        return { kind: 'text', text };
    }
    if (blob.size > 4000000)
        throw new Error(invalid);
    try {
        return { kind: 'document', document: documentPreview(JSON.parse(await blob.text())) };
    }
    catch {
        throw new Error(invalid);
    }
}
