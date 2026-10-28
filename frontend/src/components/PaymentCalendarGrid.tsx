import { useId, useMemo, useState, type CSSProperties } from 'react';
import { sumGridAmounts, type GridAmounts, type PaymentCalendarGrid as Calendar } from '../api/paymentCalendarGrid';
import '../styles/payment-calendar-grid.css';
import { goodsLabels458, type GoodsCalendar458 } from '../api/goodsCalendar458';
const money = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 });
const dateLabel = (iso: string) => iso.split('-').reverse().join('.');
const zero = sumGridAmounts([]);
type DetailScope = {
    from: string;
    through: string;
    week: boolean;
    calendar: Calendar;
};
function Amount({ amounts, id, empty = false }: {
    amounts: GridAmounts;
    id?: string;
    empty?: boolean;
}) {
    const value = amounts.totalAmountKzt === null ? 'Нет курса' : empty ? '—' : money.format(amounts.totalAmountKzt);
    return <span id={id} className={`payment-grid-value${amounts.totalAmountKzt === null ? ' payment-grid-value-unavailable' : ''}${empty ? ' payment-grid-value-empty' : ''}`} style={{ '--payment-amount-length': Math.max(value.length, 7) } as CSSProperties}>{value}</span>;
}
export function PaymentCalendarGrid({ calendar, onOpenRequest, goods }: {
    calendar: Calendar;
    onOpenRequest?: (id: string) => void;
    goods?: GoodsCalendar458;
}) {
    const amountId = useId();
    const detailsId = useId();
    const [selection, setSelection] = useState<DetailScope | null>(null);
    // New filters/refresh must not retain a previous response's selected rows.
    const selected = selection?.calendar === calendar ? selection : null;
    const [detailsSkip, setDetailsSkip] = useState(0);
    const select = (from: string, through: string, week: boolean) => {
        setSelection(selected?.from === from && selected.through === through && selected.week === week
            ? null : { from, through, week, calendar });
        setDetailsSkip(0);
    };
    const byDay = useMemo(() => new Map(calendar.days.map(day => [day.date, day])), [calendar]);
    const goodsDays = useMemo(() => goods?.categories.map(c => ({ code: c.code,
        days: new Map(c.calendar.days.map(d => [d.date, d.amounts])) })), [goods]);
    const breakdown = (dates: string[]) => goodsDays && <span className="goods-grid-breakdown458">
    {goodsDays.map(c => {
            const amounts = sumGridAmounts(dates.map(date => c.days.get(date) ?? zero));
            return <span key={c.code}><span>{goodsLabels458[c.code]}</span><span>{amounts.totalAmountKzt === null ? 'Нет курса' : money.format(amounts.totalAmountKzt)}</span></span>;
        })}
  </span>;
    const weeks = useMemo(() => {
        const first = new Date(calendar.from + 'T00:00:00Z');
        first.setUTCDate(first.getUTCDate() - ((first.getUTCDay() + 6) % 7));
        const result: string[][] = [];
        const lastInstant = Date.parse(calendar.through + 'T00:00:00Z');
        while (first.getTime() <= lastInstant) {
            const week: string[] = [];
            for (let index = 0; index < 7; index++) {
                week.push(first.getUTCFullYear() <= 9999 ? first.toISOString().slice(0, 10) : '');
                first.setUTCDate(first.getUTCDate() + 1);
            }
            result.push(week);
        }
        return result;
    }, [calendar.from, calendar.through]);
    const rows = useMemo(() => selected ? calendar.entries.filter(entry => entry.businessDate >= selected.from
        && entry.businessDate <= selected.through).sort((left, right) => left.businessDate.localeCompare(right.businessDate)) : [], [calendar.entries, selected]);
    const selectedLabel = selected ? selected.week
        ? `${dateLabel(selected.from)} — ${dateLabel(selected.through)}` : dateLabel(selected.from) : '';
    const periodLabel = new Intl.DateTimeFormat('ru-RU', { month: 'long', year: 'numeric', timeZone: 'UTC' });
    return <section className={`payment-grid${goods ? ' goods-grid458' : ''}`} aria-label={goods ? 'Календарный вид товарных платежей' : 'Календарный вид платежей'}>
    <div className="payment-grid-heading"><h2>{calendar.from.slice(0, 7) === calendar.through.slice(0, 7)
            ? periodLabel.format(new Date(calendar.from + 'T12:00:00Z')) : `${dateLabel(calendar.from)} — ${dateLabel(calendar.through)}`}</h2>
      <span>Все суммы в ₸</span></div>
    <div className="payment-grid-scroll"><table className="payment-grid-table">
      <caption className="sr-only">Платежи по дням. Итог недели — по датам выбранного периода.</caption>
      <thead><tr>{['Понедельник', 'Вторник', 'Среда', 'Четверг', 'Пятница', 'Суббота', 'Воскресенье', 'Неделя'].map((day, index) => <th key={day} scope="col" className={index >= 5 && index < 7 ? 'payment-grid-weekend' : undefined}>{day}</th>)}</tr></thead>
      <tbody>{weeks.map(week => {
            // Drill-down and total share exactly the days inside the selected filter.
            const visible = week.filter(iso => byDay.has(iso));
            const from = visible[0], through = visible[visible.length - 1];
            return <tr key={week[0]}>{week.map((iso, index) => {
                    const day = byDay.get(iso);
                    return <td key={`${iso}-${index}`} className={!day ? 'payment-grid-outside' : index >= 5 ? 'payment-grid-weekend' : undefined}>
          {day ? <button type="button" className="payment-grid-tile payment-grid-trigger payment-grid-day" aria-label={`Платежи ${dateLabel(iso)}`} aria-describedby={`${amountId}-${iso}`} title={`${dateLabel(iso)} · ${day.amounts.totalAmountKzt === null ? 'Нет курса' : `${money.format(day.amounts.totalAmountKzt)} ₸`}`} aria-controls={detailsId} aria-expanded={selected?.week === false && selected.from === iso} onClick={() => select(iso, iso, false)}>
            <time dateTime={iso}>{dateLabel(iso).slice(0, 5)}</time>
            <Amount id={`${amountId}-${iso}`} amounts={day.amounts} empty={day.entryCount === 0}/>{breakdown([iso])}</button>
                            : <div className="payment-grid-tile"><time dateTime={iso || undefined}>{iso ? dateLabel(iso).slice(0, 5) : '—'}</time></div>}
        </td>;
                })}<td className="payment-grid-week-total"><button type="button" className="payment-grid-tile payment-grid-trigger payment-grid-week" aria-label={`Расшифровка недели ${dateLabel(from)} — ${dateLabel(through)}`} aria-controls={detailsId} aria-describedby={`${amountId}-week-${from}`} aria-expanded={selected?.week === true && selected.from === from && selected.through === through} onClick={() => select(from, through, true)}>
        <span className="payment-grid-week-range">{dateLabel(from).slice(0, 5)}–{dateLabel(through).slice(0, 5)}</span>
        <Amount id={`${amountId}-week-${from}`} amounts={sumGridAmounts(week.map(iso => byDay.get(iso)?.amounts ?? zero))}/>{breakdown(visible)}</button></td></tr>;
        })}</tbody>
    </table></div>
    {calendar.totals.unknownPlanCount > 0 && <p className="payment-calendar-note" role="alert">
      Для {calendar.totals.unknownPlanCount} плановых строк нет сохранённого курса. Их суммы не заменены нулём; полный итог не рассчитан.</p>}
    {selected && <section id={detailsId} className="payment-grid-details" aria-label={`Расшифровка ${selectedLabel}`}>
      <div className="payment-grid-heading"><h3>Платежи {selectedLabel}</h3><button type="button" onClick={() => setSelection(null)}>Закрыть</button></div>
      {rows.length === 0 ? <p>{selected.week ? 'В этот период' : 'В этот день'} платежей и плановых остатков нет.</p> : <table>
        <thead><tr>{selected.week && <th>Дата</th>}{goods && <th>Статья</th>}<th>Вид</th><th>Контрагент</th><th>В валюте</th><th>Сумма, ₸</th><th>Заявка</th></tr></thead>
        <tbody>{rows.slice(detailsSkip, detailsSkip + 50).map((row, index) => <tr key={`${row.expenseRequestId}-${detailsSkip + index}`}>
          {selected.week && <td>{dateLabel(row.businessDate)}</td>}
          {goods && <td>{row.goodsCategory && goodsLabels458[row.goodsCategory as keyof typeof goodsLabels458] || '—'}</td>}
          <td>{row.kind === 'PLAN' ? 'План' : row.kind === 'BANK_REVERSAL' ? 'Сторно' : 'Оплата'}</td>
          <td>{row.counterpartyName ?? '—'}</td><td>{row.amount.toLocaleString('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} {row.currencyCode}</td>
          <td>{row.amountKzt === null ? 'Нет курса' : row.amountKzt.toLocaleString('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</td>
          <td>{row.canOpen && row.expenseRequestId && onOpenRequest ? <button type="button" onClick={() => onOpenRequest(row.expenseRequestId!)}>Открыть</button> : row.source === 'ODDS' && row.expenseRequestId === null ? '—' : 'Недоступна'}</td>
        </tr>)}</tbody></table>}
      {rows.length > 50 && <nav className="payment-calendar-paging" aria-label={selected.week ? 'Страницы расшифровки периода' : 'Страницы расшифровки дня'}>
        <button type="button" disabled={detailsSkip === 0} onClick={() => setDetailsSkip(current => Math.max(0, current - 50))}>Назад</button>
        <span>{detailsSkip + 1}–{Math.min(detailsSkip + 50, rows.length)} из {rows.length}</span>
        <button type="button" disabled={detailsSkip + 50 >= rows.length} onClick={() => setDetailsSkip(current => current + 50)}>Далее</button>
      </nav>}
    </section>}
  </section>;
}
