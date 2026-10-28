// Finite source/decode admission, worker downsampling and output canvas limits
// are independent. Do not replace maxImageSize with -1: compressed PDFs can
// contain images far larger than their uploaded byte size suggests.
export const PDF_PREVIEW_LIMITS = Object.freeze({
    imagePixels: 64000000,
    workerCanvasBytes: 48000000,
    canvasPixels: 12000000,
    canvasEdge: 8192,
    fileBytes: 25 * 1024 * 1024,
    pages: 500,
    deadlineMs: 30000,
});
// One heavy decode/render at a time across all modeless preview windows.
// Waiting/cancelled windows never acquire a second decode allocation.
export function createPdfRenderGate() {
    type Waiter = {
        signal: AbortSignal;
        resolve: (release: () => void) => void;
        reject: (error: Error) => void;
        abort: () => void;
    };
    const queue: Waiter[] = [];
    let busy = false;
    const aborted = () => new DOMException('PDF render cancelled', 'AbortError');
    const drain = () => {
        if (busy)
            return;
        const waiter = queue.shift();
        if (!waiter)
            return;
        waiter.signal.removeEventListener('abort', waiter.abort);
        if (waiter.signal.aborted) {
            waiter.reject(aborted());
            drain();
            return;
        }
        busy = true;
        let released = false;
        waiter.resolve(() => {
            if (!released) {
                released = true;
                busy = false;
                drain();
            }
        });
    };
    return (signal: AbortSignal): Promise<() => void> => new Promise((resolve, reject) => {
        if (signal.aborted) {
            reject(aborted());
            return;
        }
        const waiter: Waiter = { signal, resolve, reject, abort: () => {
                const index = queue.indexOf(waiter);
                if (index >= 0)
                    queue.splice(index, 1);
                signal.removeEventListener('abort', waiter.abort);
                reject(aborted());
            } };
        queue.push(waiter);
        signal.addEventListener('abort', waiter.abort, { once: true });
        drain();
    });
}
export const acquirePdfRenderSlot = createPdfRenderGate();
// PDF.js can successfully finish an empty operator stream after rejecting a
// too-large image. Inspect bounded strips, never duplicate the entire 48 MB
// output canvas just to detect a silent white page. A genuinely blank page is
// still navigable; this is an informational note, not a document failure.
export function isPdfCanvasEmpty(canvas: HTMLCanvasElement, context: CanvasRenderingContext2D): boolean {
    if (typeof context.getImageData !== 'function')
        return false;
    for (let top = 0; top < canvas.height; top += 16) {
        const { data } = context.getImageData(0, top, canvas.width, Math.min(16, canvas.height - top));
        for (let pixel = 0; pixel < data.length; pixel += 4)
            if (data[pixel + 3] !== 0 && (data[pixel] < 255 || data[pixel + 1] < 255 || data[pixel + 2] < 255))
                return false;
    }
    return true;
}
