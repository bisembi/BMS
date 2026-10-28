import { useLayoutEffect, useRef, type InputHTMLAttributes } from 'react';
import { canonicalMoneyText, formatEditableMoney, normalizeEditableMoney } from '../../utils/moneyInput';
type Props = Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange'> & {
    value: string;
    onValueChange: (value: string) => void;
};
const grouping = /[ \u00a0\u202f]/gu;
export function editMoneyText(previous: string, raw: string, inputType: string, data: string | null): string {
    const keyboard = (inputType === 'insertText' && data !== null && /^[\d.,-]$/u.test(data))
        || inputType === 'deleteContentBackward' || inputType === 'deleteContentForward';
    // Strip only formatter-added spaces during a known single-key edit. Pasted
    // and otherwise malformed amounts still go through the strict whole-input grammar.
    if (keyboard && canonicalMoneyText(previous, true) !== null) {
        const candidate = raw.replace(grouping, '');
        if (canonicalMoneyText(candidate, true) !== null)
            return normalizeEditableMoney(candidate);
    }
    return normalizeEditableMoney(raw);
}
function visualCaret(text: string, logical: number): number {
    if (logical <= 0)
        return 0;
    let seen = 0;
    for (let index = 0; index < text.length; index++) {
        if (!/[ \u00a0\u202f]/u.test(text[index]) && ++seen === logical)
            return index + 1;
    }
    return text.length;
}
export function MoneyInput({ value, onValueChange, ...props }: Props) {
    const ref = useRef<HTMLInputElement>(null);
    const caret = useRef<{
        start: number;
        end: number;
    } | null>(null);
    const display = formatEditableMoney(value);
    useLayoutEffect(() => {
        const next = caret.current, input = ref.current;
        if (next && input && document.activeElement === input) {
            input.setSelectionRange(visualCaret(display, next.start), visualCaret(display, next.end));
        }
        caret.current = null;
    });
    return <input {...props} ref={ref} type="text" inputMode="decimal" value={display} onChange={event => {
            const input = event.currentTarget, native = event.nativeEvent as InputEvent;
            const count = (end: number | null) => input.value.slice(0, end ?? input.value.length).replace(grouping, '').length;
            caret.current = { start: count(input.selectionStart), end: count(input.selectionEnd) };
            onValueChange(editMoneyText(display, input.value, native.inputType ?? '', native.data ?? null));
        }}/>;
}
