import type { HTMLAttributes } from 'react';
/** Keep table-row semantics and leave nested controls in charge of their own action. */
export function openableTableRow(label: string, open: () => void, disabled = false): HTMLAttributes<HTMLTableRowElement> {
    return {
        'aria-label': label,
        'aria-disabled': disabled ? true : undefined,
        className: disabled ? undefined : 'bms-openable-row',
        tabIndex: disabled ? undefined : 0,
        onClick: event => {
            if (!disabled && event.button === 0 && (!(event.target instanceof Element)
                || !event.target.closest('button, a, input, select, textarea, [role="button"], [contenteditable="true"]')))
                open();
        },
        onKeyDown: event => {
            if (!disabled && event.target === event.currentTarget && (event.key === 'Enter' || event.key === ' ')) {
                event.preventDefault();
                open();
            }
        },
    };
}
