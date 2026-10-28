import { useEffect, useRef, useState, type KeyboardEvent, type PointerEvent, type RefObject } from 'react';
export type PreviewEdge = 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w' | 'nw';
type Box = {
    left: number;
    top: number;
    width: number;
    height: number;
};
type Viewport = {
    width: number;
    height: number;
};
type Layout = {
    box: Box;
    docked: boolean;
    floatingBox: Box | null;
};
const margin = 8;
const viewport = (): Viewport => ({ width: window.innerWidth, height: window.innerHeight });
const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max);
export function constrainPreviewBox(box: Box, view: Viewport): Box {
    const width = clamp(box.width, Math.min(360, Math.max(1, view.width - 2 * margin)), Math.max(1, view.width - 2 * margin));
    const height = clamp(box.height, Math.min(240, Math.max(1, view.height - 2 * margin)), Math.max(1, view.height - 2 * margin));
    return { width, height, left: clamp(box.left, margin, Math.max(margin, view.width - margin - width)),
        top: clamp(box.top, margin, Math.max(margin, view.height - margin - height)) };
}
export function changePreviewBox(box: Box, mode: 'move' | PreviewEdge, dx: number, dy: number, view: Viewport): Box {
    const start = constrainPreviewBox(box, view);
    if (mode === 'move')
        return constrainPreviewBox({ ...start, left: start.left + dx, top: start.top + dy }, view);
    const minWidth = Math.min(360, Math.max(1, view.width - 2 * margin)), minHeight = Math.min(240, Math.max(1, view.height - 2 * margin));
    let { left, top, width, height } = start;
    if (mode.includes('e'))
        width = clamp(width + dx, minWidth, view.width - margin - left);
    if (mode.includes('s'))
        height = clamp(height + dy, minHeight, view.height - margin - top);
    if (mode.includes('w')) {
        left = clamp(left + dx, margin, start.left + start.width - minWidth);
        width = start.left + start.width - left;
    }
    if (mode.includes('n')) {
        top = clamp(top + dy, margin, start.top + start.height - minHeight);
        height = start.top + start.height - top;
    }
    return constrainPreviewBox({ left, top, width, height }, view);
}
const initialBox = () => {
    const view = viewport(), width = Math.min(1200, view.width * .96), height = Math.min(900, view.height * .94);
    return constrainPreviewBox({ width, height, left: (view.width - width) / 2, top: (view.height - height) / 2 }, view);
};
/** Keep 54% free for the request: the former 40% viewer is now 15% wider.
 * On a narrow viewport retain the existing
 * useful 360px minimum (or the available viewport) so controls stay reachable. */
export function rightDockedPreviewBox(view: Viewport): Box {
    const width = Math.max(360, view.width * .46);
    return constrainPreviewBox({ left: view.width - margin - width, top: margin,
        width, height: view.height - 2 * margin }, view);
}
export function usePreviewWindow(dialog: RefObject<HTMLDialogElement | null>) {
    const [layout, setLayout] = useState<Layout>(() => ({ box: initialBox(), docked: false, floatingBox: null }));
    const [active, setActive] = useState(false);
    const gesture = useRef<{
        id: number;
        x: number;
        y: number;
        box: Box;
        mode: 'move' | PreviewEdge;
    } | null>(null);
    useEffect(() => {
        const resize = () => {
            const id = gesture.current?.id;
            gesture.current = null;
            setActive(false);
            if (id !== undefined && dialog.current?.hasPointerCapture?.(id))
                dialog.current.releasePointerCapture(id);
            setLayout(value => ({ ...value, box: value.docked ? rightDockedPreviewBox(viewport()) : constrainPreviewBox(value.box, viewport()) }));
        };
        window.addEventListener('resize', resize);
        return () => window.removeEventListener('resize', resize);
    }, []);
    const start = (event: PointerEvent<HTMLElement>, mode: 'move' | PreviewEdge) => {
        if (event.button !== 0 || gesture.current)
            return;
        if (mode === 'move' && event.target instanceof Element && event.target.closest('button,select,input,a,textarea'))
            return;
        const element = dialog.current;
        if (!element)
            return;
        event.preventDefault();
        const rect = element.getBoundingClientRect();
        const current = constrainPreviewBox({ left: rect.left, top: rect.top, width: rect.width, height: rect.height }, viewport());
        gesture.current = { id: event.pointerId, x: event.clientX, y: event.clientY, box: current, mode };
        element.setPointerCapture?.(event.pointerId);
        // Manual movement/resizing detaches naturally, starting at the docked box
        // rather than jumping back to its previous floating position.
        setLayout({ box: current, docked: false, floatingBox: null });
        setActive(true);
    };
    const finish = (event: PointerEvent<HTMLDialogElement>) => {
        if (gesture.current?.id !== event.pointerId)
            return;
        gesture.current = null;
        setActive(false);
        if (dialog.current?.hasPointerCapture?.(event.pointerId))
            dialog.current.releasePointerCapture(event.pointerId);
    };
    const keyboard = (event: KeyboardEvent<HTMLElement>, mode: 'move' | PreviewEdge) => {
        if (event.target !== event.currentTarget || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key))
            return;
        event.preventDefault();
        event.stopPropagation();
        const step = event.shiftKey ? 1 : 10;
        setLayout(value => ({ box: changePreviewBox(value.box, mode, event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0, event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0, viewport()), docked: false, floatingBox: null }));
    };
    const toggleDock = () => {
        const id = gesture.current?.id;
        gesture.current = null;
        setActive(false);
        if (id !== undefined && dialog.current?.hasPointerCapture?.(id))
            dialog.current.releasePointerCapture(id);
        setLayout(value => value.docked
            ? { box: constrainPreviewBox(value.floatingBox ?? value.box, viewport()), docked: false, floatingBox: null }
            : { box: rightDockedPreviewBox(viewport()), docked: true, floatingBox: value.box });
    };
    return { box: layout.box, docked: layout.docked, toggleDock, active, start, keyboard, finish, move: (event: PointerEvent<HTMLDialogElement>) => {
            const current = gesture.current;
            if (!current || current.id !== event.pointerId)
                return;
            setLayout({ box: changePreviewBox(current.box, current.mode, event.clientX - current.x, event.clientY - current.y, viewport()),
                docked: false, floatingBox: null });
        } };
}
