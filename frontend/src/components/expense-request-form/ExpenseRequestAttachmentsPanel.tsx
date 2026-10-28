import { useWorkspaceRefreshRevision, useWorkspaceRefreshGuard, useWorkspaceRefreshBusy } from '../WorkspaceRefreshContext';
import { useEffect, useRef, useState } from 'react';
import { AttachmentPreviewDialog } from './AttachmentPreviewDialog';
import { useAttachmentPreviewWorkspace } from './AttachmentPreviewWorkspace';
import './ExpenseRequestAttachmentsPanel.css';
import { ExpenseReadField } from './ExpenseRequestReadView';
import { addExpenseRequestAttachment, deleteExpenseRequestAttachment, downloadExpenseRequestAttachment, getExpenseRequestAttachments, type ExpenseRequestAttachment, type ExpenseRequestAttachmentKind, type ExpenseRequestAttachmentList } from '../../api';
import { saveBlobDownload } from '../../utils/saveBlobDownload';
import { hasPendingExpenseRequestCommand } from '../../api/expenseRequestPendingCommands';
import { attachmentLifecycleErrorMessage } from '../../utils/uiErrorMessages';
import type { AttachmentUploadIntentRejectionCode, AttachmentUploadIntentResult, ExpenseRequestDraft, SubmissionReadiness } from '../../types';
const rejectionLabels: Record<AttachmentUploadIntentRejectionCode, string> = {
    MissingFileName: 'Не удалось определить имя файла.',
    EmptyFile: 'Пустой файл нельзя приложить.',
    FileTooLarge: 'Файл больше 25 МБ.',
    InvalidFileName: 'Некорректное имя файла.',
    ExecutableExtension: 'Исполняемые файлы нельзя прикладывать.',
};
// Upload-intent is a metadata-only early check. Actual persistence always
// sends the exact File to the authenticated attachment endpoint, where the
// server repeats policy validation and scans the bytes before saving them.
const uploadIntentTimeoutMs = 10000;
type UploadIntentState = {
    kind: 'idle';
} | {
    kind: 'checking';
} | {
    kind: 'accepted';
} | {
    kind: 'rejected';
    rejectionCode: AttachmentUploadIntentRejectionCode;
} | {
    kind: 'failed';
};
type AttachmentBatch = {
    contextId: string;
    expectedVersion: string;
    kind: ExpenseRequestAttachmentKind;
    files: File[];
    total: number;
    uploaded: number;
};
const kindLabels: Record<ExpenseRequestAttachmentKind, string> = {
    SUPPORTING: 'Подтверждающий документ',
    NEW_CONTRACT: 'Новый договор',
    ADDITIONAL: 'Дополнительный документ',
};
// Preserve the existing append-only audit contract, including old-tab/retry
// compatibility. This is system-generated file metadata, not a user comment.
const automaticAdditionalDocumentCommentPrefix = '[BMS] Дополнительный документ: ';
type Props = {
    supportingDocumentMissing: boolean;
    supportingDocumentMissingExplanation: string;
    isNewContract: boolean;
    newContractDocumentMissing: boolean;
    newContractDocumentMissingExplanation?: string;
    submissionReadiness: SubmissionReadiness | null;
    onChange: (patch: Partial<ExpenseRequestDraft>) => void;
    onValidateUploadIntent: (file: File, signal?: AbortSignal) => Promise<AttachmentUploadIntentResult>;
    requestId?: string | null;
    requestVersion?: string | null;
    metadataDisabled?: boolean;
    viewOnlyMetadata?: boolean;
    mutationTemporarilyDisabled?: boolean;
    onRequestVersionChanged?: (requestVersion: string) => void;
    onAttachmentsChanged?: () => void;
    embedded?: boolean;
    uploadSessionId?: string;
    onPendingWorkChanged?: (pending: boolean) => void;
    onStagedFileCountChanged?: (count: number) => void;
    reloadRevision?: number;
    contractOnly?: boolean;
    contractSaveRequired?: boolean;
    onSaveBeforeContractUpload?: () => void;
};
function formatFileSize(value: number): string {
    if (value < 1024)
        return `${value} Б`;
    if (value < 1048576)
        return `${(value / 1024).toLocaleString('ru-RU', { maximumFractionDigits: 1 })} КБ`;
    return `${(value / 1048576).toLocaleString('ru-RU', { maximumFractionDigits: 1 })} МБ`;
}
function sameIdentifier(left: string | null | undefined, right: string | null | undefined): boolean {
    return Boolean(left && right && left.toLowerCase() === right.toLowerCase());
}
export function ExpenseRequestAttachmentsPanel({ supportingDocumentMissing, supportingDocumentMissingExplanation, isNewContract, onChange, onValidateUploadIntent, requestId: savedRequestId, requestVersion: savedRequestVersion = null, metadataDisabled = false, viewOnlyMetadata = false, mutationTemporarilyDisabled = false, onRequestVersionChanged, onAttachmentsChanged, embedded = false, uploadSessionId, onPendingWorkChanged, onStagedFileCountChanged, reloadRevision = 0, contractOnly = false, contractSaveRequired = false, onSaveBeforeContractUpload, }: Props) {
    const staged = savedRequestId === null && Boolean(uploadSessionId);
    const requestId = staged ? uploadSessionId : savedRequestId;
    const requestVersion = staged ? uploadSessionId : savedRequestVersion;
    const workspaceRefresh = useWorkspaceRefreshRevision();
    // Older component-level tests exercise the metadata-only intent check without
    // mounting a saved request. Production callers always pass either a saved id
    // or an explicit null, so undefined remains a narrow compatibility harness.
    const isLegacyIntentPreview = requestId === undefined;
    const sequence = useRef(0);
    const loadSequence = useRef(0);
    const operationSequence = useRef(0);
    const currentRequestId = useRef(requestId);
    const currentRequestVersion = useRef(requestVersion);
    const initializedKindFor = useRef<string | null>(null);
    currentRequestId.current = requestId;
    currentRequestVersion.current = requestVersion;
    const activeAbortController = useRef<AbortController | null>(null);
    const fileInput = useRef<HTMLInputElement | null>(null);
    const selectionInProgress = useRef(false);
    const uploadInFlight = useRef(false);
    const activeTimeoutId = useRef<ReturnType<typeof window.setTimeout> | null>(null);
    const [uploadIntentState, setUploadIntentState] = useState<UploadIntentState>({ kind: 'idle' });
    const [selectedFile, setSelectedFile] = useState<File | null>(null);
    const [batch, setBatch] = useState<AttachmentBatch | null>(null);
    const batchRef = useRef<AttachmentBatch | null>(null);
    const [batchWake, wakeBatch] = useState(0);
    const batchPending = Boolean(batch && (batch.files.length > 0 || selectedFile));
    const [selectedKind, setSelectedKind] = useState<ExpenseRequestAttachmentKind>('SUPPORTING');
    const [snapshot, setSnapshot] = useState<ExpenseRequestAttachmentList | null>(null);
    useEffect(() => {
        if (!staged)
            onStagedFileCountChanged?.(0);
        else if (snapshot !== null)
            onStagedFileCountChanged?.(snapshot.items.length);
    }, [staged, snapshot, onStagedFileCountChanged]);
    const [previewAttachment, setPreviewAttachment] = useState<ExpenseRequestAttachment | null>(null);
    const openPreview = useAttachmentPreviewWorkspace();
    const [isLoadingAttachments, setIsLoadingAttachments] = useState(false);
    const [activeOperation, setActiveOperation] = useState<'upload' | 'download' | 'delete' | null>(null);
    const [lifecycleMessage, setLifecycleMessage] = useState<{
        tone: 'success' | 'error' | 'warning';
        text: string;
    } | null>(null);
    const latestUpload = useRef({ snapshot, selectedKind, mutationTemporarilyDisabled });
    // A later scenario/button change must never reinterpret retained file bytes.
    const selectedFileKind = useRef<ExpenseRequestAttachmentKind | null>(null);
    latestUpload.current = { snapshot, selectedKind, mutationTemporarilyDisabled };
    useEffect(() => {
        onPendingWorkChanged?.(batchPending || activeOperation !== null || uploadIntentState.kind === 'checking'
            || (selectedFile !== null && hasPendingExpenseRequestCommand(requestId ?? null)));
        return () => onPendingWorkChanged?.(false);
    }, [activeOperation, selectedFile, uploadIntentState.kind, onPendingWorkChanged, requestId, batchPending]);
    useWorkspaceRefreshGuard(batchPending || activeOperation !== null || uploadIntentState.kind === 'checking' || selectedFile !== null
        ? 'Сначала завершите операцию или очистите выбранный файл в блоке «Вложения».' : null);
    useWorkspaceRefreshBusy(isLoadingAttachments);
    useEffect(() => () => {
        sequence.current += 1;
        loadSequence.current += 1;
        operationSequence.current += 1;
        if (activeTimeoutId.current !== null) {
            window.clearTimeout(activeTimeoutId.current);
            activeTimeoutId.current = null;
        }
        activeAbortController.current?.abort();
        activeAbortController.current = null;
    }, []);
    useEffect(() => {
        loadSequence.current += 1;
        operationSequence.current += 1;
        setLifecycleMessage(null);
        setSnapshot(null);
        setPreviewAttachment(null);
        setSelectedFile(null);
        batchRef.current = null;
        setBatch(null);
        selectedFileKind.current = null;
        setUploadIntentState({ kind: 'idle' });
        setActiveOperation(null);
        selectionInProgress.current = false;
        uploadInFlight.current = false;
        initializedKindFor.current = null;
        sequence.current += 1;
        if (activeTimeoutId.current !== null) {
            window.clearTimeout(activeTimeoutId.current);
            activeTimeoutId.current = null;
        }
        activeAbortController.current?.abort();
        activeAbortController.current = null;
    }, [requestId]);
    useEffect(() => {
        const currentSequence = ++loadSequence.current;
        setLifecycleMessage(null);
        if (!requestId) {
            setIsLoadingAttachments(false);
            return;
        }
        const abortController = new AbortController();
        setIsLoadingAttachments(true);
        void (staged ? getExpenseRequestAttachments(requestId, abortController.signal, true)
            : getExpenseRequestAttachments(requestId, abortController.signal))
            .then((result) => {
            if (loadSequence.current !== currentSequence)
                return;
            setSnapshot(result);
            if (initializedKindFor.current !== requestId || result.requiresComment) {
                setSelectedKind(result.requiresComment ? 'ADDITIONAL' : 'SUPPORTING');
                initializedKindFor.current = requestId;
            }
        })
            .catch((error: unknown) => {
            if (loadSequence.current !== currentSequence
                || (error instanceof Error && error.name === 'AbortError'))
                return;
            setSnapshot(null);
            setLifecycleMessage({
                tone: 'error',
                text: 'Не удалось загрузить список вложений. Повторите попытку позже.',
            });
        })
            .finally(() => {
            if (loadSequence.current === currentSequence)
                setIsLoadingAttachments(false);
        });
        return () => {
            abortController.abort();
        };
        // A successful save/currency refresh changes the parent's version without
        // remounting this panel. Refresh file facts too, but never adopt the list's
        // version into the editable form: an external conflict must still block writes.
    }, [requestId, requestVersion, workspaceRefresh, staged, reloadRevision]);
    useEffect(() => {
        if (snapshot?.requiresComment) {
            setSelectedKind('ADDITIONAL');
        }
        else if (!isNewContract) {
            setSelectedKind('SUPPORTING');
        }
    }, [isNewContract, snapshot?.requiresComment]);
    async function validateSelectedFile(file: File | undefined) {
        if (!file || (!isLegacyIntentPreview && selectionInProgress.current) || activeOperation !== null
            || mutationTemporarilyDisabled || hasPendingExpenseRequestCommand(requestId ?? null)
            || (!isLegacyIntentPreview && pickerDisabled))
            return;
        selectionInProgress.current = true;
        setSelectedFile(file);
        selectedFileKind.current = latestUpload.current.selectedKind;
        const requestSequence = sequence.current + 1;
        sequence.current = requestSequence;
        if (activeTimeoutId.current !== null) {
            window.clearTimeout(activeTimeoutId.current);
            activeTimeoutId.current = null;
        }
        activeAbortController.current?.abort();
        const abortController = new AbortController();
        activeAbortController.current = abortController;
        setUploadIntentState({ kind: 'checking' });
        let timeoutId: ReturnType<typeof window.setTimeout> | null = null;
        const timeout = new Promise<never>((_, reject) => {
            timeoutId = window.setTimeout(() => {
                abortController.abort();
                reject(new Error('attachment upload-intent timeout'));
            }, uploadIntentTimeoutMs);
            activeTimeoutId.current = timeoutId;
        });
        try {
            const result = await Promise.race([
                onValidateUploadIntent(file, abortController.signal),
                timeout,
            ]);
            if (sequence.current !== requestSequence)
                return;
            if (timeoutId !== null) {
                window.clearTimeout(timeoutId);
                if (activeTimeoutId.current === timeoutId)
                    activeTimeoutId.current = null;
            }
            setUploadIntentState(result.isAccepted
                ? { kind: 'accepted' }
                : { kind: 'rejected', rejectionCode: result.rejectionCode ?? 'InvalidFileName' });
            if (result.isAccepted && !isLegacyIntentPreview) {
                await uploadSelectedFile(file, requestSequence);
            }
        }
        catch {
            if (sequence.current !== requestSequence)
                return;
            setUploadIntentState({ kind: 'failed' });
        }
        finally {
            if (timeoutId !== null) {
                window.clearTimeout(timeoutId);
                if (activeTimeoutId.current === timeoutId)
                    activeTimeoutId.current = null;
            }
            if (activeAbortController.current === abortController) {
                activeAbortController.current = null;
            }
            if (sequence.current === requestSequence) {
                selectionInProgress.current = false;
                if (batchRef.current)
                    wakeBatch(value => value + 1);
            }
        }
    }
    function clearSelectedFile() {
        if (activeOperation !== null || (mutationTemporarilyDisabled && !batchRef.current) || hasPendingExpenseRequestCommand(requestId ?? null))
            return;
        sequence.current += 1;
        selectionInProgress.current = false;
        if (activeTimeoutId.current !== null) {
            window.clearTimeout(activeTimeoutId.current);
            activeTimeoutId.current = null;
        }
        activeAbortController.current?.abort();
        activeAbortController.current = null;
        setSelectedFile(null);
        selectedFileKind.current = null;
        setUploadIntentState({ kind: 'idle' });
        batchRef.current = null;
        setBatch(null);
    }
    function applyMutationVersion(nextVersion: string) {
        setSnapshot((current) => current && ({ ...current, requestVersion: nextVersion }));
        if (!staged)
            onRequestVersionChanged?.(nextVersion);
        onAttachmentsChanged?.();
    }
    async function uploadSelectedFile(file: File, validatedSequence?: number) {
        const current = latestUpload.current;
        if (!requestId || !requestVersion || !current.snapshot?.canAdd || uploadInFlight.current
            || (current.selectedKind === 'NEW_CONTRACT' && contractSaveRequired)
            || selectedFileKind.current !== current.selectedKind
            || current.mutationTemporarilyDisabled
            || !sameIdentifier(currentRequestId.current, requestId)
            || !sameIdentifier(currentRequestVersion.current, requestVersion)
            || !sameIdentifier(current.snapshot.requestVersion, requestVersion)
            || (validatedSequence === undefined ? uploadIntentState.kind !== 'accepted'
                : sequence.current !== validatedSequence))
            return;
        uploadInFlight.current = true;
        const operation = operationSequence.current + 1;
        operationSequence.current = operation;
        const operationRequestId = requestId;
        const operationRequestVersion = requestVersion;
        setActiveOperation('upload');
        setLifecycleMessage(null);
        try {
            const input = {
                expectedVersion: requestVersion,
                kind: current.snapshot.requiresComment ? 'ADDITIONAL' : current.selectedKind,
                file,
                comment: current.snapshot.requiresComment ? automaticAdditionalDocumentCommentPrefix + file.name : null,
            };
            const result = await (staged ? addExpenseRequestAttachment(requestId, input, true)
                : addExpenseRequestAttachment(requestId, input));
            if (operationSequence.current !== operation
                || !sameIdentifier(currentRequestId.current, operationRequestId)
                || !sameIdentifier(currentRequestVersion.current, operationRequestVersion))
                return;
            const attachment = result.mutation.attachment;
            if (!attachment)
                throw new Error('invalid attachment mutation');
            setSnapshot((current) => current && ({
                ...current,
                requestVersion: result.mutation.requestVersion,
                items: current.items.some((item) => item.id.toLocaleLowerCase('en-US')
                    === attachment.id.toLocaleLowerCase('en-US'))
                    ? current.items
                    : [...current.items, attachment],
            }));
            const uploadingBatch = batchRef.current;
            if (uploadingBatch && sameIdentifier(uploadingBatch.contextId, operationRequestId)) {
                const nextBatch = { ...uploadingBatch, expectedVersion: result.mutation.requestVersion,
                    uploaded: uploadingBatch.uploaded + 1 };
                batchRef.current = nextBatch;
                setBatch(nextBatch);
            }
            setSelectedFile(null);
            selectedFileKind.current = null;
            setUploadIntentState({ kind: 'idle' });
            applyMutationVersion(result.mutation.requestVersion);
            setLifecycleMessage({
                tone: 'success',
                text: result.recovered ? 'Результат загрузки восстановлен.' : staged
                    ? 'Файл сохранён на сервере. При сохранении заявки он будет привязан к ней.' : 'Файл сохранён.',
            });
        }
        catch (error) {
            if (operationSequence.current !== operation
                || !sameIdentifier(currentRequestId.current, operationRequestId))
                return;
            setLifecycleMessage({
                tone: 'error',
                text: attachmentLifecycleErrorMessage(error),
            });
        }
        finally {
            if (operationSequence.current === operation
                && sameIdentifier(currentRequestId.current, operationRequestId)) {
                setActiveOperation(null);
                uploadInFlight.current = false;
            }
        }
    }
    async function deleteAttachment(attachment: ExpenseRequestAttachment) {
        if (!requestId || !requestVersion || !attachment.canDelete
            || mutationTemporarilyDisabled || selectionInProgress.current || batchPending)
            return;
        const operation = operationSequence.current + 1;
        operationSequence.current = operation;
        const operationRequestId = requestId;
        const operationRequestVersion = requestVersion;
        setActiveOperation('delete');
        setLifecycleMessage(null);
        try {
            const result = await (staged ? deleteExpenseRequestAttachment(requestId, attachment.id, requestVersion, true) : deleteExpenseRequestAttachment(requestId, attachment.id, requestVersion));
            if (operationSequence.current !== operation
                || !sameIdentifier(currentRequestId.current, operationRequestId)
                || !sameIdentifier(currentRequestVersion.current, operationRequestVersion))
                return;
            setSnapshot((current) => current && ({
                ...current,
                requestVersion: result.mutation.requestVersion,
                items: current.items.filter((item) => item.id.toLocaleLowerCase('en-US')
                    !== attachment.id.toLocaleLowerCase('en-US')),
            }));
            applyMutationVersion(result.mutation.requestVersion);
            setLifecycleMessage({
                tone: 'success',
                text: result.recovered ? 'Результат удаления восстановлен.' : 'Вложение удалено.',
            });
        }
        catch {
            if (operationSequence.current !== operation
                || !sameIdentifier(currentRequestId.current, operationRequestId))
                return;
            setLifecycleMessage({
                tone: 'error',
                text: 'Не удалось удалить вложение. Обновите заявку и повторите попытку.',
            });
        }
        finally {
            if (operationSequence.current === operation
                && sameIdentifier(currentRequestId.current, operationRequestId)) {
                setActiveOperation(null);
            }
        }
    }
    async function downloadAttachment(attachment: ExpenseRequestAttachment, propagateFailure = false) {
        if (!requestId || selectionInProgress.current || batchPending)
            return;
        const operation = operationSequence.current + 1;
        operationSequence.current = operation;
        const operationRequestId = requestId;
        setActiveOperation('download');
        setLifecycleMessage(null);
        try {
            const result = await (staged ? downloadExpenseRequestAttachment(requestId, attachment, true)
                : downloadExpenseRequestAttachment(requestId, attachment));
            if (operationSequence.current !== operation
                || !sameIdentifier(currentRequestId.current, operationRequestId))
                return;
            saveBlobDownload(result.blob, result.fileName);
        }
        catch {
            if (operationSequence.current !== operation
                || !sameIdentifier(currentRequestId.current, operationRequestId))
                return;
            setLifecycleMessage({
                tone: 'error',
                text: 'Не удалось скачать вложение. Повторите попытку позже.',
            });
            if (propagateFailure)
                throw new Error('Не удалось скачать оригинал.');
        }
        finally {
            if (operationSequence.current === operation
                && sameIdentifier(currentRequestId.current, operationRequestId)) {
                setActiveOperation(null);
            }
        }
    }
    const uploadIntentMessage = uploadIntentState.kind === 'checking'
        ? isLegacyIntentPreview ? 'Проверяем файл без загрузки и сохранения.' : 'Проверяем файл…'
        : uploadIntentState.kind === 'accepted'
            ? isLegacyIntentPreview ? 'Файл проходит предварительную проверку. Он ещё не загружен и не сохранён.' : null
            : uploadIntentState.kind === 'rejected'
                ? rejectionLabels[uploadIntentState.rejectionCode]
                : uploadIntentState.kind === 'failed'
                    ? 'Не удалось проверить файл. Попробуйте позже.'
                    : null;
    const staleVersion = Boolean(requestVersion && snapshot
        && requestVersion.toLocaleLowerCase('en-US')
            !== snapshot.requestVersion.toLocaleLowerCase('en-US'));
    const pickerDisabled = (!isLegacyIntentPreview && (!requestId || !requestVersion || !snapshot?.canAdd))
        || activeOperation !== null
        || (!isLegacyIntentPreview && uploadIntentState.kind === 'checking')
        || mutationTemporarilyDisabled
        || isLoadingAttachments
        || staleVersion;
    const pendingUploadRecovery = Boolean(selectedFile && hasPendingExpenseRequestCommand(requestId ?? null));
    const hasSupporting = snapshot?.items.some(item => item.kind === 'SUPPORTING') ?? false;
    const visibleAttachments = snapshot?.items.filter(item => !contractOnly || item.kind === 'NEW_CONTRACT') ?? [];
    const canSelectContract = isNewContract && snapshot?.canAdd && !snapshot.requiresComment;
    const batchBlocksSelection = (kind: ExpenseRequestAttachmentKind) => batchPending
        && !(selectedFile && selectedFileKind.current === kind && selectedKind === kind
            && (uploadIntentState.kind === 'accepted' || uploadIntentState.kind === 'failed'));
    function selectFiles(files: readonly File[]) {
        if (!files.length || (!isLegacyIntentPreview && selectionInProgress.current) || uploadInFlight.current || batchPending
            || activeOperation !== null || mutationTemporarilyDisabled
            || hasPendingExpenseRequestCommand(requestId ?? null) || (!isLegacyIntentPreview && pickerDisabled))
            return;
        const nextBatch = files.length > 1 && requestId && requestVersion && !isLegacyIntentPreview
            ? { contextId: requestId, expectedVersion: requestVersion, kind: latestUpload.current.selectedKind,
                files: files.slice(1), total: files.length, uploaded: 0 } : null;
        batchRef.current = nextBatch;
        setBatch(nextBatch);
        void validateSelectedFile(files[0]);
    }
    // Only a confirmed upload can advance this queue. The next file waits for
    // the parent's acknowledgement of our exact version, never a version adopted
    // from a background GET or another employee's edit.
    useEffect(() => {
        if (!batch?.files.length || selectedFile !== null || uploadIntentState.kind !== 'idle'
            || pickerDisabled || selectionInProgress.current || uploadInFlight.current
            || hasPendingExpenseRequestCommand(requestId ?? null)
            || !sameIdentifier(batch.contextId, requestId)
            || !sameIdentifier(batch.expectedVersion, requestVersion)
            || !sameIdentifier(batch.expectedVersion, snapshot?.requestVersion)
            || batch.kind !== selectedKind)
            return;
        const [file, ...files] = batch.files;
        const nextBatch = { ...batch, files };
        batchRef.current = nextBatch;
        setBatch(nextBatch);
        void validateSelectedFile(file);
    }, [batch, batchWake, selectedFile, uploadIntentState.kind, pickerDisabled,
        requestId, requestVersion, snapshot, selectedKind]);
    function selectFile(kind: ExpenseRequestAttachmentKind) {
        if (pickerDisabled || (pendingUploadRecovery && selectedKind !== kind))
            return;
        if (kind === 'NEW_CONTRACT' && contractSaveRequired)
            return;
        if (selectedFile && selectedFileKind.current === kind && selectedKind === kind && uploadIntentState.kind === 'accepted')
            void uploadSelectedFile(selectedFile);
        else if (batchPending && selectedFile && selectedFileKind.current === kind && selectedKind === kind && uploadIntentState.kind === 'failed')
            void validateSelectedFile(selectedFile);
        else if (batchPending)
            return;
        else {
            setSelectedKind(kind);
            fileInput.current?.click();
        }
    }
    return (<section id="expense-panel-attachments" role={embedded ? undefined : 'tabpanel'} aria-label={embedded ? 'Вложения' : undefined} aria-labelledby={embedded ? undefined : 'expense-tab-attachments'} className="form-card attachment-panel">
      <div className="lines-heading">
        <div>
          <div className="section-title">{contractOnly ? 'Новый договор' : 'Вложения и подтверждающие документы'}</div>
        </div>
      </div>

      {requestId === null && (<div className="storage-open-note" role="note">
          Сначала сохраните черновик, затем приложите файл.
        </div>)}
      {requestId && isLoadingAttachments && <div role="status">Загружаем вложения…</div>}
      {lifecycleMessage && lifecycleMessage.tone !== 'success' && (<div className={`notice ${lifecycleMessage.tone}`} role="status">
          {lifecycleMessage.text}
        </div>)}

      {requestId && snapshot && visibleAttachments.length > 0 && (<div className="attachment-list" aria-label="Сохранённые вложения">
            <ul>
              {visibleAttachments.map((attachment) => (<li key={attachment.id}>
                  <div>
                    <strong>{attachment.fileName}</strong>
                    <span>{kindLabels[attachment.kind]} · {formatFileSize(attachment.fileSizeBytes)}</span>
                    {attachment.comment && attachment.comment !== automaticAdditionalDocumentCommentPrefix + attachment.fileName
                    && <span>{attachment.comment}</span>}
                  </div>
                  <div className="attachment-actions">
                    <button type="button" aria-label={`Просмотреть ${attachment.fileName}`} disabled={batchPending || activeOperation !== null || uploadIntentState.kind === 'checking'} onClick={() => {
                    if (openPreview && requestId)
                        openPreview({ contextId: requestId, attachment, staged });
                    else
                        setPreviewAttachment(attachment);
                }}>
                      Просмотреть
                    </button>
                    <button type="button" aria-label={`Скачать ${attachment.fileName}`} disabled={batchPending || activeOperation !== null || uploadIntentState.kind === 'checking'} onClick={() => void downloadAttachment(attachment)}>
                      Скачать
                    </button>
                    {attachment.canDelete && (<button type="button" aria-label={`Удалить ${attachment.fileName}`} title={staleVersion ? 'Обновите карточку перед изменением вложений.' : undefined} disabled={batchPending || activeOperation !== null || uploadIntentState.kind === 'checking' || mutationTemporarilyDisabled || isLoadingAttachments || staleVersion} onClick={() => void deleteAttachment(attachment)}>
                        Удалить
                      </button>)}
                  </div>
                </li>))}
            </ul>
        </div>)}

      <div className="form-grid attachment-validation">
        {batch && <div className="field wide attachment-batch-queue" role="status" aria-label="Очередь вложений">
          <span>Загружено файлов: {batch.uploaded} из {batch.total}.</span>
          {selectedFile && <span>Текущий файл: {selectedFile.name}</span>}
          {batch.files.length > 0 && <span>Ожидают загрузки: {batch.files.map(file => file.name).join('; ')}.</span>}
          {batchPending && <>
            {(uploadIntentState.kind === 'rejected' || uploadIntentState.kind === 'failed'
                    || lifecycleMessage?.tone === 'error' || staleVersion || mutationTemporarilyDisabled
                    || snapshot?.canAdd === false || batch.kind !== selectedKind) &&
                    <span>Загрузка очереди приостановлена. Уже загруженные файлы сохранены. Повторите текущую загрузку или очистите очередь, чтобы выбрать файлы заново.</span>}
            <button type="button" disabled={activeOperation !== null || hasPendingExpenseRequestCommand(requestId ?? null)} onClick={clearSelectedFile}>Очистить очередь файлов</button>
          </>}
        </div>}
        {selectedFile && !batch && <div className="field wide" aria-label="Выбранный файл вложения">
          {uploadIntentState.kind === 'accepted' && <span>Выбран файл: {selectedFile.name}</span>}
          <button type="button" disabled={activeOperation !== null || mutationTemporarilyDisabled || hasPendingExpenseRequestCommand(requestId ?? null)} onClick={clearSelectedFile}>Очистить выбранный файл</button>
        </div>}
        {uploadIntentMessage && (<div className={`validation-message ${uploadIntentState.kind}`} role={uploadIntentState.kind === 'failed' || uploadIntentState.kind === 'rejected' ? 'alert' : 'status'}>
            {uploadIntentMessage}
          </div>)}
        {isLegacyIntentPreview && <span>Выберите файл</span>}
        <input ref={fileInput} type="file" multiple hidden={!isLegacyIntentPreview} aria-label="Проверить файл вложения" disabled={pickerDisabled || pendingUploadRecovery || batchPending} onChange={(event) => {
            const files = Array.from(event.currentTarget.files ?? []);
            event.currentTarget.value = '';
            selectFiles(files);
        }}/>
        {!isLegacyIntentPreview && !contractOnly && <button type="button" className="attachment-upload-button" disabled={pickerDisabled || batchBlocksSelection(snapshot?.requiresComment ? 'ADDITIONAL' : 'SUPPORTING') || (pendingUploadRecovery && selectedKind === 'NEW_CONTRACT')} title={staleVersion && !isLoadingAttachments ? 'Обновите карточку перед изменением вложений.' : undefined} onClick={() => selectFile(snapshot?.requiresComment ? 'ADDITIONAL' : 'SUPPORTING')}>
          {activeOperation === 'upload' ? 'Загружаем…' : 'Добавить файл'}
        </button>}
        {!isLegacyIntentPreview && canSelectContract && contractSaveRequired && <div role="note">
          Сначала сохраните изменения черновика, затем приложите новый договор. Введённые данные и выбранный файл сохраняются в форме.
          {onSaveBeforeContractUpload && <button type="button" disabled={pickerDisabled || pendingUploadRecovery} onClick={onSaveBeforeContractUpload}>Сохранить черновик перед загрузкой договора</button>}
        </div>}
        {!isLegacyIntentPreview && canSelectContract && <button type="button" className="attachment-upload-button" disabled={pickerDisabled || contractSaveRequired || batchBlocksSelection('NEW_CONTRACT') || (pendingUploadRecovery && selectedKind !== 'NEW_CONTRACT')} title={staleVersion && !isLoadingAttachments ? 'Обновите карточку перед изменением вложений.' : undefined} onClick={() => selectFile('NEW_CONTRACT')}>
          {activeOperation === 'upload' && selectedKind === 'NEW_CONTRACT' ? 'Загружаем договор…' : 'Добавить новый договор'}
        </button>}
      </div>

      {viewOnlyMetadata && !contractOnly && supportingDocumentMissing && <dl className="expense-view-grid attachment-metadata">
        <ExpenseReadField label="Подтверждающий документ" wide>Отсутствует</ExpenseReadField>
        <ExpenseReadField label="Пояснение отсутствия" wide>{supportingDocumentMissingExplanation}</ExpenseReadField>
      </dl>}
      {!viewOnlyMetadata && !contractOnly && (!hasSupporting || supportingDocumentMissing) && <div className="form-grid attachment-metadata">
        <label className="field checkbox-field"><input type="checkbox" disabled={metadataDisabled} checked={supportingDocumentMissing} onChange={(event) => onChange({ supportingDocumentMissing: event.target.checked, supportingDocumentMissingExplanation: event.target.checked ? supportingDocumentMissingExplanation : '' })}/><span>Подтверждающий документ отсутствует</span></label>
        {supportingDocumentMissing && <label className="field wide required"><span>Пояснение отсутствия</span><textarea disabled={metadataDisabled} rows={3} maxLength={1000} value={supportingDocumentMissingExplanation} onChange={(event) => onChange({ supportingDocumentMissingExplanation: event.target.value })}/></label>}
      </div>}
      {requestId && previewAttachment && <AttachmentPreviewDialog key={`${requestId}:${previewAttachment.id}:${staged}`} contextId={requestId} attachmentId={previewAttachment.id} fileName={previewAttachment.fileName} staged={staged} onClose={() => setPreviewAttachment(null)} onDownloadOriginal={() => downloadAttachment(previewAttachment, true)}/>}
    </section>);
}
