import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { downloadExpenseRequestAttachment } from '../../api/expenseRequestAttachments';
import type { ExpenseRequestAttachment } from '../../api/expenseRequestAttachmentPublicResponse';
import { saveBlobDownload } from '../../utils/saveBlobDownload';
import { AttachmentPreviewDialog } from './AttachmentPreviewDialog';
type Preview = {
    contextId: string;
    staged: boolean;
    attachment: ExpenseRequestAttachment;
};
type Window = Preview & {
    key: string;
    scope: string;
};
const PreviewWorkspace = createContext<((preview: Preview) => void) | null>(null);
const keyFor = (preview: Preview) => `${preview.staged}:${preview.contextId.toLowerCase()}:${preview.attachment.id.toLowerCase()}`;
// This host outlives cached/closed request tabs. No file bytes or preview keys
// are persisted to storage, and an identity/role change revokes all windows.
export function AttachmentPreviewWorkspace({ scopeKey, children }: {
    scopeKey: string | null;
    children: ReactNode;
}) {
    const [windows, setWindows] = useState<Window[]>([]);
    const [order, setOrder] = useState<string[]>([]);
    const currentScope = useRef(scopeKey);
    currentScope.current = scopeKey;
    useEffect(() => { setWindows([]); setOrder([]); }, [scopeKey]);
    const activate = useCallback((key: string) => {
        setOrder(current => current.at(-1) === key ? current : [...current.filter(item => item !== key), key]);
    }, []);
    const open = useCallback((preview: Preview) => {
        if (scopeKey === null)
            return;
        const key = keyFor(preview);
        setWindows(current => current.some(item => item.key === key && item.scope === scopeKey)
            ? current : [...current.filter(item => item.scope === scopeKey), { ...preview, key, scope: scopeKey }]);
        activate(key);
    }, [scopeKey, activate]);
    const value = useMemo(() => scopeKey === null ? null : open, [scopeKey, open]);
    return <PreviewWorkspace.Provider value={value}>
    {children}
    {windows.filter(item => item.scope === scopeKey).map(item => <AttachmentPreviewDialog key={item.key} contextId={item.contextId} attachmentId={item.attachment.id} fileName={item.attachment.fileName} staged={item.staged} zIndex={1100 + Math.max(0, order.indexOf(item.key))} onActivate={() => activate(item.key)} onClose={() => { setWindows(current => current.filter(window => window.key !== item.key)); setOrder(current => current.filter(key => key !== item.key)); }} onDownloadOriginal={async () => {
                if (currentScope.current !== item.scope)
                    throw new Error('Preview scope changed.');
                const result = await downloadExpenseRequestAttachment(item.contextId, item.attachment, item.staged);
                if (currentScope.current !== item.scope)
                    throw new Error('Preview scope changed.');
                saveBlobDownload(result.blob, result.fileName);
            }}/>)}
  </PreviewWorkspace.Provider>;
}
export function useAttachmentPreviewWorkspace() { return useContext(PreviewWorkspace); }
