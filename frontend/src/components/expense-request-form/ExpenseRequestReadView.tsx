import { useId, type ReactNode } from 'react';
import type { ReferenceDataItem } from '../../types';
import { sameOpaqueIdentifier } from '../../utils/expenseRequestDraft';
import './ExpenseRequestReadView.css';
/** Presentation only. Never derives edit/approval authority or changes a draft. */
export function ExpenseReadField({ label, children, wide = false, note }: {
    label: string;
    children?: ReactNode;
    wide?: boolean;
    note?: ReactNode;
}) {
    const labelId = useId();
    const empty = children == null || (typeof children === 'string' && children.trim() === '');
    return <div className={`expense-view-field${wide ? ' expense-view-wide' : ''}`}>
    <dt id={labelId}>{label}</dt>
    <dd aria-labelledby={labelId}>
      <div className="expense-view-value">{empty ? <span className="expense-view-empty">Не указано</span> : children}</div>
      {note}
    </dd>
  </div>;
}
export function expenseViewReference(items: readonly ReferenceDataItem[], id: string, unavailable: string, withCode = false): string {
    const item = items.find(candidate => sameOpaqueIdentifier(candidate.id, id));
    if (!item)
        return id ? unavailable : 'Не указано';
    // CF-* is a technical key, not the public DDS code.
    const code = withCode && !/^CF-/iu.test(item.code) ? item.code.trim() : '';
    return code ? `${code} — ${item.name}` : item.name;
}
export function expenseViewDate(value: string): string {
    if (!value)
        return 'Не указано';
    if (!/^\d{4}-\d{2}-\d{2}$/u.test(value))
        return 'Дата недоступна';
    const date = new Date(`${value}T00:00:00Z`);
    return !Number.isNaN(date.valueOf()) && date.toISOString().slice(0, 10) === value
        ? `${value.slice(8, 10)}.${value.slice(5, 7)}.${value.slice(2, 4)}` : 'Дата недоступна';
}
export function expenseViewMonth(value: string): string {
    if (!value)
        return 'Не указано';
    if (!/^\d{4}-(0[1-9]|1[0-2])(?:-\d{2})?$/u.test(value))
        return 'Период недоступен';
    return new Intl.DateTimeFormat('ru-RU', { month: 'long', year: 'numeric', timeZone: 'UTC' })
        .format(new Date(`${value.slice(0, 7)}-01T00:00:00Z`)).replace(/ г\.$/u, '');
}
