type Measure = (text: string, cell: HTMLTableCellElement) => number;
/** Only list presentation. Values stay in the browser; no new API requests. */
export function fitRequestColumns(table: HTMLTableElement, measure?: Measure): void {
    const headers = Array.from(table.tHead?.rows[0]?.cells ?? []);
    if (!headers.length)
        return;
    const context = measure || typeof CanvasRenderingContext2D === 'undefined'
        ? null : document.createElement('canvas').getContext('2d');
    const styles = new Map<HTMLTableCellElement, CSSStyleDeclaration>();
    const styleOf = (cell: HTMLTableCellElement) => {
        let style = styles.get(cell);
        if (!style) {
            style = getComputedStyle(cell);
            styles.set(cell, style);
        }
        return style;
    };
    const number = (value: string) => Number.parseFloat(value) || 0;
    const widthOf = (cell: HTMLTableCellElement) => {
        const style = styleOf(cell);
        const lines = Array.from(cell.querySelectorAll('.request-header-line'));
        const texts = (lines.length ? lines.map(line => line.textContent ?? '') : [cell.textContent ?? ''])
            .map(text => text.trim().replace(/\s+/gu, ' '));
        let textWidth = 0;
        if (measure)
            textWidth = Math.max(...texts.map(text => measure(text, cell)));
        else if (context) {
            context.font = `${style.fontStyle || 'normal'} ${style.fontWeight || 'normal'} ${style.fontSize || '13px'} ${style.fontFamily || 'sans-serif'}`;
            textWidth = Math.max(...texts.map(text => context.measureText(text).width
                + Math.max(0, text.length - 1) * number(style.letterSpacing)));
        }
        else
            textWidth = cell.scrollWidth;
        const button = cell.querySelector('button');
        const buttonPadding = button ? number(getComputedStyle(button).paddingLeft) + number(getComputedStyle(button).paddingRight) : 0;
        // A plain sort button stretches to the current header width. Measuring that
        // width as intrinsic content would add cell padding on every render, growing
        // the column indefinitely. Its label is already measured above.
        const atom = cell.tagName === 'TH' && button?.classList.contains('request-sort')
            ? null : cell.querySelector('.request-row-actions, button, .status-chip, .urgency-chip, .budget-indicator');
        const contentWidth = Math.max(textWidth + buttonPadding, atom?.getBoundingClientRect().width ?? 0);
        return Math.ceil(contentWidth + number(style.paddingLeft) + number(style.paddingRight)
            + number(style.borderLeftWidth) + number(style.borderRightWidth) + 4);
    };
    const widths = headers.map(widthOf);
    // A long purpose/name wraps or is truncated locally; never create multi-thousand-pixel columns.
    for (const body of Array.from(table.tBodies))
        for (const row of Array.from(body.rows)) {
            if (row.cells.length !== headers.length || Array.from(row.cells).some(cell => cell.colSpan !== 1))
                continue;
            Array.from(row.cells).forEach((cell, index) => { widths[index] = Math.max(widths[index], Math.min(420, widthOf(cell))); });
        }
    headers.forEach((cell, index) => {
        const min = Math.max(40, widthOf(cell));
        for (const [name, value] of Object.entries({ columnMin: String(min), columnDefault: String(Math.max(min, widths[index])) })) {
            if (cell.dataset[name] !== value)
                cell.dataset[name] = value;
        }
    });
}
