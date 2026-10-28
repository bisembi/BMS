import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react';
import type { BudgetLineSort, BudgetLineSortColumn } from '../../pages/budgetLineSort';
import '../../styles/budget-line-sort.css';
const columns = [
    { title: 'Операция', width: 400, min: 120, max: 2000 },
    { title: 'Статья ДДС', width: 400, min: 120, max: 2000 },
    { title: 'Направление', width: 140, min: 140, max: 400 },
    { title: 'Месяц', width: 100, min: 100, max: 300 },
    { title: 'Сумма, ₸', width: 120, min: 80, max: 500 },
    { title: 'Действия', width: 58, min: 58, max: 300 },
] as const;
/** View-only state: no document, dirty marker, storage or API writes. */
export function useBudgetLineColumnWidths() {
    const [widths, setWidths] = useState<number[]>(() => columns.map(column => column.width));
    const changeWidth = useCallback((index: number, value: number) => {
        const column = columns[index];
        if (!column || !Number.isFinite(value))
            return;
        const width = Math.max(column.min, Math.min(column.max, Math.round(value)));
        setWidths(previous => previous[index] === width ? previous : previous.map((old, at) => at === index ? width : old));
    }, []);
    return { widths, changeWidth, totalWidth: widths.reduce((total, width) => total + width, 0) };
}
function ColumnHandle({ column, width, onChange }: {
    column: typeof columns[number];
    width: number;
    onChange: (width: number) => void;
}) {
    const drag = useRef<{
        pointerId: number;
        startX: number;
        startWidth: number;
    } | null>(null);
    function release(target: HTMLElement, pointerId: number) {
        if (target.hasPointerCapture?.(pointerId))
            target.releasePointerCapture(pointerId);
    }
    function start(event: PointerEvent<HTMLSpanElement>) {
        if (event.button !== 0 || event.isPrimary === false || drag.current || !Number.isFinite(event.clientX))
            return;
        event.preventDefault();
        event.stopPropagation();
        event.currentTarget.focus({ preventScroll: true });
        event.currentTarget.setPointerCapture?.(event.pointerId);
        drag.current = { pointerId: event.pointerId, startX: event.clientX, startWidth: width };
    }
    function move(event: PointerEvent<HTMLSpanElement>) {
        const active = drag.current;
        if (!active || active.pointerId !== event.pointerId || !Number.isFinite(event.clientX))
            return;
        event.preventDefault();
        event.stopPropagation();
        onChange(active.startWidth + event.clientX - active.startX);
    }
    function finish(event: PointerEvent<HTMLSpanElement>, cancelled = false) {
        const active = drag.current;
        if (!active || active.pointerId !== event.pointerId)
            return;
        drag.current = null;
        if (cancelled)
            onChange(active.startWidth);
        release(event.currentTarget, active.pointerId);
    }
    function keyboard(event: KeyboardEvent<HTMLSpanElement>) {
        if (event.key === 'Escape' && drag.current) {
            const active = drag.current;
            drag.current = null;
            onChange(active.startWidth);
            release(event.currentTarget, active.pointerId);
        }
        else if (event.key === 'ArrowLeft')
            onChange(width - (event.shiftKey ? 50 : 10));
        else if (event.key === 'ArrowRight')
            onChange(width + (event.shiftKey ? 50 : 10));
        else if (event.key === 'Home')
            onChange(column.min);
        else if (event.key === 'End')
            onChange(column.max);
        else
            return;
        event.preventDefault();
        event.stopPropagation();
    }
    return <span className="budget-instance-column-resizer" role="separator" tabIndex={0} aria-label={`Ширина столбца «${column.title}»`} aria-orientation="vertical" aria-valuemin={column.min} aria-valuemax={column.max} aria-valuenow={width} aria-valuetext={`${width} px`} title="Перетащите границу; ←/→ — изменить ширину; двойной щелчок — исходная ширина" onPointerDown={start} onPointerMove={move} onPointerUp={event => finish(event)} onPointerCancel={event => finish(event, true)} onLostPointerCapture={event => {
            if (drag.current?.pointerId === event.pointerId)
                drag.current = null;
        }} onKeyDown={keyboard} onDoubleClick={event => { event.preventDefault(); onChange(column.width); }}/>;
}
export function BudgetLineColumnsHead({ widths, changeWidth, sort = null, onSort, sortDisabled = false }: {
    widths: readonly number[];
    changeWidth: (index: number, width: number) => void;
    sort?: BudgetLineSort | null;
    onSort?: (column: BudgetLineSortColumn) => void;
    sortDisabled?: boolean;
}) {
    const head = useRef<HTMLTableSectionElement>(null);
    useEffect(() => {
        const table = head.current?.closest('table');
        if (!table)
            return;
        const listener = (event: Event) => {
            const detail = (event as CustomEvent<{
                index: number;
                width: number;
            }>).detail;
            if (!detail)
                return;
            const { index, width } = detail;
            if (Number.isInteger(index) && Number.isFinite(width))
                changeWidth(index, width);
        };
        table.addEventListener('bms:column-width', listener);
        return () => table.removeEventListener('bms:column-width', listener);
    }, [changeWidth]);
    return <thead ref={head}><tr>{columns.map((column, index) => <th key={column.title} scope="col" aria-label={column.title} aria-sort={index < 5 && onSort ? (sort?.column === index ? sort.direction : 'none') : undefined} data-column-min={column.min} data-column-max={column.max} data-column-default={column.width} className={index === 5 ? 'budget-instance-line-actions' : undefined}>
    {index < 5 && (onSort ? <button type="button" className="budget-line-sort-button" disabled={sortDisabled} aria-label={`Сортировать: ${column.title}`} title={sort?.column !== index ? 'По возрастанию' : sort.direction === 'ascending' ? 'По убыванию' : 'Исходный порядок'} onClick={() => onSort(index as BudgetLineSortColumn)}>
      {column.title}<span aria-hidden="true" className="budget-line-sort-symbol">
        {sort?.column !== index ? '↕' : sort.direction === 'ascending' ? '▲' : '▼'}
      </span>
    </button> : column.title)}
    <ColumnHandle column={column} width={widths[index]} onChange={width => changeWidth(index, width)}/>
  </th>)}</tr></thead>;
}
