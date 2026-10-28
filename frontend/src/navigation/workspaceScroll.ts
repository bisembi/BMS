import type { ScrollPosition } from './browserHistory';
export function readWorkspaceScroll(main: HTMLElement | null): ScrollPosition {
    return { mainTop: main?.scrollTop ?? 0, mainLeft: main?.scrollLeft ?? 0, windowTop: window.scrollY, windowLeft: window.scrollX };
}
/** Cached pages restore immediately. Lazy pages wait for their real rows, and
 * stop restoring as soon as the user scrolls, types, clicks or leaves the view. */
export function restoreWorkspaceScroll(main: HTMLElement | null, position: ScrollPosition): () => void {
    let frame = 0, stopped = false, settledFrames = 0;
    // The fixed original THEAD inserts its layout caption on the next frame.
    // Keep manual restoration alive through that layout change; otherwise native
    // scroll anchoring shifts the viewport by one header row after we finish.
    const anchor = main?.style.getPropertyValue('overflow-anchor') ?? '';
    const anchorPriority = main?.style.getPropertyPriority('overflow-anchor') ?? '';
    main?.style.setProperty('overflow-anchor', 'none');
    const observer = new MutationObserver(schedule);
    const resize = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(schedule);
    const inputs = ['wheel', 'touchstart', 'pointerdown', 'keydown'] as const;
    function stop() {
        if (stopped)
            return;
        stopped = true;
        cancelAnimationFrame(frame);
        observer.disconnect();
        resize?.disconnect();
        if (main) {
            if (anchor)
                main.style.setProperty('overflow-anchor', anchor, anchorPriority);
            else
                main.style.removeProperty('overflow-anchor');
        }
        inputs.forEach(event => window.removeEventListener(event, stop, true));
    }
    function restore() {
        frame = 0;
        if (stopped)
            return;
        if (main) {
            main.scrollTop = position.mainTop;
            main.scrollLeft = position.mainLeft;
        }
        if (window.scrollX !== position.windowLeft || window.scrollY !== position.windowTop)
            window.scrollTo(position.windowLeft, position.windowTop);
        const current = readWorkspaceScroll(main);
        if (Object.keys(position).every(key => Math.abs(current[key as keyof ScrollPosition] - position[key as keyof ScrollPosition]) <= 1)) {
            if (++settledFrames >= 3)
                stop();
            else
                schedule();
        }
        else
            settledFrames = 0;
    }
    function schedule() {
        if (!stopped && !frame)
            frame = requestAnimationFrame(restore);
    }
    observer.observe(main ?? document.body, { subtree: true, childList: true });
    if (main) {
        resize?.observe(main);
        if (main.firstElementChild)
            resize?.observe(main.firstElementChild);
    }
    resize?.observe(document.body);
    inputs.forEach(event => window.addEventListener(event, stop, { capture: true, passive: true }));
    restore();
    return stop;
}
