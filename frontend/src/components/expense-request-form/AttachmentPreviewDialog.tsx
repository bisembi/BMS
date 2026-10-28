import { Component, lazy, Suspense, useCallback, useEffect, useId, useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { PanelRightClose, PanelRightOpen } from 'lucide-react';
import { getAttachmentPreview, type AttachmentPreview } from '../../api/attachmentPreview';
import { attachmentPreviewErrorMessage } from '../../utils/uiErrorMessages';
import { usePreviewWindow, type PreviewEdge } from './usePreviewWindow';
import './AttachmentPreviewDialog.css';
const PdfView = lazy(() => import('./AttachmentPdfView'));
class PreviewBoundary extends Component<{
    children: ReactNode;
    onFailure: () => void;
}, {
    failed: boolean;
}> {
    state = { failed: false };
    static getDerivedStateFromError() { return { failed: true }; }
    componentDidCatch() { this.props.onFailure(); }
    render() { return this.state.failed ? <p role="alert">Не удалось открыть средство просмотра. Закройте окно и повторите попытку.</p> : this.props.children; }
}
export function AttachmentPreviewDialog({ contextId, attachmentId, fileName, staged, onClose, onDownloadOriginal, onActivate, zIndex = 1100 }: {
    contextId: string;
    attachmentId: string;
    fileName: string;
    staged: boolean;
    onClose: () => void;
    onDownloadOriginal?: () => Promise<void>;
    onActivate?: () => void;
    zIndex?: number;
}) {
    const dialog = useRef<HTMLDialogElement>(null), close = useRef<HTMLButtonElement>(null), title = useId();
    const windowState = usePreviewWindow(dialog);
    const [preview, setPreview] = useState<AttachmentPreview | null>(null);
    const [error, setError] = useState<string | null>(null), [sheet, setSheet] = useState(0);
    const [imageUrl, setImageUrl] = useState<string | null>(null);
    const [revision, setRevision] = useState(0);
    const [downloading, setDownloading] = useState(false), [downloadError, setDownloadError] = useState(false);
    const pdfFailed = useCallback(() => setError('Не удалось отобразить PDF. Можно повторить просмотр или скачать оригинал.'), []);
    const viewerFailed = useCallback(() => setError('Не удалось открыть средство просмотра. Можно повторить попытку или скачать оригинал.'), []);
    const download = async () => {
        if (!onDownloadOriginal || downloading)
            return;
        setDownloading(true);
        setDownloadError(false);
        try {
            await onDownloadOriginal();
        }
        catch {
            setDownloadError(true);
        }
        finally {
            setDownloading(false);
        }
    };
    useLayoutEffect(() => {
        const previous = document.activeElement, element = dialog.current;
        // A modeless native dialog leaves the portal, keyboard navigation and
        // other BMS actions usable. Never add this window to the modal top layer.
        try {
            element?.show();
        }
        catch {
            element?.setAttribute('open', '');
        }
        if (element && !element.open)
            element.setAttribute('open', '');
        close.current?.focus();
        return () => {
            const ownedFocus = element?.contains(document.activeElement);
            if (element?.open) {
                try {
                    element.close();
                }
                catch {
                    element.removeAttribute('open');
                }
            }
            if (ownedFocus && previous instanceof HTMLElement && previous.isConnected)
                previous.focus();
        };
    }, []);
    useEffect(() => {
        const abort = new AbortController();
        let active = true;
        setPreview(null);
        setError(null);
        setSheet(0);
        const deadline = window.setTimeout(() => {
            if (active) {
                setError('Просмотр занял слишком много времени. Повторите попытку.');
                abort.abort();
            }
        }, 45000);
        void getAttachmentPreview(contextId, attachmentId, staged, abort.signal)
            .then(value => {
            if (active)
                setPreview(value);
        })
            .catch((failure: unknown) => {
            if (active && !abort.signal.aborted)
                setError(attachmentPreviewErrorMessage(failure));
        })
            .finally(() => window.clearTimeout(deadline));
        return () => { active = false; abort.abort(); window.clearTimeout(deadline); };
    }, [contextId, attachmentId, staged, revision]);
    useEffect(() => {
        setImageUrl(null);
        if (preview?.kind !== 'image')
            return;
        const reader = new FileReader();
        let active = true;
        reader.onload = () => {
            if (active && typeof reader.result === 'string')
                setImageUrl(reader.result);
        };
        reader.onerror = () => {
            if (active)
                setError('Изображение не удалось открыть.');
        };
        reader.readAsDataURL(preview.blob);
        return () => { active = false; reader.abort(); };
    }, [preview]);
    const section = preview?.kind === 'document' ? preview.document.sections[sheet] : null;
    const dockLabel = windowState.docked ? 'Вернуть прежний размер окна просмотра' : 'Прижать просмотр вправо (46% экрана)';
    const dockControl = <button type="button" aria-label={dockLabel} title={dockLabel} aria-pressed={windowState.docked} onClick={windowState.toggleDock}>{windowState.docked ? <PanelRightOpen size={16} aria-hidden="true"/> : <PanelRightClose size={16} aria-hidden="true"/>}</button>;
    return createPortal(<dialog ref={dialog} className={`attachment-preview-dialog bms-unified-theme${windowState.active ? ' attachment-preview-moving' : ''}`} role="dialog" aria-modal="false" aria-labelledby={title} data-preview-layout={windowState.docked ? 'right' : 'floating'} style={{ ...windowState.box, zIndex }} onPointerDownCapture={onActivate} onFocusCapture={onActivate} onPointerMove={windowState.move} onPointerUp={windowState.finish} onPointerCancel={windowState.finish} onLostPointerCapture={windowState.finish} onCancel={event => { event.preventDefault(); }} onKeyDown={event => {
            if (event.key === 'Escape') {
                event.preventDefault();
                event.stopPropagation();
            }
        }}>
    <div className="attachment-preview-heading" tabIndex={0} aria-label="Переместить окно просмотра" title="Перетащите заголовок мышью. С клавиатуры — стрелками, Shift для точного перемещения." onPointerDown={event => windowState.start(event, 'move')} onKeyDown={event => windowState.keyboard(event, 'move')}>
      <h2 id={title}>{fileName}</h2><button ref={close} type="button" onClick={onClose}>Закрыть просмотр</button></div>
    {(preview?.kind !== 'pdf' || error) && <div className="attachment-preview-tools attachment-preview-window-tools">{dockControl}</div>}
    <div className={`attachment-preview-body${preview?.kind === 'pdf' ? ' attachment-preview-body-pdf' : ''}`} tabIndex={0}>
      {error ? <p role="alert">{error}</p> : !preview ? <p role="status">Подготовка просмотра…</p> : <>
        {preview.kind === 'pdf' && <PreviewBoundary onFailure={viewerFailed}><Suspense fallback={<p role="status">Открытие PDF…</p>}><PdfView blob={preview.blob} onError={pdfFailed} windowControl={dockControl} onDownloadOriginal={onDownloadOriginal ? () => void download() : undefined}/></Suspense></PreviewBoundary>}
        {preview.kind === 'image' && (imageUrl ? <img className="attachment-preview-image" src={imageUrl} alt={fileName} onError={() => setError('Изображение повреждено или не поддерживается браузером.')} onLoad={event => {
                    const image = event.currentTarget;
                    if (image.naturalWidth * image.naturalHeight > 20000000)
                        setError('Изображение слишком большое для просмотра. Можно скачать оригинал.');
                }}/> : <p role="status">Открытие изображения…</p>)}
        {preview.kind === 'text' && <pre>{preview.text}</pre>}
        {preview.kind === 'document' && <>
          <p className="notice">{preview.document.notice}</p>
          {preview.document.sections.length > 1 && <label>Лист <select aria-label="Лист документа" value={sheet} onChange={e => setSheet(Number(e.target.value))}>
            {preview.document.sections.map((part, index) => <option key={index} value={index}>{part.name}</option>)}
          </select></label>}
          {section?.text !== null && <pre>{section?.text}</pre>}
          {section?.rows && <div className="attachment-preview-sheet"><table aria-label={section.name}><tbody>
            {section.rows.map((row, index) => <tr key={index}><th scope="row">{index + 1}</th>{row.map((cell, col) => <td key={col}>{cell}</td>)}</tr>)}
          </tbody></table></div>}
        </>}
      </>}
    </div>
    {error && <div className="attachment-preview-tools">
      <button type="button" disabled={downloading} onClick={() => { setDownloadError(false); setRevision(value => value + 1); }}>Повторить</button>
      {onDownloadOriginal && <button type="button" disabled={downloading} onClick={() => void download()}>{downloading ? 'Скачивание…' : 'Скачать оригинал'}</button>}
    </div>}
    {downloadError && <p role="alert">Не удалось скачать оригинал. Проверьте доступ и повторите попытку.</p>}
    {(['n', 'ne', 'e', 'se', 's', 'sw', 'w', 'nw'] as PreviewEdge[]).map(edge => <button key={edge} type="button" className={`attachment-preview-resize attachment-preview-resize-${edge}`} aria-label={`Изменить размер окна: ${{ n: 'сверху', ne: 'сверху справа', e: 'справа', se: 'снизу справа', s: 'снизу', sw: 'снизу слева', w: 'слева', nw: 'сверху слева' }[edge]}`} title="Изменить размер мышью или стрелками клавиатуры" onPointerDown={event => windowState.start(event, edge)} onKeyDown={event => windowState.keyboard(event, edge)}/>)}
  </dialog>, document.body);
}
