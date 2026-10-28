import { CalendarDays } from 'lucide-react';
import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { formatBudgetMonthDisplay } from '../../utils/budgetPeriodDisplay';
interface Props {
    value: string;
    ariaLabel: string;
    disabled?: boolean;
    required?: boolean;
    onChange: (value: string) => void;
}
/** Native ISO month input, with a deterministic compact presentation. */
export function CompactMonthInput({ value, ariaLabel, disabled, required, onChange }: Props) {
    const input = useRef<HTMLInputElement>(null);
    const fallback = useRef<HTMLDivElement>(null);
    const [fallbackOpen, setFallbackOpen] = useState(false);
    const [position, setPosition] = useState({ left: 0, top: 0 });
    useLayoutEffect(() => {
        if (!fallbackOpen || disabled)
            return;
        const box = input.current?.getBoundingClientRect();
        if (!box)
            return;
        setPosition({ left: Math.max(4, Math.min(box.left, window.innerWidth - 254)),
            top: box.bottom + 55 > window.innerHeight ? Math.max(4, box.top - 55) : box.bottom + 4 });
        fallback.current?.querySelector('input')?.focus();
    }, [fallbackOpen, disabled]);
    useEffect(() => {
        if (!fallbackOpen)
            return;
        const outside = (event: PointerEvent) => {
            if (!fallback.current?.contains(event.target as Node) && event.target !== input.current)
                setFallbackOpen(false);
        };
        const close = () => setFallbackOpen(false);
        document.addEventListener('pointerdown', outside, true);
        window.addEventListener('resize', close);
        window.addEventListener('scroll', close, true);
        return () => {
            document.removeEventListener('pointerdown', outside, true);
            window.removeEventListener('resize', close);
            window.removeEventListener('scroll', close, true);
        };
    }, [fallbackOpen]);
    function openFallback() {
        setFallbackOpen(true);
        // A repeated click can leave the same state true after blur/click batching.
        // Focus the existing editor as well as focusing newly mounted editors.
        fallback.current?.querySelector('input')?.focus();
    }
    function openPicker() {
        if (disabled)
            return;
        try {
            if (typeof input.current?.showPicker === 'function') {
                input.current.showPicker();
                return;
            }
        }
        catch { /* Unsupported context: expose a visible, editable native input. */ }
        openFallback();
    }
    return <span className={`compact-month-input${disabled ? ' compact-month-disabled' : ''}`}>
    <span className="compact-month-value" aria-hidden="true">{formatBudgetMonthDisplay(value)}</span>
    <CalendarDays size={13} aria-hidden="true"/>
    <input ref={input} className="compact-month-native" aria-label={ariaLabel} type="month" title={value} value={value} disabled={disabled} required={required} onChange={event => onChange(event.target.value)} onClick={event => { event.preventDefault(); openPicker(); }} onKeyDown={event => {
            if (disabled)
                return;
            if (event.key === 'Enter' || event.key === ' ' || event.key === 'ArrowDown') {
                event.preventDefault();
                openPicker();
            }
            else if (event.key === 'ArrowUp' || event.key === 'ArrowLeft' || event.key === 'ArrowRight'
                || event.key === 'Backspace' || event.key === 'Delete' || /^\d$/u.test(event.key)) {
                event.preventDefault();
                openFallback();
            }
        }}/>
    {fallbackOpen && !disabled && createPortal(<div ref={fallback} className="compact-month-fallback" style={position} onBlur={event => {
                if (!event.currentTarget.contains(event.relatedTarget as Node | null))
                    setFallbackOpen(false);
            }}>
      <input type="month" aria-label={`${ariaLabel}, ввод`} value={value} onChange={event => { onChange(event.target.value); }} onKeyDown={event => {
                if (event.key === 'Escape' || event.key === 'Enter') {
                    event.preventDefault();
                    setFallbackOpen(false);
                    input.current?.focus();
                }
            }}/>
    </div>, document.body)}
  </span>;
}
