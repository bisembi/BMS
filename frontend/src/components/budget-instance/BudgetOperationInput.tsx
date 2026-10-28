import { useLayoutEffect, useRef, type TextareaHTMLAttributes } from 'react';
export function BudgetOperationInput(props: TextareaHTMLAttributes<HTMLTextAreaElement>) {
    const input = useRef<HTMLTextAreaElement>(null);
    const measured = useRef<{
        width: number;
        value: string;
    } | null>(null);
    useLayoutEffect(() => {
        const element = input.current;
        if (!element)
            return;
        const measure = () => {
            const width = element.clientWidth;
            if (!width || (measured.current?.width === width && measured.current.value === element.value))
                return;
            measured.current = { width, value: element.value };
            element.style.height = 'auto';
            const style = getComputedStyle(element);
            const border = (Number.parseFloat(style.borderTopWidth) || 0) + (Number.parseFloat(style.borderBottomWidth) || 0);
            element.style.height = `${Math.max(25, Math.ceil(element.scrollHeight + border))}px`;
        };
        measure();
        const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure);
        observer?.observe(element);
        window.addEventListener('resize', measure);
        return () => { observer?.disconnect(); window.removeEventListener('resize', measure); };
    }, [props.value]);
    return <textarea {...props} ref={input} rows={1} className={`budget-operation-input ${props.className ?? ''}`}/>;
}
