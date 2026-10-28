/** Fixed resize rails live outside a table's native scroll ancestry. Route
 * their wheel to that ancestry, never to financial commands or column widths.
 * Leave browser zoom and an unconsumed boundary gesture alone. */
export function forwardTableRailWheel(table: HTMLTableElement, event: WheelEvent): boolean {
    if (event.defaultPrevented || !event.cancelable || event.ctrlKey || event.metaKey
        || !Number.isFinite(event.deltaX) || !Number.isFinite(event.deltaY)
        || ![0, 1, 2].includes(event.deltaMode))
        return false;
    const x = event.deltaX || (event.shiftKey ? event.deltaY : 0);
    const y = event.shiftKey ? 0 : event.deltaY;
    if (!x && !y)
        return false;
    const tableStyle = getComputedStyle(table);
    const line = Number.parseFloat(tableStyle.lineHeight)
        || (Number.parseFloat(tableStyle.fontSize) || 16) * 1.2;
    function move(axis: 'x' | 'y', delta: number): boolean {
        if (!delta)
            return false;
        for (let node = table.parentElement; node; node = node.parentElement) {
            const style = getComputedStyle(node);
            if (!/^(auto|scroll|overlay)$/u.test(axis === 'x' ? style.overflowX : style.overflowY))
                continue;
            const client = axis === 'x' ? node.clientWidth : node.clientHeight;
            const range = (axis === 'x' ? node.scrollWidth : node.scrollHeight) - client;
            if (client <= 0 || range <= 0)
                continue;
            const before = axis === 'x' ? node.scrollLeft : node.scrollTop;
            const rtl = axis === 'x' && style.direction === 'rtl';
            const minimum = rtl ? -range : 0, maximum = rtl ? 0 : range;
            const factor = event.deltaMode === 1 ? line : event.deltaMode === 2 ? client : 1;
            const after = Math.max(minimum, Math.min(maximum, before + delta * factor));
            if (Math.abs(after - before) < 0.01)
                continue;
            if (axis === 'x')
                node.scrollLeft = after;
            else
                node.scrollTop = after;
            return true;
        }
        return false;
    }
    // Evaluate both axes; a diagonal touchpad gesture may use different native
    // ancestors. Do not short-circuit and silently discard its vertical part.
    const movedX = move('x', x), movedY = move('y', y);
    if (movedX || movedY) {
        event.preventDefault();
        return true;
    }
    return false;
}
