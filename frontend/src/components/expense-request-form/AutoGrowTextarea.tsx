import { useLayoutEffect, useRef, type TextareaHTMLAttributes } from 'react';
type Props = Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'defaultValue' | 'value'> & {
    value: string;
};
// Presentation only: retain the native textarea, label, permissions and change payload.
export function AutoGrowTextarea({ rows = 2, ...props }: Props) {
    const input = useRef<HTMLTextAreaElement>(null);
    useLayoutEffect(() => {
        const element = input.current;
        if (!element)
            return;
        let active = true;
        let measured = '';
        const measure = (force = false) => {
            if (!active || element.clientWidth === 0)
                return; // Hidden tabs are measured when revealed.
            const style = getComputedStyle(element);
            const key = JSON.stringify([element.clientWidth, element.value, style.font, style.lineHeight,
                style.paddingTop, style.paddingBottom, style.borderTopWidth, style.borderBottomWidth, style.minHeight]);
            if (!force && measured === key)
                return; // Ignore observer callbacks caused by our own height.
            measured = key;
            element.style.height = 'auto';
            const border = (Number.parseFloat(style.borderTopWidth) || 0) + (Number.parseFloat(style.borderBottomWidth) || 0);
            element.style.height = `${Math.ceil(Math.max(Number.parseFloat(style.minHeight) || 0, element.scrollHeight + border))}px`;
        };
        const resize = () => measure();
        measure();
        const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(resize);
        observer?.observe(element);
        window.addEventListener('resize', resize);
        void document.fonts?.ready.then(() => {
            if (active)
                measure(true);
        });
        return () => { active = false; observer?.disconnect(); window.removeEventListener('resize', resize); };
    }, [props.value, props.disabled, props.readOnly, rows]);
    return <textarea {...props} rows={rows} ref={input} style={{ ...props.style, boxSizing: 'border-box', overflowY: 'hidden', maxHeight: 'none' }}/>;
}
