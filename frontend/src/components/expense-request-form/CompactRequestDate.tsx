import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { CalendarDays } from 'lucide-react';
export function compactRequestDate(value: string): string {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/u.exec(value);
    return match ? `${match[3]}.${match[2]}.${match[1].slice(2)}` : '';
}
export function parseCompactRequestDate(value: string): string | null {
    const match = /^(\d{2})\.(\d{2})\.(\d{2})$/u.exec(value);
    if (!match)
        return null;
    const iso = `20${match[3]}-${match[2]}-${match[1]}`;
    const date = new Date(`${iso}T00:00:00Z`);
    return Number.isFinite(date.valueOf()) && date.toISOString().slice(0, 10) === iso ? iso : null;
}
export function CompactRequestDate({ value, onChange }: {
    value: string;
    onChange: (value: string) => void;
}) {
    const [text, setText] = useState(() => compactRequestDate(value));
    // Invalid manual input deliberately emits an empty canonical value while
    // retaining the visible text for correction. An external empty value is a
    // real reset, however, and must never keep a previous document's date.
    const emitted = useRef<string | null>(null);
    useEffect(() => {
        if (emitted.current === value) {
            emitted.current = null;
            return;
        }
        emitted.current = null;
        setText(compactRequestDate(value));
    }, [value]);
    const invalid = text !== '' && parseCompactRequestDate(text) === null;
    const root = useRef<HTMLSpanElement>(null);
    const input = useRef<HTMLInputElement>(null);
    const popup = useRef<HTMLDivElement>(null);
    const id = useId();
    const [open, setOpen] = useState(false);
    const [month, setMonth] = useState('');
    const [position, setPosition] = useState({ left: 0, top: 0 });
    const isDisabled = () => input.current?.matches(':disabled') ?? true;
    const close = (restoreFocus = false) => {
        setOpen(false);
        if (restoreFocus && !isDisabled())
            input.current?.focus();
    };
    function openCalendar() {
        if (isDisabled())
            return;
        const parsed = new Date(`${value}T00:00:00Z`);
        const selected = /^\d{4}-\d{2}-\d{2}$/u.test(value) && Number.isFinite(parsed.valueOf())
            && parsed.getUTCFullYear() > 0 && parsed.toISOString().slice(0, 10) === value ? value : businessToday();
        setMonth(selected.slice(0, 7));
        setOpen(true);
    }
    useLayoutEffect(() => {
        if (!open)
            return;
        if (isDisabled()) {
            setOpen(false);
            return;
        }
        const box = root.current?.getBoundingClientRect();
        if (!box)
            return;
        setPosition({ left: Math.max(4, Math.min(box.left, window.innerWidth - 284)),
            top: box.bottom + 310 > window.innerHeight ? Math.max(4, box.top - 310) : box.bottom + 4 });
        const day = popup.current?.querySelector<HTMLButtonElement>('[data-selected="true"]') ?? popup.current?.querySelector<HTMLButtonElement>('[data-in-month="true"]');
        day?.focus();
    }, [open, month]);
    // The popup is outside the fieldset in a portal: re-check the original input
    // on every action, and dismiss when a refresh/workflow locks the form.
    useLayoutEffect(() => {
        if (open && isDisabled())
            setOpen(false);
    });
    useEffect(() => {
        if (!open)
            return;
        const outside = (event: PointerEvent) => {
            if (!root.current?.contains(event.target as Node) && !popup.current?.contains(event.target as Node))
                setOpen(false);
        };
        const dismiss = () => setOpen(false);
        document.addEventListener('pointerdown', outside, true);
        window.addEventListener('scroll', dismiss, true);
        window.addEventListener('resize', dismiss);
        return () => { document.removeEventListener('pointerdown', outside, true); window.removeEventListener('scroll', dismiss, true); window.removeEventListener('resize', dismiss); };
    }, [open]);
    const first = new Date(`${month || '2026-01'}-01T00:00:00Z`);
    const start = new Date(first);
    start.setUTCDate(1 - (first.getUTCDay() + 6) % 7);
    const days = Array.from({ length: 42 }, (_, index) => { const day = new Date(start); day.setUTCDate(start.getUTCDate() + index); return day; });
    const changeMonth = (delta: number) => {
        if (isDisabled()) {
            close();
            return;
        }
        const next = new Date(first);
        next.setUTCMonth(next.getUTCMonth() + delta);
        if (next.getUTCFullYear() > 0 && next.getUTCFullYear() < 10000)
            setMonth(next.toISOString().slice(0, 7));
    };
    return <span className="rz-compact-date" ref={root}>
    <input aria-label="Планируемая дата оплаты" placeholder="ДД.ММ.ГГ" inputMode="numeric" maxLength={8} ref={input} aria-haspopup="dialog" aria-expanded={open} aria-controls={open ? id : undefined} onClick={openCalendar} onKeyDown={event => {
            if (event.key === 'Enter' || (event.altKey && event.key === 'ArrowDown')) {
                event.preventDefault();
                openCalendar();
            }
            if (event.key === 'Escape')
                close();
        }} value={text} aria-invalid={invalid || undefined} onChange={(e) => {
            setOpen(false);
            setText(e.target.value);
            const canonical = parseCompactRequestDate(e.target.value) ?? '';
            emitted.current = canonical;
            onChange(canonical);
        }}/>
    <button className="rz-calendar-button" type="button" aria-label="Выбрать дату оплаты в календаре" aria-haspopup="dialog" aria-expanded={open} aria-controls={open ? id : undefined} onClick={() => open ? close(true) : openCalendar()}><CalendarDays size={13} aria-hidden="true"/></button>
    {open && createPortal(<div ref={popup} id={id} role="dialog" aria-label="Календарь даты оплаты" className="rz-date-popover" style={position} onBlur={event => {
                if (event.relatedTarget && !popup.current?.contains(event.relatedTarget as Node) && !root.current?.contains(event.relatedTarget as Node))
                    close();
            }} onKeyDown={event => {
                if (event.key === 'Escape') {
                    event.preventDefault();
                    event.stopPropagation();
                    close(true);
                    return;
                }
                const offset = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }[event.key];
                if (offset !== undefined) {
                    const buttons = Array.from(popup.current?.querySelectorAll<HTMLButtonElement>('button[data-date]') ?? []);
                    const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
                    if (index >= 0) {
                        event.preventDefault();
                        buttons[Math.max(0, Math.min(buttons.length - 1, index + offset))]?.focus();
                    }
                }
            }}>
      <div className="rz-date-navigation"><button type="button" aria-label="Предыдущий месяц" onClick={() => changeMonth(-1)}>‹</button>
        <strong aria-live="polite">{new Intl.DateTimeFormat('ru-RU', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(first)}</strong>
        <button type="button" aria-label="Следующий месяц" onClick={() => changeMonth(1)}>›</button></div>
      <div className="rz-date-grid">{['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс'].map(day => <span className="rz-date-weekday" key={day}>{day}</span>)}
        {days.map(day => {
                const iso = day.toISOString().slice(0, 10);
                const inMonth = iso.slice(0, 7) === month;
                return <button key={iso} type="button" data-date={iso} data-in-month={inMonth} data-selected={iso === value} disabled={day.getUTCFullYear() < 1 || day.getUTCFullYear() > 9999} aria-label={new Intl.DateTimeFormat('ru-RU', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }).format(day)} aria-pressed={iso === value} className={inMonth ? '' : 'outside-month'} onClick={() => {
                        if (isDisabled()) {
                            close();
                            return;
                        }
                        setText(compactRequestDate(iso));
                        onChange(iso);
                        close(true);
                    }}>{day.getUTCDate()}</button>;
            })}</div>
    </div>, document.body)}
  </span>;
}
function businessToday(): string {
    const parts = new Intl.DateTimeFormat('en-US', { year: 'numeric', month: '2-digit', day: '2-digit', timeZone: 'Asia/Qyzylorda' }).formatToParts(new Date());
    return `${parts.find(x => x.type === 'year')!.value}-${parts.find(x => x.type === 'month')!.value}-${parts.find(x => x.type === 'day')!.value}`;
}
