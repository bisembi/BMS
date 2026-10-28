/** Native logical cells, including colspan/rowspan; nested tables never leak in. */
export type LogicalCell = {
    cell: HTMLTableCellElement;
    start: number;
    span: number;
};
export function logicalCells(table: HTMLTableElement): LogicalCell[] {
    const result: LogicalCell[] = [];
    const sections = [table.tHead, ...Array.from(table.tBodies), table.tFoot].filter((s): s is HTMLTableSectionElement => s !== null);
    for (const section of sections) {
        const occupied = new Map<number, number>();
        const rows = Array.from(section.rows);
        rows.forEach((row, rowIndex) => {
            let start = 0;
            for (const cell of Array.from(row.cells)) {
                while ((occupied.get(start) ?? 0) > rowIndex)
                    start += 1;
                const span = Math.max(1, cell.colSpan);
                const height = cell.rowSpan === 0 ? rows.length - rowIndex : cell.rowSpan;
                result.push({ cell, start, span });
                if (height > 1)
                    for (let offset = 0; offset < span; offset += 1)
                        occupied.set(start + offset, rowIndex + height);
                start += span;
            }
        });
    }
    return result;
}
export type Column = {
    key: string;
    label: string;
    width: number;
    initial: number;
    min: number;
    max: number;
};
export function readColumns(table: HTMLTableElement, previous: readonly Column[] = [], fitDefaults = true, cells = logicalCells(table)): Column[] {
    // Do not spread an unbounded row/cell count into a function call, and do
    // not repeatedly scan every body cell for each of the report's columns.
    let count = 0;
    const headers: LogicalCell[] = [], firstCells = new Map<number, HTMLTableCellElement>();
    for (const item of cells) {
        count = Math.max(count, item.start + item.span);
        if (item.cell.parentElement?.parentElement === table.tHead)
            headers.push(item);
        if (item.span === 1 && !firstCells.has(item.start))
            firstCells.set(item.start, item.cell);
    }
    const seen = new Map<string, number>();
    const expandedCols = Array.from(table.children).filter(e => e.tagName === 'COLGROUP').flatMap(group => Array.from(group.children).filter(e => e.tagName === 'COL').flatMap(e => Array.from({ length: (e as HTMLTableColElement).span }, () => e as HTMLTableColElement)));
    return Array.from({ length: count }, (_, index) => {
        const headings = headers.filter(c => c.start <= index && c.start + c.span > index);
        const heading = headings.at(-1);
        const label = (cell: HTMLTableCellElement) => cell.dataset.columnKey ?? cell.getAttribute('aria-label') ?? cell.textContent?.trim() ?? '';
        const text = headings.map(h => label(h.cell)).filter(Boolean).join(' / ') || `Столбец ${index + 1}`;
        const occurrence = seen.get(text) ?? 0;
        seen.set(text, occurrence + 1);
        const key = `${text}:${occurrence}`;
        const measured = firstCells.get(index)?.getBoundingClientRect().width
            ?? (heading ? heading.cell.getBoundingClientRect().width / heading.span : 100);
        const col = expandedCols[index];
        const colWidth = col ? col.getBoundingClientRect().width / Math.max(1, col.span) : 0;
        const initial = Number(heading?.cell.dataset.columnDefault) || colWidth || measured || 100;
        const min = Number(heading?.cell.dataset.columnMin) || 40;
        const max = Number(heading?.cell.dataset.columnMax) || 3000;
        const old = previous.find(c => c.key === key);
        if (table.dataset.bmsFitColumns === 'content') {
            return { key, label: text, initial, min, max: Math.max(min, max),
                // Automatic sizing follows content, not a stretched 100%-wide initial
                // table or stale page values. Explicit manual sizing still wins.
                width: Math.max(min, fitDefaults ? initial : (old?.width ?? initial)) };
        }
        return { key, label: text, initial: old?.initial ?? initial, width: old?.width ?? (colWidth || measured || initial), min, max };
    });
}
export function clampWidth(column: Column, width: number): number {
    return Number.isFinite(width) ? Math.max(column.min, Math.min(column.max, Math.round(width))) : column.width;
}
export type Rect = {
    left: number;
    right: number;
    top: number;
    bottom: number;
};
export function visibleTableRect(table: HTMLTableElement, viewport: Rect): Rect | null {
    const own = table.getBoundingClientRect();
    const result = { left: Math.max(own.left, viewport.left), right: Math.min(own.right, viewport.right), top: Math.max(own.top, viewport.top), bottom: Math.min(own.bottom, viewport.bottom) };
    for (let ancestor = table.parentElement; ancestor; ancestor = ancestor.parentElement) {
        const style = getComputedStyle(ancestor), rect = ancestor.getBoundingClientRect();
        // Client bounds exclude borders and scrollbars; overlays must not steal
        // native scrollbar interactions. The layout-less unit harness has zero clients.
        const left = rect.left + ancestor.clientLeft, top = rect.top + ancestor.clientTop;
        const right = ancestor.clientWidth ? left + ancestor.clientWidth : rect.right;
        const bottom = ancestor.clientHeight ? top + ancestor.clientHeight : rect.bottom;
        if (/(auto|scroll|hidden|clip)/u.test(style.overflowX)) {
            result.left = Math.max(result.left, left);
            result.right = Math.min(result.right, right);
        }
        if (/(auto|scroll|hidden|clip)/u.test(style.overflowY)) {
            result.top = Math.max(result.top, top);
            result.bottom = Math.min(result.bottom, bottom);
        }
    }
    return result.right > result.left && result.bottom > result.top ? result : null;
}
