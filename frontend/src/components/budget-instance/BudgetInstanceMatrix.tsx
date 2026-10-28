import type { ReferenceDataItem } from '../../types';
import { useLayoutEffect, useRef, useState } from 'react';
import { BudgetAmountInput } from './BudgetAmountInput';
import { budgetCentsText, budgetMatrixAmount, budgetMatrixCellKey, budgetMatrixRows, previewBudgetMatrix, signedBudgetCents, MAX_BUDGET_MATRIX_MONTHS, MAX_BUDGET_MATRIX_CELLS, type BudgetDraftLine } from '../../pages/budgetInstanceMatrix';
import '../../styles/budget-instance-matrix.css';
const money = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 });
const monthLabel = new Intl.DateTimeFormat('ru-RU', { month: 'long', year: 'numeric', timeZone: 'UTC' });
const headingMonth = new Intl.DateTimeFormat('ru-RU', { month: 'short', year: 'numeric', timeZone: 'UTC' });
/** Display only: round exact integer cents, never the underlying value or draft. */
const wholeCentsText = (cents: number) => {
    const whole = (BigInt(Math.abs(cents)) + 50n) / 100n;
    return money.format(cents < 0 ? -whole : whole).replace(/\u00a0/gu, ' ');
};
const wholeDraftText = (raw: string) => {
    const cents = signedBudgetCents(raw);
    return cents === null ? raw : wholeCentsText(cents);
};
export function BudgetInstanceMatrix({ lines, items, months, cellDrafts, editable, onChange, onPaste }: {
    lines: readonly BudgetDraftLine[];
    items: readonly ReferenceDataItem[];
    months: readonly string[];
    cellDrafts: Readonly<Record<string, string | null>>;
    editable: boolean;
    onChange: (articleId: string, month: string, value: string) => void;
    onPaste?: (articleId: string, month: string, text: string) => void;
}) {
    const inputs = useRef(new Map<string, HTMLInputElement>());
    const topScroll = useRef<HTMLDivElement>(null);
    const scrollSpacer = useRef<HTMLDivElement>(null);
    const tableScroll = useRef<HTMLDivElement>(null);
    const tableRef = useRef<HTMLTableElement>(null);
    const synchronizedPositions = useRef(new WeakMap<HTMLDivElement, number>());
    const measureScroll = useRef<(() => void) | null>(null);
    const [hasHorizontalOverflow, setHasHorizontalOverflow] = useState(false);
    useLayoutEffect(() => {
        const top = topScroll.current, spacer = scrollSpacer.current, bottom = tableScroll.current, table = tableRef.current;
        if (!top || !spacer || !bottom || !table)
            return;
        let disposed = false, frame = 0;
        const measure = () => {
            if (disposed)
                return;
            // Cached hidden tabs have no viewport. Never reset their saved position.
            if (bottom.clientWidth === 0) {
                setHasHorizontalOverflow(false);
                return;
            }
            const width = bottom.scrollWidth;
            if (spacer.style.width !== `${width}px`)
                spacer.style.width = `${width}px`;
            const maximum = Math.max(0, width - bottom.clientWidth);
            setHasHorizontalOverflow(maximum > 0);
            const left = Math.min(maximum, Math.max(0, bottom.scrollLeft));
            if (bottom.scrollLeft !== left) {
                bottom.scrollLeft = left;
                synchronizedPositions.current.set(bottom, bottom.scrollLeft);
            }
            if (top.scrollLeft !== bottom.scrollLeft) {
                top.scrollLeft = bottom.scrollLeft;
                synchronizedPositions.current.set(top, top.scrollLeft);
            }
        };
        const schedule = () => {
            // Column-width is emitted before the universal resize adapter applies DOM widths.
            if (!disposed && !frame)
                frame = requestAnimationFrame(() => { frame = 0; measure(); });
        };
        const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure);
        observer?.observe(bottom);
        observer?.observe(table);
        table.addEventListener('bms:column-width', schedule);
        window.addEventListener('resize', measure);
        measureScroll.current = measure;
        measure();
        return () => {
            disposed = true;
            measureScroll.current = null;
            if (frame)
                cancelAnimationFrame(frame);
            observer?.disconnect();
            table.removeEventListener('bms:column-width', schedule);
            window.removeEventListener('resize', measure);
        };
    }, []);
    // React month-count changes and showing the bar must also synchronize after layout.
    useLayoutEffect(() => { measureScroll.current?.(); });
    const synchronize = (from: HTMLDivElement, to: HTMLDivElement | null) => {
        const expected = synchronizedPositions.current.get(from);
        synchronizedPositions.current.delete(from);
        // Ignore an echo from a programmatic sync. A newer native scroll on the
        // other viewport may already have advanced before this queued event fires.
        if (expected !== undefined && Math.abs(from.scrollLeft - expected) < 0.5)
            return;
        // A queued event from a display:none cached tab must not erase its position.
        if (to && from.clientWidth > 0 && to.clientWidth > 0 && to.scrollLeft !== from.scrollLeft) {
            to.scrollLeft = from.scrollLeft;
            synchronizedPositions.current.set(to, to.scrollLeft);
        }
    };
    const preview = previewBudgetMatrix(lines, cellDrafts);
    const rows = budgetMatrixRows(items, preview);
    if (months.length === 0 || months.length > MAX_BUDGET_MATRIX_MONTHS
        || (rows.length + 2) * (months.length + 2) > MAX_BUDGET_MATRIX_CELLS) {
        return <p role="status">Табличный вид слишком большой для безопасного отображения. Используйте строковый вид. Период и суммы бюджета не изменены.</p>;
    }
    const allIds = rows.filter(row => row.kind === 'article').flatMap(row => row.articleIds);
    const cellText = (cents: number | null, invalid: boolean) => invalid ? '—' : cents === null ? '' : wholeCentsText(cents);
    const inputText = (cents: number | null) => cents === null ? '' : budgetCentsText(cents);
    const numericCell = (cents: number | null, invalid: boolean, total = false) => <td className={`${total ? 'budget-matrix-total ' : ''}${cents !== null && cents < 0 ? 'budget-matrix-negative' : ''}`}>
      {cellText(cents, invalid)}
    </td>;
    return <div className="budget-matrix-scroll-shell">
    <div className="budget-matrix-top-scroll" ref={topScroll} hidden={!hasHorizontalOverflow} role="region" tabIndex={0} aria-label="Горизонтальная прокрутка таблицы бюджета сверху" onScroll={event => synchronize(event.currentTarget, tableScroll.current)}>
      <div className="budget-matrix-scroll-spacer" ref={scrollSpacer} aria-hidden="true"/>
    </div>
    <div className="budget-matrix-wrap" ref={tableScroll} tabIndex={0} aria-label="Табличный вид экземпляра бюджета" onScroll={event => synchronize(event.currentTarget, topScroll.current)}>
    <table className="budget-matrix" ref={tableRef} aria-label="Бюджет по статьям и месяцам" style={{ width: `${350 + (months.length + 1) * 120}px` }}>
      <caption className="visually-hidden">Экземпляр бюджета в формате БДДС</caption>
      <colgroup><col className="budget-matrix-name-column"/>{months.map(month => <col key={month}/>)}<col /></colgroup>
      <thead><tr><th scope="col">Статья</th>{months.map(month => <th scope="col" key={month}>
        {headingMonth.format(new Date(`${month}T00:00:00Z`)).replace(' г.', '')}
      </th>)}<th scope="col" className="budget-matrix-total">Итого, ₸</th></tr></thead>
      <tbody>{rows.map(row => {
            const total = budgetMatrixAmount(preview, row.articleIds, months);
            return <tr key={row.key} className={`budget-matrix-${row.kind}`}>
          <th scope="row" title={row.label}><span>{row.label}</span></th>
          {months.map(month => {
                    const value = budgetMatrixAmount(preview, row.articleIds, [month]);
                    if (row.kind !== 'article' || !editable)
                        return <td key={month} className={value.cents !== null && value.cents < 0 ? 'budget-matrix-negative' : ''}>
              {cellText(value.cents, value.invalid)}
            </td>;
                    const draft = cellDrafts[budgetMatrixCellKey(row.articleIds[0], month)];
                    const invalid = draft !== undefined && draft !== null ? signedBudgetCents(draft) === null : value.invalid;
                    return <td key={month} className={value.cents !== null && value.cents < 0 ? 'budget-matrix-negative' : ''}>
              <BudgetAmountInput aria-label={`${row.label}, ${monthLabel.format(new Date(`${month}T00:00:00Z`)).replace(' г.', '')}`} ref={element => {
                            const key = budgetMatrixCellKey(row.articleIds[0], month);
                            if (element)
                                inputs.current.set(key, element);
                            else
                                inputs.current.delete(key);
                        }} aria-invalid={invalid || undefined} inputMode="decimal" value={draft === undefined ? inputText(value.cents) : draft ?? ''} normalizeOnChange={false} unfocusedFormatter={wholeDraftText} onValueChange={value => onChange(row.articleIds[0], month, value)} onPaste={event => {
                            if (!onPaste)
                                return;
                            event.preventDefault();
                            onPaste(row.articleIds[0], month, event.clipboardData.getData('text/plain'));
                        }} onKeyDown={event => {
                            if (event.key !== 'Enter' || event.altKey || event.ctrlKey || event.metaKey || event.nativeEvent.isComposing)
                                return;
                            event.preventDefault();
                            const articleRows = rows.filter(candidate => candidate.kind === 'article');
                            const index = articleRows.findIndex(candidate => candidate.key === row.key);
                            const next = articleRows[index + (event.shiftKey ? -1 : 1)];
                            if (next)
                                inputs.current.get(budgetMatrixCellKey(next.articleIds[0], month))?.focus();
                        }}/>
            </td>;
                })}
          {numericCell(total.cents, total.invalid, true)}
        </tr>;
        })}</tbody>
      <tfoot><tr><th scope="row">Чистый денежный поток</th>{months.map(month => {
            const value = budgetMatrixAmount(preview, allIds, [month]);
            return <td key={month} className={value.cents !== null && value.cents < 0 ? 'budget-matrix-negative' : ''}>
          {cellText(value.cents, value.invalid)}
        </td>;
        })}{(() => {
            const total = budgetMatrixAmount(preview, allIds, months);
            return numericCell(total.cents, total.invalid, true);
        })()}</tr></tfoot>
    </table>
    </div>
  </div>;
}
