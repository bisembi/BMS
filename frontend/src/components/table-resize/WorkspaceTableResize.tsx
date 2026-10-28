import { useEffect, useRef } from 'react';
import { clampWidth, logicalCells, readColumns, visibleTableRect, type Column, type LogicalCell } from './tableColumns';
import { forwardTableRailWheel } from './tableRailWheel';
import '../../styles/table-resize.css';
type TableState = {
    columns: Column[];
    cells: LogicalCell[];
    structureDirty: boolean;
    rails: HTMLSpanElement[];
    changed: boolean;
    signature: string;
    needsApply: boolean;
    manuallyResized?: boolean;
    group?: HTMLTableColElement;
};
type Drag = {
    state: TableState;
    table: HTMLTableElement;
    index: number;
    pointer: number;
    x: number;
    width: number;
    rail: HTMLSpanElement;
};
const isScreenTable = (table: HTMLTableElement) => !table.closest('.expense-request-print-summary, [data-print-only], [hidden]');
/** Workspace-wide, view-only behavior. Lazy/future native tables require no opt-in. */
export function WorkspaceTableResize() {
    const layerRef = useRef<HTMLDivElement>(null);
    useEffect(() => {
        const foundLayer = layerRef.current, foundScope = foundLayer?.parentElement?.querySelector<HTMLElement>('.workspace-panel');
        if (!foundLayer || !foundScope)
            return;
        const layer = foundLayer, scope = foundScope;
        const states = new WeakMap<HTMLTableElement, TableState>();
        const active = new Map<HTMLTableElement, TableState>();
        let drag: Drag | null = null, frame = 0, destroyed = false;
        const schedule = () => {
            if (!frame && !destroyed)
                frame = requestAnimationFrame(reconcile);
        };
        const setProperty = (style: CSSStyleDeclaration, name: string, value: string) => {
            if (style.getPropertyValue(name) !== value)
                style.setProperty(name, value);
        };
        function apply(table: HTMLTableElement, state: TableState) {
            const widths = state.columns.map(c => c.width);
            if (table.dataset.bmsResizable !== 'true')
                table.dataset.bmsResizable = 'true';
            setProperty(table.style, '--bms-table-width', `${widths.reduce((sum, width) => sum + width, 0)}px`);
            // Own a screen-only colgroup; never rewrite React's col/span nodes.
            if (!state.group || state.group.parentElement !== table) {
                state.group = document.createElement('colgroup');
                state.group.className = 'bms-managed-colgroup';
                table.insertBefore(state.group, table.firstChild);
            }
            while (state.group.children.length < widths.length)
                state.group.appendChild(document.createElement('col'));
            while (state.group.children.length > widths.length)
                state.group.lastElementChild!.remove();
            widths.forEach((width, index) => setProperty((state.group!.children[index] as HTMLTableColElement).style, 'width', `${width}px`));
            for (const { cell, start, span } of logicalCells(table)) {
                if (cell.dataset.bmsResizeCell !== 'true')
                    cell.dataset.bmsResizeCell = 'true';
                setProperty(cell.style, '--bms-cell-width', `${widths.slice(start, start + span).reduce((sum, width) => sum + width, 0)}px`);
            }
            state.changed = true;
            state.needsApply = false;
        }
        function resize(table: HTMLTableElement, state: TableState, index: number, requested: number) {
            const column = state.columns[index];
            if (!column)
                return;
            state.manuallyResized = true;
            column.width = clampWidth(column, requested);
            // A controlled budget-row adapter updates its existing React state and
            // limits. The universal state then agrees with its colgroup/total width.
            table.dispatchEvent(new CustomEvent('bms:column-width', { detail: { index, width: column.width } }));
            apply(table, state);
            schedule();
        }
        function end(cancel: boolean) {
            const current = drag;
            if (!current)
                return;
            drag = null;
            if (cancel)
                resize(current.table, current.state, current.index, current.width);
            current.rail.classList.remove('is-dragging');
            if (current.rail.hasPointerCapture?.(current.pointer))
                current.rail.releasePointerCapture(current.pointer);
        }
        function railFor(table: HTMLTableElement, state: TableState, index: number) {
            const rail = document.createElement('span');
            rail.className = 'bms-table-column-rail';
            rail.tabIndex = 0;
            rail.setAttribute('role', 'separator');
            rail.setAttribute('aria-orientation', 'vertical');
            rail.title = 'Перетащите границу по всей таблице; ←/→ — ширина; двойной щелчок — исходная ширина';
            rail.addEventListener('wheel', event => {
                if (forwardTableRailWheel(table, event))
                    schedule();
            }, { passive: false });
            rail.addEventListener('pointerdown', event => {
                if (event.button !== 0 || event.isPrimary === false || drag || !Number.isFinite(event.clientX))
                    return;
                refreshUnchanged(table, state);
                event.preventDefault();
                event.stopPropagation();
                rail.focus({ preventScroll: true });
                rail.setPointerCapture?.(event.pointerId);
                drag = { table, state, index, pointer: event.pointerId, x: event.clientX, width: state.columns[index].width, rail };
                rail.classList.add('is-dragging');
            });
            rail.addEventListener('pointermove', event => {
                const current = drag;
                if (!current || current.rail !== rail || current.pointer !== event.pointerId || !Number.isFinite(event.clientX))
                    return;
                event.preventDefault();
                resize(table, state, index, current.width + event.clientX - current.x);
            });
            rail.addEventListener('pointerup', event => {
                if (drag?.rail === rail && drag.pointer === event.pointerId)
                    end(false);
            });
            rail.addEventListener('pointercancel', event => {
                if (drag?.rail === rail && drag.pointer === event.pointerId)
                    end(true);
            });
            rail.addEventListener('lostpointercapture', event => {
                if (drag?.rail === rail && drag.pointer === event.pointerId)
                    end(true);
            });
            rail.addEventListener('dblclick', event => { event.preventDefault(); resize(table, state, index, state.columns[index].initial); });
            rail.addEventListener('keydown', event => {
                refreshUnchanged(table, state);
                const column = state.columns[index], step = event.shiftKey ? 50 : 10;
                if (event.key === 'Escape' && drag)
                    end(true);
                else if (event.key === 'ArrowLeft')
                    resize(table, state, index, column.width - step);
                else if (event.key === 'ArrowRight')
                    resize(table, state, index, column.width + step);
                else if (event.key === 'Home')
                    resize(table, state, index, column.min);
                else if (event.key === 'End')
                    resize(table, state, index, column.max);
                else
                    return;
                event.preventDefault();
                event.stopPropagation();
            });
            // Native showModal() makes the background inert, regardless of z-index.
            (table.closest('dialog') ?? layer).appendChild(rail);
            return rail;
        }
        function refreshUnchanged(table: HTMLTableElement, state: TableState) {
            if (state.changed)
                return;
            state.columns = readColumns(table).map(column => ({
                ...column, initial: state.columns.find(old => old.key === column.key)?.initial ?? column.initial,
            }));
        }
        function reconcile() {
            frame = 0;
            if (destroyed)
                return;
            const tables = Array.from(scope.querySelectorAll<HTMLTableElement>('table')).filter(isScreenTable);
            for (const [table, state] of active)
                if (!tables.includes(table)) {
                    if (drag?.table === table)
                        end(true);
                    state.rails.forEach(rail => rail.remove());
                    state.rails = [];
                    active.delete(table);
                    observer?.unobserve(table);
                }
            for (const table of tables) {
                let state = states.get(table);
                // Scrolling changes geometry, not logical rowspan/colspan membership.
                // Rebuild only after DOM structure changes. Headers stay in table flow.
                const cells = !state || state.structureDirty ? logicalCells(table) : state.cells;
                const rect = visibleTableRect(table, { left: 0, top: 0, right: innerWidth, bottom: innerHeight });
                // Hidden/lazy hosts have no measurable layout. Do not freeze fallback
                // widths before the user first sees their real table.
                if (!state && !rect)
                    continue;
                // One fresh column read, not two full table traversals per scroll frame.
                const fitContent = table.dataset.bmsFitColumns === 'content';
                const columns = readColumns(table, state?.changed ? state.columns : [], !state?.manuallyResized, cells).map(column => ({
                    ...column, initial: fitContent ? column.initial : state?.columns.find(old => old.key === column.key)?.initial ?? column.initial,
                }));
                const signature = columns.map(c => c.key).join('\n');
                if (!state) {
                    state = { columns, cells, structureDirty: false, rails: [], changed: false, needsApply: false, signature };
                    states.set(table, state);
                }
                state.cells = cells;
                state.structureDirty = false;
                if (state.signature !== signature) {
                    if (drag?.table === table)
                        end(true);
                    state.rails.forEach(rail => rail.remove());
                    state.rails = [];
                    state.columns = columns;
                    state.signature = signature;
                    state.needsApply = true;
                }
                if (fitContent) {
                    const different = !state.changed || columns.some((column, index) => column.width !== state!.columns[index]?.width);
                    state.columns = columns;
                    if (different) {
                        state.needsApply = true;
                        apply(table, state);
                    }
                }
                else if (!state.changed)
                    state.columns = columns;
                if (!active.has(table)) {
                    active.set(table, state);
                    observer?.observe(table);
                }
                // Scrolling changes screen coordinates, not every cell's saved width.
                if (state.changed && state.needsApply)
                    apply(table, state);
                if (table.dataset.bmsResizeEnhanced !== 'true')
                    table.dataset.bmsResizeEnhanced = 'true';
                if (!state.rails.length)
                    state.rails = state.columns.map((_, index) => railFor(table, state!, index));
                // Header cells define screen positions too, including sticky columns.
                const headers = cells.filter(c => c.cell.parentElement?.parentElement === table.tHead);
                const stickyHeaders = headers.filter(c => { const style = getComputedStyle(c.cell); return style.position === 'sticky' && style.left !== 'auto'; });
                const stickyRight = stickyHeaders.reduce((right, c) => Math.max(right, c.cell.getBoundingClientRect().right), -Infinity);
                const nestedRects = Array.from(table.querySelectorAll('table')).map(child => child.getBoundingClientRect());
                // Read all geometry before any rail writes; interleaving them forces
                // a synchronous layout for each month column during horizontal scroll.
                const positions = state.columns.map((column, index) => {
                    const leaf = cells.find(c => c.span === 1 && c.start === index && c.cell.parentElement?.parentElement === table.tHead)
                        ?? cells.find(c => c.span === 1 && c.start === index);
                    const boundary = leaf?.cell.getBoundingClientRect().right ?? table.getBoundingClientRect().left + state!.columns.slice(0, index + 1).reduce((sum, c) => sum + c.width, 0);
                    const stickyLeaf = leaf && getComputedStyle(leaf.cell).position === 'sticky';
                    const visible = rect && boundary >= rect.left - 1 && boundary <= rect.right + 1 && (stickyLeaf || boundary > stickyRight + 1);
                    return { column, boundary, visible };
                });
                positions.forEach(({ column, boundary, visible }, index) => {
                    const rail = state!.rails[index];
                    if (rail.hidden !== !visible)
                        rail.hidden = !visible;
                    if (rect && visible) {
                        setProperty(rail.style, 'left', `${boundary - 3}px`);
                        setProperty(rail.style, 'top', `${rect.top}px`);
                        setProperty(rail.style, 'height', `${rect.bottom - rect.top}px`);
                        // A parent's boundary must never cover a nested table's inputs.
                        // Subtract only those vertical portions; each child has its own rails.
                        const gaps = nestedRects
                            .filter(child => boundary >= child.left && boundary <= child.right && child.bottom > rect.top && child.top < rect.bottom)
                            .map(child => [Math.max(0, child.top - rect.top), Math.min(rect.bottom - rect.top, child.bottom - rect.top)])
                            .sort((a, b) => a[0] - b[0]);
                        let from = 0;
                        const regions: string[] = [];
                        const add = (to: number) => {
                            if (to > from)
                                regions.push(`M0 ${from}H6V${to}H0Z`);
                        };
                        for (const [top, bottom] of gaps) {
                            add(top);
                            from = Math.max(from, bottom);
                        }
                        add(rect.bottom - rect.top);
                        setProperty(rail.style, 'clip-path', gaps.length ? `path('${regions.join(' ')}')` : '');
                    }
                    for (const [name, value] of Object.entries({ 'aria-label': `Ширина столбца «${column.label}»`,
                        'aria-valuemin': String(column.min), 'aria-valuemax': String(column.max),
                        'aria-valuenow': String(column.width), 'aria-valuetext': `${Math.round(column.width)} px` })) {
                        if (rail.getAttribute(name) !== value)
                            rail.setAttribute(name, value);
                    }
                });
            }
        }
        const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(schedule);
        const mutations = new MutationObserver(records => {
            for (const record of records)
                if (record.target instanceof Element) {
                    const table = record.target.closest('table');
                    const state = table ? states.get(table) : undefined;
                    if (state && (record.type === 'childList' || record.attributeName === 'colspan' || record.attributeName === 'rowspan')) {
                        state.structureDirty = true;
                        if (state.changed)
                            state.needsApply = true;
                    }
                }
            schedule();
        });
        mutations.observe(scope, { childList: true, subtree: true, attributes: true, attributeFilter: ['style', 'class', 'hidden', 'open', 'colspan', 'rowspan', 'data-column-min', 'data-column-default'] });
        // A fixed-width table does not resize when the sidebar changes the workspace
        // origin or clipping width. Reposition only the column-resizing rails.
        observer?.observe(scope);
        for (let ancestor = scope.parentElement; ancestor; ancestor = ancestor.parentElement) {
            mutations.observe(ancestor, { attributes: true, attributeFilter: ['class', 'style', 'hidden'] });
        }
        function onScroll() { schedule(); }
        scope.addEventListener('scroll', onScroll, { capture: true, passive: true });
        window.addEventListener('scroll', onScroll, { capture: true, passive: true });
        window.addEventListener('resize', schedule);
        window.addEventListener('blur', cancel);
        window.addEventListener('beforeprint', beforePrint);
        window.addEventListener('afterprint', schedule);
        function cancel() { end(true); }
        function beforePrint() { end(true); }
        reconcile();
        return () => {
            destroyed = true;
            end(true);
            cancelAnimationFrame(frame);
            mutations.disconnect();
            observer?.disconnect();
            scope.removeEventListener('scroll', onScroll, true);
            window.removeEventListener('scroll', onScroll, true);
            window.removeEventListener('resize', schedule);
            window.removeEventListener('blur', cancel);
            window.removeEventListener('beforeprint', beforePrint);
            window.removeEventListener('afterprint', schedule);
            layer.replaceChildren();
            active.forEach(state => state.rails.forEach(rail => rail.remove()));
        };
    }, []);
    return <div className="bms-table-resize-layer" ref={layerRef}/>;
}
