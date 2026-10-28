import { useWorkspaceRefreshRevision, useWorkspaceRefreshAction, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import { exportDatedPaymentCalendar, getDatedPaymentCalendar, type DatedPaymentCalendarPage as CalendarPage, type DatedPaymentCalendarRequest, type DatedPaymentCalendarRow } from '../api/datedPaymentCalendar';
import { saveBlobDownload } from '../utils/saveBlobDownload';
import { expenseRequestStatusLabel } from '../expenseRequestStatus';
import '../styles/dated-payment-calendar.css';
type Filters = Omit<DatedPaymentCalendarRequest, 'skip' | 'take'>;
type LoadState = {
    kind: 'idle' | 'loading' | 'failed';
} | {
    kind: 'ready';
    page: CalendarPage;
};
type Props = {
    onOpenRequest?: (requestId: string) => void;
};
const statusLabels = {
    TO_PAY: expenseRequestStatusLabel('TO_PAY'), PARTIALLY_PAID: expenseRequestStatusLabel('PARTIALLY_PAID'), PAID: expenseRequestStatusLabel('PAID'), CLOSED: expenseRequestStatusLabel('CLOSED'),
} as const;
const kindLabels = {
    PLAN: 'План', BANK_PAYMENT: 'Оплата банка', BANK_REVERSAL: 'Сторно банка', APPLIED_ALLOCATION: 'Зачёт',
} as const;
const amountFormatter = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 });
const amount = (value: number | null) => value === null ? '—' : amountFormatter.format(value);
const dateLabel = (iso: string) => iso.split('-').reverse().join('.');
function initialFilters(): Filters {
    // An editable convenience, explicitly labelled as the browser month. Actual
    // movement dates and their time zone always come from the server report.
    const now = new Date();
    const year = now.getFullYear();
    const month = now.getMonth() + 1;
    const prefix = `${year}-${String(month).padStart(2, '0')}`;
    return { from: `${prefix}-01`, through: `${prefix}-${new Date(year, month, 0).getDate()}`,
        status: null, currency: null, grouping: 'date' };
}
function isDate(value: string): boolean {
    if (!/^(?!0000)\d{4}-\d{2}-\d{2}$/u.test(value))
        return false;
    const [year, month, day] = value.split('-').map(Number);
    const parsed = new Date(0);
    parsed.setUTCFullYear(year, month - 1, day);
    return parsed.getUTCFullYear() === year && parsed.getUTCMonth() === month - 1 && parsed.getUTCDate() === day;
}
function validFilters(filters: Filters): boolean {
    return isDate(filters.from) && isDate(filters.through) && filters.from <= filters.through
        && (filters.currency === null || /^[A-Z]{3}$/u.test(filters.currency));
}
function organizationLabel(org: Pick<DatedPaymentCalendarRow, 'organizationId' | 'organizationCode' | 'organizationName'>): string {
    if (org.organizationName !== null)
        return org.organizationCode === null
            ? org.organizationName : `${org.organizationCode} — ${org.organizationName}`;
    return org.organizationId === null ? 'Не указана' : 'Не найдена в справочнике';
}
function Totals({ currencies, kzt, suffix }: {
    currencies: CalendarPage['currencyTotals'];
    kzt: CalendarPage['kztTotals'];
    suffix: string;
}) {
    return (<div className="dated-calendar-totals">
      <div className="dated-calendar-table-wrap">
        <table>
          <caption>Итоги по валютам {suffix}</caption>
          <thead><tr>{['Валюта', 'План', 'Оплаты банка', 'Сторно банка', 'Зачёты', 'Все движения'].map(label => <th key={label} scope="col">{label}</th>)}</tr></thead>
          <tbody>{currencies.map(total => (<tr key={total.currencyCode}>
              <th scope="row">{total.currencyCode}</th>
              <td>{amount(total.planAmount)}</td><td>{amount(total.bankPaymentAmount)}</td>
              <td>{amount(total.bankReversalAmount)}</td><td>{amount(total.appliedAllocationAmount)}</td>
              <td>{amount(total.movementAmount)}</td>
            </tr>))}</tbody>
        </table>
      </div>
      <div className="dated-calendar-table-wrap">
        <table>
          <caption>Итоги в KZT {suffix}</caption>
          <thead><tr>{['План', 'Оплаты банка', 'Сторно банка', 'Зачёты', 'Все движения'].map(label => <th key={label} scope="col">{label}</th>)}</tr></thead>
          <tbody><tr><td>{amount(kzt.planAmountKzt)}</td><td>{amount(kzt.bankPaymentAmountKzt)}</td>
            <td>{amount(kzt.bankReversalAmountKzt)}</td><td>{amount(kzt.appliedAllocationAmountKzt)}</td>
            <td>{amount(kzt.movementAmountKzt)}</td></tr></tbody>
        </table>
      </div>
      {kzt.planKztUnknownCount > 0 && (<p className="dated-calendar-note">План в KZT не определён для {amountFormatter.format(kzt.planKztUnknownCount)} из {amountFormatter.format(kzt.planCount)} плановых строк.
          {' '}Полный итог плана в KZT не вычислен; «—» не означает ноль.</p>)}
    </div>);
}
/** A separate read-only dated ledger: signed movements are not capped by plan. */
export function DatedPaymentCalendarPage({ onOpenRequest }: Props) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [draft, setDraft] = useState<Filters>(initialFilters);
    const [query, setQuery] = useState<DatedPaymentCalendarRequest | null>(() => ({ ...draft, skip: 0, take: 50 }));
    const [state, setState] = useState<LoadState>({ kind: 'loading' });
    const [validationError, setValidationError] = useState(false);
    const [reload, setReload] = useState(0);
    const sequence = useRef(0);
    const readController = useRef<AbortController | null>(null);
    const exportController = useRef<AbortController | null>(null);
    const [exportState, setExportState] = useState<'idle' | 'running' | 'done' | 'failed'>('idle');
    const tableRegion = useRef<HTMLDivElement>(null);
    const focusAfterPaging = useRef(false);
    const cancelExport = useCallback(() => {
        exportController.current?.abort();
        exportController.current = null;
        setExportState('idle');
    }, []);
    useWorkspaceRefreshBusy(state.kind === 'loading');
    useEffect(() => {
        if (query === null)
            return;
        const controller = new AbortController();
        readController.current = controller;
        const requestId = ++sequence.current;
        setState({ kind: 'loading' });
        void getDatedPaymentCalendar(query, controller.signal).then(page => {
            if (!controller.signal.aborted && requestId === sequence.current)
                setState({ kind: 'ready', page });
        }).catch(() => {
            // Never display a transport/provider error as text or an empty report.
            if (!controller.signal.aborted && requestId === sequence.current)
                setState({ kind: 'failed' });
        });
        return () => controller.abort();
    }, [query, reload, workspaceRefresh]);
    useWorkspaceRefreshAction(cancelExport);
    useEffect(() => () => {
        sequence.current += 1;
        readController.current?.abort();
        exportController.current?.abort();
    }, []);
    useEffect(() => {
        if (state.kind === 'ready' && focusAfterPaging.current) {
            focusAfterPaging.current = false;
            tableRegion.current?.focus();
        }
    }, [state]);
    const changeDraft = (patch: Partial<Filters>) => {
        sequence.current += 1;
        readController.current?.abort();
        cancelExport();
        focusAfterPaging.current = false;
        setDraft(current => ({ ...current, ...patch }));
        setQuery(null);
        setState({ kind: 'idle' });
        setValidationError(false);
    };
    const applyFilters = () => {
        if (!validFilters(draft)) {
            setValidationError(true);
            return;
        }
        sequence.current += 1;
        readController.current?.abort();
        cancelExport();
        setValidationError(false);
        setQuery({ ...draft, skip: 0, take: 50 });
        setState({ kind: 'loading' });
    };
    const movePage = (skip: number) => {
        if (query === null)
            return;
        sequence.current += 1;
        readController.current?.abort();
        cancelExport();
        focusAfterPaging.current = true;
        setState({ kind: 'loading' });
        setQuery({ ...query, skip });
    };
    const page = state.kind === 'ready' ? state.page : null;
    const download = async () => {
        if (query === null || page === null || exportController.current !== null)
            return;
        const controller = new AbortController();
        exportController.current = controller;
        setExportState('running');
        const { from, through, status, currency, grouping } = query;
        try {
            const file = await exportDatedPaymentCalendar({ from, through, status, currency, grouping }, controller.signal);
            if (controller.signal.aborted || exportController.current !== controller)
                return;
            saveBlobDownload(file.blob, file.fileName);
            setExportState('done');
        }
        catch {
            if (!controller.signal.aborted && exportController.current === controller)
                setExportState('failed');
        }
        finally {
            if (exportController.current === controller)
                exportController.current = null;
        }
    };
    return (<section className="dated-calendar-page" aria-labelledby="dated-calendar-heading" aria-busy={state.kind === 'loading'}>
      <h1 id="dated-calendar-heading">Календарь по датам</h1>
      <p>План и все фактические движения: банк, зачёты и сторно на дату операции. Движения не ограничиваются суммой заявки.
        {' '}Итоги разных валют не складываются между собой.</p>
      <form className="dated-calendar-filters" onSubmit={event => { event.preventDefault(); applyFilters(); }}>
        <label htmlFor="dated-from">С даты
        <input id="dated-from" type="date" min="0001-01-01" max="9999-12-31" required value={draft.from} onChange={event => changeDraft({ from: event.target.value })}/>
        </label>
        <label htmlFor="dated-through">По дату
        <input id="dated-through" type="date" min="0001-01-01" max="9999-12-31" required value={draft.through} onChange={event => changeDraft({ through: event.target.value })}/>
        </label>
        <label htmlFor="dated-status">Статус заявки
        <select id="dated-status" value={draft.status ?? ''} onChange={event => changeDraft({ status: event.target.value === '' ? null : event.target.value as Filters['status'] })}>
          <option value="">Все</option>
          {(Object.keys(statusLabels) as Array<keyof typeof statusLabels>).map(status => <option key={status} value={status}>{statusLabels[status]}</option>)}
        </select>
        </label>
        <label htmlFor="dated-currency">Валюта
        <input id="dated-currency" value={draft.currency ?? ''} placeholder="Все валюты" maxLength={3} pattern="[A-Z]{3}" aria-describedby="dated-filter-help" onChange={event => changeDraft({ currency: event.target.value.toUpperCase() || null })}/>
        </label>
        <label htmlFor="dated-grouping">Группировка
        <select id="dated-grouping" value={draft.grouping} onChange={event => changeDraft({ grouping: event.target.value as Filters['grouping'] })}>
          <option value="date">По датам</option><option value="organization">По организациям</option>
        </select>
        </label>
        <button type="submit">Применить</button>
      </form>
      <p id="dated-filter-help" className="dated-calendar-help">Начальный период — текущий месяц браузера. Валюта: трёхбуквенный код или пустое поле для всех валют.</p>
      {validationError && <p role="alert">Укажите корректный период и трёхбуквенный код валюты.</p>}
      {state.kind === 'idle' && <p role="status">Фильтры изменены. Нажмите «Применить».</p>}
      {state.kind === 'loading' && <p role="status">Загрузка календаря…</p>}
      {state.kind === 'failed' && <div role="alert"><p>Не удалось загрузить календарь по датам. Повторите запрос позже.</p>
        <button type="button" onClick={() => { setState({ kind: 'loading' }); setReload(value => value + 1); }}>Повторить</button></div>}
      <div className="dated-calendar-export">
        <button type="button" disabled={page === null || exportState === 'running'} onClick={() => { void download(); }}>Выгрузить в Excel</button>
        <span>Все строки выбранного периода и фильтров, не только текущая страница.</span>
      </div>
      {exportState === 'running' && <p role="status">Формирование файла…</p>}
      {exportState === 'done' && <p role="status">Файл календаря подготовлен.</p>}
      {exportState === 'failed' && <p role="alert">Не удалось сформировать файл календаря. Повторите выгрузку позже.</p>}
      {page !== null && query !== null && <>
        <p className="dated-calendar-period">Период: {dateLabel(query.from)} — {dateLabel(query.through)}.
          {' '}Часовой пояс операций: {page.businessTimeZoneId}. Суммы на экране округлены до целых; Excel содержит исходные значения.</p>
        <Totals currencies={page.currencyTotals} kzt={page.kztTotals} suffix="за весь период"/>
        {page.grouping === 'organization' && page.organizationGroups.map((group, index) => (<section key={group.organizationId ?? 'absent'} className="dated-calendar-group" aria-label={`Организация: ${organizationLabel(group)}`}>
            <h3>Организация: {organizationLabel(group)}</h3>
            <p>На странице: {page.items.filter(row => row.organizationId === group.organizationId).length} из {group.total} строк организации.
              {' '}Итоги организации — за весь период.</p>
            <Totals currencies={group.currencyTotals} kzt={group.kztTotals} suffix={`организации ${index + 1} за весь период`}/>
          </section>))}
        <div className="dated-calendar-table-wrap" ref={tableRegion} tabIndex={-1} role="region" aria-label="Строки календаря">
          <table className="dated-calendar-rows">
            <caption>План и фактические движения по датам</caption>
            <thead><tr>{['Дата', 'Операция', 'Организация', 'Контрагент', 'Назначение', 'Статус', 'Валюта', 'План', 'План в KZT', 'Движение', 'Движение в KZT', 'Заявка']
                .map(label => <th scope="col" key={label}>{label}</th>)}</tr></thead>
            <tbody>{page.items.length === 0 ? <tr><td colSpan={12}>{page.total === 0
                    ? 'За выбранный период нет строк плана и движений.' : 'На этой странице нет строк. Вернитесь на предыдущую страницу.'}</td></tr>
                : page.items.map((row, index) => (
                // Public contract intentionally has no movement ID. Never dedupe
                // repeated equal movements or use request ID alone as a row key.
                <tr key={`${page.skip + index}`}>
                  <td>{dateLabel(row.businessDate)}</td><td>{kindLabels[row.kind]}</td><td>{organizationLabel(row)}</td>
                  <td>{row.counterpartyName ?? '—'}</td><td className="dated-calendar-purpose">{row.purpose ?? '—'}</td>
                  <td>{statusLabels[row.status]}</td><td>{row.currencyCode}</td>
                  <td className="dated-calendar-money">{amount(row.planAmount)}</td><td className="dated-calendar-money">{amount(row.planAmountKzt)}</td>
                  <td className="dated-calendar-money">{amount(row.signedAmount)}</td><td className="dated-calendar-money">{amount(row.signedAmountKzt)}</td>
                  <td>{row.canOpen && onOpenRequest ? <button type="button" className="dated-calendar-open" onClick={() => onOpenRequest(row.expenseRequestId)}>Открыть заявку</button> : '—'}</td>
                </tr>))}</tbody>
          </table>
        </div>
        {(page.total > page.take || page.skip > 0) && <nav className="dated-calendar-paging" aria-label="Страницы календаря">
          <button type="button" disabled={page.skip === 0} onClick={() => movePage(Math.max(0, page.skip - page.take))}>Предыдущая страница</button>
          <span>{page.items.length === 0 ? '0' : `${page.skip + 1}–${page.skip + page.items.length}`} из {amountFormatter.format(page.total)} строк</span>
          <button type="button" disabled={page.skip + page.items.length >= page.total || page.items.length === 0} onClick={() => movePage(page.skip + page.take)}>Следующая страница</button>
        </nav>}
      </>}
    </section>);
}
