import { useEffect, useRef, useState, type ReactNode } from 'react';
import { RotateCcw, RotateCw } from 'lucide-react';
import { AnnotationMode, getDocument, GlobalWorkerOptions, type PDFDocumentProxy, type PDFPageProxy } from 'pdfjs-dist';
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url';
import { acquirePdfRenderSlot, isPdfCanvasEmpty, PDF_PREVIEW_LIMITS as limits } from './pdfPreviewPolicy';
GlobalWorkerOptions.workerSrc = workerUrl;
const assets = '/assets/pdfjs-6.4.299/';
export default function AttachmentPdfView({ blob, onError, windowControl, onDownloadOriginal }: {
    blob: Blob;
    onError?: () => void;
    windowControl?: ReactNode;
    onDownloadOriginal?: () => void;
}) {
    const [document, setDocument] = useState<PDFDocumentProxy | null>(null);
    const [page, setPage] = useState(1), [zoom, setZoom] = useState<number | 'fit-width'>('fit-width');
    const [rotation, setRotation] = useState(0);
    const [availableWidth, setAvailableWidth] = useState(0);
    const [error, setError] = useState(false), [rendering, setRendering] = useState(true);
    const [emptyPage, setEmptyPage] = useState(false);
    const canvas = useRef<HTMLCanvasElement>(null);
    const pageContainer = useRef<HTMLDivElement>(null);
    const landing = useRef<'top' | 'bottom' | null>('top');
    const wheelBurst = useRef({ locked: false, lastAt: -Infinity });
    const rotate = (delta: number) => {
        if (!document)
            return;
        landing.current = 'top';
        wheelBurst.current = { locked: true, lastAt: performance.now() };
        setRendering(true);
        setRotation(current => (current + delta + 360) % 360);
    };
    const navigate = (next: number, edge: 'top' | 'bottom') => {
        if (!document || next < 1 || next > document.numPages || next === page)
            return;
        landing.current = edge;
        wheelBurst.current = { locked: true, lastAt: performance.now() };
        setRendering(true);
        setPage(next);
    };
    useEffect(() => {
        const target = pageContainer.current;
        if (!target || error)
            return;
        const wheel = (event: WheelEvent) => {
            // Leave horizontal scrolling and browser/pinch zoom alone. Use a native
            // non-passive listener: React's delegated wheel listener can be passive.
            if (event.ctrlKey || event.metaKey || event.shiftKey || !Number.isFinite(event.deltaY)
                || event.deltaY === 0 || Math.abs(event.deltaX) >= Math.abs(event.deltaY))
                return;
            const now = performance.now(), burst = wheelBurst.current;
            if (now - burst.lastAt >= 250)
                burst.locked = false;
            burst.lastAt = now;
            if (rendering || landing.current !== null || burst.locked) {
                event.preventDefault();
                return;
            }
            if (!document)
                return;
            const forward = event.deltaY > 0;
            const atEdge = forward ? target.scrollTop + target.clientHeight >= target.scrollHeight - 2 : target.scrollTop <= 2;
            if (!atEdge)
                return; // Ordinary scrolling inside the current page.
            event.preventDefault(); // Never scroll the BMS portal behind this window.
            navigate(page + (forward ? 1 : -1), forward ? 'top' : 'bottom');
        };
        target.addEventListener('wheel', wheel, { passive: false });
        return () => target.removeEventListener('wheel', wheel);
    }, [document, page, rendering, error]);
    useEffect(() => {
        if (error)
            onError?.();
    }, [error, onError]);
    useEffect(() => {
        const target = pageContainer.current;
        if (!target)
            return;
        const measure = () => {
            const style = window.getComputedStyle(target);
            setAvailableWidth(Math.max(0, Math.floor(target.clientWidth - (parseFloat(style.paddingLeft) || 0) - (parseFloat(style.paddingRight) || 0))));
        };
        measure();
        const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(measure);
        observer?.observe(target);
        window.addEventListener('resize', measure);
        return () => { observer?.disconnect(); window.removeEventListener('resize', measure); };
    }, [error]);
    useEffect(() => {
        let active = true;
        let loading: ReturnType<typeof getDocument> | undefined;
        setError(false);
        setDocument(null);
        setPage(1);
        setZoom('fit-width');
        setRotation(0);
        setRendering(true);
        landing.current = 'top';
        wheelBurst.current = { locked: false, lastAt: -Infinity };
        const destroy = () => {
            if (loading)
                void Promise.resolve(loading.destroy()).catch(() => undefined);
        };
        const deadline = window.setTimeout(() => {
            if (active) {
                setError(true);
                destroy();
            }
        }, limits.deadlineMs);
        void Promise.resolve().then(() => {
            if (blob.size > limits.fileBytes)
                throw new Error('PDF file limit');
            return blob.arrayBuffer();
        }).then(bytes => {
            if (!active)
                return;
            if (bytes.byteLength > limits.fileBytes)
                throw new Error('PDF file limit');
            loading = getDocument({ data: new Uint8Array(bytes), cMapUrl: assets + 'cmaps/', cMapPacked: true,
                standardFontDataUrl: assets + 'standard_fonts/', wasmUrl: assets + 'wasm/', iccUrl: assets + 'iccs/', useWasm: true, enableXfa: false,
                disableFontFace: true, stopAtErrors: true, maxImageSize: limits.imagePixels, canvasMaxAreaInBytes: limits.workerCanvasBytes });
            return loading.promise.then(pdf => {
                if (!active)
                    return;
                if (pdf.numPages > limits.pages || pdf.numPages < 1) {
                    setError(true);
                    destroy();
                    return;
                }
                setDocument(pdf);
            });
        }).catch(() => {
            if (active) {
                setError(true);
                destroy();
            }
        }).finally(() => window.clearTimeout(deadline));
        return () => { active = false; window.clearTimeout(deadline); destroy(); };
    }, [blob]);
    const fitWidth = zoom === 'fit-width' ? availableWidth : 0;
    useEffect(() => {
        if (!document || !canvas.current || (zoom === 'fit-width' && fitWidth <= 0))
            return;
        let active = true, task: ReturnType<PDFPageProxy['render']> | undefined;
        const abort = new AbortController();
        const target = canvas.current;
        setRendering(true);
        setEmptyPage(false);
        const fail = () => {
            setError(true);
            abort.abort();
            task?.cancel();
            // Cancelling the canvas alone does not stop a worker decoding an image.
            void Promise.resolve(document.loadingTask.destroy()).catch(() => undefined);
        };
        const deadline = window.setTimeout(() => {
            if (active)
                fail();
        }, limits.deadlineMs);
        void (async () => {
            let release: (() => void) | undefined, pdfPage: PDFPageProxy | undefined;
            try {
                release = await acquirePdfRenderSlot(abort.signal);
                if (!active)
                    return;
                pdfPage = await document.getPage(page);
                if (!active)
                    return;
                // Add the user's turn to the PDF's intrinsic orientation. Rendering a
                // rotated viewport (not a CSS transform) keeps fit-width, scroll bounds,
                // wheel navigation and canvas memory limits correct in every direction.
                const angle = ((pdfPage.rotate || 0) + rotation) % 360;
                const base = pdfPage.getViewport({ scale: 1, rotation: angle });
                if (!Number.isFinite(base.width) || base.width <= 0)
                    throw new Error('invalid PDF page width');
                const scale = zoom === 'fit-width' ? fitWidth / base.width : zoom;
                const initial = pdfPage.getViewport({ scale: scale * Math.min(window.devicePixelRatio || 1, 2), rotation: angle });
                if (![initial.width, initial.height].every(value => Number.isFinite(value) && value > 0))
                    throw new Error('invalid PDF viewport');
                const factor = Math.min(1, Math.sqrt(limits.canvasPixels / (initial.width * initial.height)), limits.canvasEdge / Math.max(initial.width, initial.height));
                const viewport = pdfPage.getViewport({ scale: initial.scale * factor, rotation: angle });
                target.width = Math.max(1, Math.floor(viewport.width));
                target.height = Math.max(1, Math.floor(viewport.height));
                const density = Math.min(window.devicePixelRatio || 1, 2);
                target.style.width = `${Math.max(1, Math.floor(viewport.width / density))}px`;
                target.style.height = `${Math.max(1, Math.floor(viewport.height / density))}px`;
                const context = target.getContext('2d');
                if (!context)
                    throw new Error('canvas unavailable');
                task = pdfPage.render({ canvas: target, canvasContext: context, viewport, annotationMode: AnnotationMode.DISABLE });
                await task.promise;
                if (active) {
                    setEmptyPage(isPdfCanvasEmpty(target, context));
                    const container = pageContainer.current;
                    if (container && landing.current !== null)
                        container.scrollTop = landing.current === 'bottom' ? Math.max(0, container.scrollHeight - container.clientHeight) : 0;
                    landing.current = null;
                    setRendering(false);
                }
            }
            catch {
                if (active)
                    fail();
            }
            finally {
                try {
                    if (pdfPage)
                        await document.cleanup?.();
                }
                catch {
                    if (active)
                        fail();
                }
                finally {
                    release?.();
                    window.clearTimeout(deadline);
                }
            }
        })();
        return () => {
            active = false;
            abort.abort();
            window.clearTimeout(deadline);
            task?.cancel();
            target.width = 1;
            target.height = 1; // Release the detached canvas backing store.
        };
    }, [document, page, zoom, fitWidth, rotation]);
    if (error)
        return <p role="alert">Не удалось отобразить PDF: файл повреждён, защищён паролем либо превышает ограничения просмотра. Можно скачать оригинал.</p>;
    return <div className="attachment-preview-pdf">
    <div className="attachment-preview-tools">
      <button type="button" disabled={!document || page <= 1} onClick={() => navigate(page - 1, 'top')}>Назад</button>
      <span>Страница {page} из {document?.numPages ?? '…'}</span>
      <button type="button" disabled={!document || page >= document.numPages} onClick={() => navigate(page + 1, 'top')}>Вперёд</button>
      <label>Масштаб <select aria-label="Масштаб PDF" value={zoom} onChange={e => setZoom(e.target.value === 'fit-width' ? 'fit-width' : Number(e.target.value))}>
        <option value="fit-width">По ширине страницы</option>
        <option value={0.25}>25%</option><option value={0.5}>50%</option><option value={0.75}>75%</option><option value={1}>100%</option><option value={1.5}>150%</option><option value={2}>200%</option>
      </select></label>
      <button type="button" disabled={!document} aria-label="Повернуть против часовой стрелки на 90°" title="Повернуть против часовой стрелки на 90°" onClick={() => rotate(-90)}><RotateCcw size={16} aria-hidden="true"/></button>
      <button type="button" disabled={!document} aria-label="Повернуть по часовой стрелке на 90°" title="Повернуть по часовой стрелке на 90°" onClick={() => rotate(90)}><RotateCw size={16} aria-hidden="true"/></button>
      {windowControl}
      {rendering && <span role="status">Отрисовка…</span>}
    </div>
    {emptyPage && !rendering && <p role="note">На странице нет видимого содержимого. Если ожидался документ, скачайте оригинал.
      {onDownloadOriginal && <> <button type="button" onClick={onDownloadOriginal}>Скачать оригинал</button></>}
    </p>}
    <div className="attachment-preview-page" ref={pageContainer} tabIndex={0} aria-label="Область страниц PDF"><canvas key={`${page}:${zoom}:${fitWidth}:${rotation}`} ref={canvas} aria-label={`Страница ${page} PDF`}/></div>
  </div>;
}
