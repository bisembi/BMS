import { useWorkspaceRefreshRevision, useWorkspaceRefreshAction, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { Fragment, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { getPaymentCalendar, getPaymentCalendarExport, type PaymentCalendarGrouping, type PaymentCalendarOrganizationGroup, type PaymentCalendarPage as PaymentCalendarPageData, type PaymentCalendarProgress, type PaymentCalendarStatus } from '../api/paymentCalendar';
import { saveBlobDownload } from '../utils/saveBlobDownload';
import { expenseRequestStatusLabel } from '../expenseRequestStatus';
import '../styles/payment-calendar.css';
import { getPaymentCalendarGrid, getPaymentCalendarGridExport, paymentCalendarGridStatuses, type PaymentCalendarGridStatus, type PaymentCalendarGrid as CalendarGridData } from '../api/paymentCalendarGrid';
import { PaymentCalendarGrid } from '../components/PaymentCalendarGrid';
import { getGoodsCalendar458, getGoodsCalendarExport458, type GoodsCalendar458 } from '../api/goodsCalendar458';
const pageSize = 50;
const statusLabels: Record<PaymentCalendarGridStatus, string> = {
    IN_APPROVAL: expenseRequestStatusLabel('IN_APPROVAL'), RETURNED: expenseRequestStatusLabel('RETURNED'),
    AWAITING_NSI: expenseRequestStatusLabel('AWAITING_NSI'), APPROVED: expenseRequestStatusLabel('APPROVED'),
    TO_PAY: expenseRequestStatusLabel('TO_PAY'),
    PARTIALLY_PAID: expenseRequestStatusLabel('PARTIALLY_PAID'),
    PAID: expenseRequestStatusLabel('PAID'),
    CLOSED: expenseRequestStatusLabel('CLOSED'),
};
const progressLabels: Record<PaymentCalendarProgress, string> = {
    unpaid: 'Не оплачено',
    partiallyPaid: expenseRequestStatusLabel('PARTIALLY_PAID'),
    paid: expenseRequestStatusLabel('PAID'),
};
const statusOptions: readonly PaymentCalendarStatus[] = ['TO_PAY', 'PARTIALLY_PAID', 'PAID', 'CLOSED'];
const amountFormatter = new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
});
function formatAmount(amount: number): string {
    return `${amountFormatter.format(amount)} KZT`;
}
function formatSettlementPart(amount: number | undefined): string {
    return amount === undefined ? 'Нет данных' : formatAmount(amount);
}
function formatDate(isoDate: string): string {
    const [year, month, day] = isoDate.split('-');
    return `${day}.${month}.${year}`;
}
/// The first and last day of the current month, as local calendar components:
/// a treasurer opening the page means "this month", not a UTC slice of it.
function currentMonthPeriod(): {
    from: string;
    through: string;
} {
    const now = new Date();
    const year = now.getFullYear();
    const month = now.getMonth();
    const pad = (value: number) => String(value).padStart(2, '0');
    const lastDay = new Date(year, month + 1, 0).getDate();
    return {
        from: `${year}-${pad(month + 1)}-01`,
        through: `${year}-${pad(month + 1)}-${pad(lastDay)}`,
    };
}
type LoadState = {
    kind: 'loading';
} | {
    kind: 'invalid';
} | {
    kind: 'unavailable';
} | {
    kind: 'ready';
    page: PaymentCalendarPageData;
};
type Props = {
    onOpenRequest?: (requestId: string) => void;
    goodsOnly?: boolean;
};
/// Read-only payment calendar: what is due to be paid in a period and how much
/// of it the accounting circuit has already confirmed. There is deliberately
/// no way to record a payment here.
export function PaymentCalendarPage({ onOpenRequest, goodsOnly = false }: Props) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const initialPeriod = useMemo(currentMonthPeriod, []);
    const [from, setFrom] = useState(initialPeriod.from);
    const [through, setThrough] = useState(initialPeriod.through);
    const [status, setStatus] = useState<PaymentCalendarGridStatus | ''>('');
    const [grouping, setGrouping] = useState<PaymentCalendarGrouping>('date');
    const [calendarView, setCalendarView] = useState(goodsOnly);
    const [goods, setGoods] = useState<GoodsCalendar458 | null>(null);
    const [grid, setGrid] = useState<CalendarGridData | null>(null);
    const [gridCaptureKey, setGridCaptureKey] = useState<string | null>(null);
    const [gridState, setGridState] = useState<'loading' | 'ready' | 'failed'>('loading');
    const [skip, setSkip] = useState(0);
    const [state, setState] = useState<LoadState>({ kind: 'loading' });
    // Retrying must re-issue the same query, so it is a token the effect
    // depends on rather than a filter change.
    const [retryToken, setRetryToken] = useState(0);
    // Guards a stale response from overwriting a newer one when filters change
    // faster than the network answers.
    const requestSequence = useRef(0);
    const exportController = useRef<AbortController | null>(null);
    const exportSequence = useRef(0);
    const [exportState, setExportState] = useState<'idle' | 'running' | 'failed' | 'done'>('idle');
    const gridQueryKey = `${goodsOnly}|${from}|${through}|${status}|${retryToken}|${workspaceRefresh}`;
    const visibleGrid = gridCaptureKey === gridQueryKey ? grid : null;
    const cancelExport = useCallback(() => {
        exportSequence.current += 1;
        exportController.current?.abort();
        exportController.current = null;
        setExportState('idle');
    }, []);
    useWorkspaceRefreshAction(cancelExport);
    useWorkspaceRefreshBusy(calendarView ? gridState === 'loading' : state.kind === 'loading');
    useEffect(() => () => {
        exportSequence.current += 1;
        exportController.current?.abort();
    }, []);
    useEffect(() => {
        if (calendarView)
            return;
        if (from === '' || through === '' || from > through) {
            requestSequence.current += 1;
            setState({ kind: 'invalid' });
            return;
        }
        const controller = new AbortController();
        const sequence = requestSequence.current + 1;
        requestSequence.current = sequence;
        setState({ kind: 'loading' });
        getPaymentCalendar({ from, through, skip, take: pageSize, status: status === '' ? null : status as PaymentCalendarStatus,
            ...(grouping === 'organization' ? { grouping } : {}) }, controller.signal)
            .then((page) => {
            if (sequence === requestSequence.current) {
                setState({ kind: 'ready', page });
            }
        })
            .catch(() => {
            // The reason is dropped on purpose: no server failure text reaches the
            // DOM, and an outage must never be shown as an empty period.
            if (sequence === requestSequence.current) {
                setState({ kind: 'unavailable' });
            }
        });
        return () => controller.abort();
    }, [from, through, skip, status, grouping, retryToken, workspaceRefresh, calendarView]);
    useEffect(() => {
        if (!calendarView)
            return;
        setGrid(null);
        if (from === '' || through === '' || from > through) {
            setGridState('failed');
            return;
        }
        const controller = new AbortController();
        let current = true;
        setGridState('loading');
        const query = { from, through, status: status === '' ? null : status };
        const pending = goodsOnly ? getGoodsCalendar458(query, controller.signal)
            : getPaymentCalendarGrid(query, controller.signal).then(calendar => ({ calendar, categories: [] }));
        pending.then(result => {
            if (current) {
                setGrid(result.calendar);
                setGoods(goodsOnly ? result : null);
                setGridCaptureKey(gridQueryKey);
                setGridState('ready');
            }
        })
            .catch(() => {
            if (current)
                setGridState('failed');
        });
        return () => { current = false; controller.abort(); };
    }, [calendarView, from, through, status, retryToken, workspaceRefresh, gridQueryKey, goodsOnly]);
    const changePeriod = useCallback((next: {
        from?: string;
        through?: string;
    }) => {
        cancelExport();
        if (next.from !== undefined)
            setFrom(next.from);
        if (next.through !== undefined)
            setThrough(next.through);
        setSkip(0);
    }, [cancelExport]);
    const page = state.kind === 'ready' ? state.page : null;
    const organizationGroups = useMemo(() => {
        const result = new Map<string | null, {
            group: PaymentCalendarOrganizationGroup;
            visibleCount: number;
        }>();
        if (page?.grouping !== 'organization')
            return result;
        const counts = new Map<string | null, number>();
        for (const item of page.items)
            counts.set(item.organizationId, (counts.get(item.organizationId) ?? 0) + 1);
        for (const group of page.organizationGroups) {
            result.set(group.organizationId, { group, visibleCount: counts.get(group.organizationId) ?? 0 });
        }
        return result;
    }, [page]);
    const canPrevious = skip > 0;
    const canNext = page !== null && page.items.length > 0 && skip + page.items.length < page.total;
    const periodIsValid = from !== '' && through !== '' && from <= through;
    const exportCalendar = async () => {
        if (!periodIsValid || (calendarView ? visibleGrid === null : page === null) || exportController.current !== null)
            return;
        const controller = new AbortController();
        exportController.current = controller;
        const sequence = ++exportSequence.current;
        setExportState('running');
        try {
            const download = goodsOnly ? await getGoodsCalendarExport458({ from, through, status: status === '' ? null : status }, controller.signal)
                : calendarView ? await getPaymentCalendarGridExport({ from, through, status: status === '' ? null : status }, controller.signal)
                    : await getPaymentCalendarExport({ from, through, status: status === '' ? null : status as PaymentCalendarStatus,
                        ...(grouping === 'organization' ? { grouping } : {}) }, controller.signal);
            if (controller.signal.aborted || sequence !== exportSequence.current)
                return;
            saveBlobDownload(download.blob, download.fileName);
            setExportState('done');
        }
        catch {
            if (!controller.signal.aborted && sequence === exportSequence.current)
                setExportState('failed');
        }
        finally {
            if (sequence === exportSequence.current) {
                exportController.current = null;
                setExportState((current) => current === 'running' ? 'idle' : current);
            }
        }
    };
    return (<div className="payment-calendar-page">
      <h1>{goodsOnly ? 'Товарный календарь' : 'Платежный календарь'}</h1>

      <div className="payment-calendar-filters">
        <label htmlFor="payment-calendar-from">Период с
        <input id="payment-calendar-from" type="date" value={from} onChange={(event) => changePeriod({ from: event.target.value })}/>
        </label>
        <label htmlFor="payment-calendar-through">по
        <input id="payment-calendar-through" type="date" value={through} onChange={(event) => changePeriod({ through: event.target.value })}/>
        </label>
        <label htmlFor="payment-calendar-status">Статус
        <select id="payment-calendar-status" value={status} onChange={(event) => {
            cancelExport();
            setStatus(event.target.value as PaymentCalendarGridStatus | '');
            setSkip(0);
        }}>
          <option value="">Все</option>
          {(calendarView ? paymentCalendarGridStatuses : statusOptions).map((option) => (<option key={option} value={option}>{statusLabels[option]}</option>))}
        </select>
        </label>
        <label htmlFor="payment-calendar-grouping">Группировка
        <select id="payment-calendar-grouping" disabled={calendarView} title={calendarView ? 'В календарном виде суммы сгруппированы по датам.' : undefined} value={grouping} onChange={(event) => {
            cancelExport();
            setGrouping(event.target.value as PaymentCalendarGrouping);
            setSkip(0);
        }}>
          <option value="date">По датам</option>
          <option value="organization">По организациям</option>
        </select>
        </label>
      </div>

      <div className="payment-calendar-export-bar">
        {!goodsOnly && <button type="button" className="payment-calendar-export" aria-pressed={calendarView} onClick={() => {
                cancelExport();
                if (calendarView) {
                    setState({ kind: 'loading' });
                    if (status !== '' && !statusOptions.includes(status as PaymentCalendarStatus))
                        setStatus('');
                }
                setCalendarView(current => !current);
            }}>
          {calendarView ? 'В виде списка' : 'В виде календаря'}
        </button>}
        <button type="button" className="payment-calendar-export" disabled={!periodIsValid || (calendarView ? visibleGrid === null || gridState !== 'ready' : page === null) || exportState === 'running'} onClick={() => { void exportCalendar(); }}>
          Выгрузить в Excel
        </button>
        <span>{calendarView ? 'Все суммы календаря — за выбранный период, без ограничения страницей.'
            : 'Вся выборка по текущим фильтрам, не только открытая страница.'}</span>
      </div>
      {exportState === 'running' && <p className="payment-calendar-note" role="status">Формирование файла…</p>}
      {exportState === 'done' && <p className="payment-calendar-note" role="status">Файл платёжного календаря подготовлен.</p>}
      {exportState === 'failed' && (<p className="payment-calendar-note" role="alert">
          Не удалось сформировать файл платёжного календаря. Повторите выгрузку позже.
        </p>)}

      {!periodIsValid && (<p className="payment-calendar-note" role="status">
          {from === '' || through === ''
                ? 'Укажите начало и окончание периода.'
                : 'Начало периода не может быть позже его окончания.'}
        </p>)}
      {calendarView && periodIsValid && gridState === 'loading' && <p role="status" className="payment-calendar-note">Загрузка календарного вида…</p>}
      {calendarView && periodIsValid && gridState === 'failed' && <div className="payment-calendar-note" role="alert">
        Не удалось получить календарь. Это не означает отсутствие платежей. Попробуйте повторить или сократите период до одного года.
        {' '}<button type="button" onClick={() => setRetryToken(current => current + 1)}>Повторить календарь</button>
      </div>}
      {calendarView && visibleGrid !== null
            && <PaymentCalendarGrid key={gridQueryKey} calendar={visibleGrid} goods={goodsOnly ? goods ?? undefined : undefined} onOpenRequest={onOpenRequest}/>}
      {!calendarView && <>
      {state.kind === 'loading' && (<p className="payment-calendar-note" role="status">Загрузка платежного календаря…</p>)}
      {state.kind === 'unavailable' && (<>
          <p className="payment-calendar-note" role="status">
            Платежный календарь сейчас недоступен. Это не означает, что платежей в
            периоде нет.
          </p>
          <button type="button" className="payment-calendar-retry" onClick={() => setRetryToken((current) => current + 1)}>
            Повторить
          </button>
        </>)}
      {page !== null && page.total === 0 && (<p className="payment-calendar-note" role="status">
          В выбранном периоде заявок к оплате нет.
        </p>)}
      {page !== null && page.total > 0 && page.items.length === 0 && (<p className="payment-calendar-note" role="status">
          На этой странице заявок нет. Вернитесь на предыдущую страницу.
        </p>)}

      {page !== null && (page.total > 0 || page.items.length > 0 || canPrevious) && (<>
          <div className="payment-calendar-table-wrap">
            <table className="payment-calendar-table">
            <caption>Заявки к оплате за выбранный период</caption>
            <thead>
              <tr>
                <th scope="col">Плановая дата</th>
                <th scope="col">Контрагент</th>
                <th scope="col">Назначение</th>
                <th scope="col">Статус</th>
                <th scope="col">Сумма заявки</th>
                <th scope="col">Факт погашения</th>
                <th scope="col">Банк</th>
                <th scope="col">Зачёт</th>
                <th scope="col">Остаток</th>
                <th scope="col">Оплата</th>
              </tr>
            </thead>
            <tbody>
              {page.items.map((item, index) => {
                    const summary = index === 0 || item.organizationId !== page.items[index - 1].organizationId
                        ? organizationGroups.get(item.organizationId) : undefined;
                    return (<Fragment key={item.expenseRequestId}>
                {summary !== undefined && (<tr className="payment-calendar-organization">
                    <th scope="row" colSpan={4}>
                      {summary.group.organizationCode !== null
                                ? `${summary.group.organizationCode} — ${summary.group.organizationName}`
                                : summary.group.organizationId === null ? 'Не указана' : 'Не найдена в справочнике'}
                      <span className="payment-calendar-organization-detail">
                        {`На странице: ${summary.visibleCount} из ${summary.group.total} заявок. Итоги организации — за весь период.`}
                      </span>
                    </th>
                    <td>{formatAmount(summary.group.totals.obligationAmount)}</td>
                    <td>{formatAmount(summary.group.totals.paidAmount)}
                      {(summary.group.totals.approvedRoundingAmount ?? 0) > 0 && <small> Округление {summary.group.totals.approvedRoundingAmount!.toLocaleString('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ₸</small>}</td>
                    <td>{formatSettlementPart(summary.group.totals.bankPaidAmount)}</td>
                    <td>{formatSettlementPart(summary.group.totals.appliedOverpaymentAmount)}</td>
                    <td>{formatAmount(summary.group.totals.remainingAmount)}</td>
                    <td>{summary.group.totals.overpaidAmount > 0
                                ? `Переплата ${formatAmount(summary.group.totals.overpaidAmount)}` : '—'}</td>
                  </tr>)}
                <tr className={item.canOpen ? undefined : 'payment-calendar-row-inactive'}>
                  <td>
                    {/* The row keeps its table semantics; the affordance is a
                                real button, so a screen reader and the keyboard both
                                get it for free. A row the session may not open has no
                                control at all rather than a disabled-looking one. */}
                    {item.canOpen && onOpenRequest !== undefined ? (<button type="button" className="payment-calendar-open" onClick={() => onOpenRequest(item.expenseRequestId)}>
                        {formatDate(item.plannedPaymentDate)}
                      </button>) : (formatDate(item.plannedPaymentDate))}
                  </td>
                  <td>{item.counterpartyName ?? '—'}</td>
                  <td>{item.purpose ?? '—'}</td>
                  <td>{statusLabels[item.status]}</td>
                  <td>{formatAmount(item.obligationAmount)}</td>
                  <td>{formatAmount(item.paidAmount)}
                    {(item.approvedRoundingAmount ?? 0) > 0 && <small> Округление {item.approvedRoundingAmount!.toLocaleString('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ₸</small>}</td>
                  <td>{formatSettlementPart(item.bankPaidAmount)}</td>
                  <td>{formatSettlementPart(item.appliedOverpaymentAmount)}</td>
                  <td>{formatAmount(item.remainingAmount)}</td>
                  <td>{progressLabels[item.progress]}</td>
                </tr>
                </Fragment>);
                })}
            </tbody>
            <tfoot>
              <tr>
                <th scope="row" colSpan={4}>Итого за период</th>
                <td>{formatAmount(page.totals.obligationAmount)}</td>
                <td>{formatAmount(page.totals.paidAmount)}
                  {(page.totals.approvedRoundingAmount ?? 0) > 0 && <small> Округление {page.totals.approvedRoundingAmount!.toLocaleString('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ₸</small>}</td>
                <td>{formatSettlementPart(page.totals.bankPaidAmount)}</td>
                <td>{formatSettlementPart(page.totals.appliedOverpaymentAmount)}</td>
                <td>{formatAmount(page.totals.remainingAmount)}</td>
                <td>
                  {page.totals.overpaidAmount > 0
                    ? `Переплата ${formatAmount(page.totals.overpaidAmount)}`
                    : '—'}
                </td>
              </tr>
            </tfoot>
            </table>
          </div>

          <div className="payment-calendar-paging">
            {(canPrevious || canNext) && <button type="button" disabled={!canPrevious} onClick={() => setSkip((current) => Math.max(current - pageSize, 0))}>
              Назад
            </button>}
            <span>
              {page.items.length > 0
                    ? `Показано ${skip + 1}–${skip + page.items.length} из ${page.total}`
                    : `На странице 0 заявок, всего за период ${page.total}`}
            </span>
            {(canPrevious || canNext) && <button type="button" disabled={!canNext} onClick={() => setSkip((current) => current + pageSize)}>
              Вперед
            </button>}
          </div>
        </>)}
      </>}
    </div>);
}
