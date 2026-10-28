import { Fragment, useEffect, useMemo, useState } from 'react';
import { getBudgetExecutionExport, type BudgetPositionKey } from '../api';
import { expenseRequestStatusLabel } from '../expenseRequestStatus';
import { formatBudgetMonth } from '../utils/expenseRequestDraft';
import { toBusinessDateInput } from '../utils/businessDate';
import { useBudgetExecutionData } from '../hooks/useBudgetExecutionData';
import { useBudgetReservation } from '../hooks/useBudgetReservation';
import type { BudgetPosition } from '../types';
import { budgetPageNumbers } from '../utils/budgetPageNumbers';
import { excelExportFailureMessage } from '../api/excelExportFailure';
import { formatBusinessTimestamp } from '../utils/businessTime';
import { executionCaptureDelayed } from '../utils/executionCaptureFreshness386';
import '../styles/budget-execution.css';
// Rendering only: keep API amounts and all financial predicates at their original precision.
const wholeMoneyFormat = new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 0, maximumFractionDigits: 0, useGrouping: true,
});
function formatWholeMoney(value: number): string {
    return wholeMoneyFormat.format(Math.abs(value) < 0.5 ? 0 : value);
}
const pageSizes = [25, 50, 100] as const;
const reservationPageSize = 25;
/// One sentence whatever went wrong: the reason a workbook could not be built
/// belongs in the server log, not on screen.
const exportErrorMessage = 'Не удалось сформировать файл выгрузки.';
function keyOf(position: BudgetPositionKey): string {
    return `${position.costCenterId}:${position.cashFlowItemId}:${position.budgetMonth}`;
}
function labelFor(items: {
    id: string;
    name: string;
}[], id: string, fallback: string): string {
    return items.find((item) => item.id.toLowerCase() === id.toLowerCase())?.name ?? fallback;
}
export function BudgetExecutionPage() {
    const [structure, setStructure] = useState<'current' | 'approved'>('current');
    const [month, setMonth] = useState(() => toBusinessDateInput().slice(0, 7));
    const [costCenterIndex, setCostCenterIndex] = useState('');
    const [cashFlowItemIndex, setCashFlowItemIndex] = useState('');
    const [costCenterId, setCostCenterId] = useState<string | undefined>();
    const [cashFlowItemId, setCashFlowItemId] = useState<string | undefined>();
    const [appliedFilters, setAppliedFilters] = useState(() => ({ structure: 'current' as 'current' | 'approved',
        costCenterId: undefined as string | undefined, cashFlowItemId: undefined as string | undefined,
        budgetMonth: `${month}-01` as string | undefined }));
    const [costCenterLabel, setCostCenterLabel] = useState('Выбранный ЦФО');
    const [cashFlowItemLabel, setCashFlowItemLabel] = useState('Выбранная статья ДДС');
    const [pageNumber, setPageNumber] = useState(0);
    const [pageSize, setPageSize] = useState<number>(50);
    const [reloadToken, setReloadToken] = useState(0);
    const [exporting, setExporting] = useState(false);
    const [exportError, setExportError] = useState<string | null>(null);
    const [openPosition, setOpenPosition] = useState<BudgetPositionKey | null>(null);
    const [reservationSkip, setReservationSkip] = useState(0);
    const [reservationToken, setReservationToken] = useState(0);
    const reservation = useBudgetReservation(openPosition, reservationSkip, reservationPageSize, reservationToken);
    const data = useBudgetExecutionData({
        ...appliedFilters,
        skip: pageNumber * pageSize,
        take: pageSize,
        reloadToken,
    });
    useEffect(() => {
        if (costCenterIndex === '')
            return;
        const resolvedIndex = costCenterId === undefined
            ? -1
            : data.costCenters.findIndex((item) => item.id.toLowerCase() === costCenterId.toLowerCase());
        if (resolvedIndex < 0) {
            // Catalogue failure/removal does not mean the user selected "All".
            setCostCenterIndex('missing');
        }
        else if (String(resolvedIndex) !== costCenterIndex) {
            setCostCenterIndex(String(resolvedIndex));
        }
    }, [costCenterId, costCenterIndex, data.costCenters]);
    useEffect(() => {
        if (cashFlowItemIndex === '')
            return;
        const resolvedIndex = cashFlowItemId === undefined
            ? -1
            : data.cashFlowItems.findIndex((item) => item.id.toLowerCase() === cashFlowItemId.toLowerCase());
        if (resolvedIndex < 0) {
            setCashFlowItemIndex('missing');
        }
        else if (String(resolvedIndex) !== cashFlowItemIndex) {
            setCashFlowItemIndex(String(resolvedIndex));
        }
    }, [cashFlowItemId, cashFlowItemIndex, data.cashFlowItems]);
    const totalPages = data.page.total === 0 ? 0 : Math.ceil(data.page.total / pageSize);
    useEffect(() => {
        if (data.loading || data.error !== null
            || data.page.skip !== pageNumber * pageSize || data.page.take !== pageSize)
            return;
        const lastPage = Math.max(totalPages - 1, 0);
        if (pageNumber > lastPage)
            setPageNumber(lastPage);
    }, [data.loading, data.error, data.page.skip, data.page.take, pageNumber, pageSize, totalPages]);
    const rows = useMemo(() => data.page.items, [data.page.items]);
    const openKey = openPosition === null ? null : keyOf(openPosition);
    // A composition belongs to one position. As soon as that position is no
    // longer among the rows on screen - a filter changed, a page turned, the
    // list reloaded - the panel is closed rather than left describing something
    // the user can no longer see.
    useEffect(() => {
        if (openKey === null)
            return;
        if (!rows.some((item) => keyOf(item) === openKey || item.planCostCenters?.some(source => keyOf({ ...item, costCenterId: source.costCenterId }) === openKey))) {
            setOpenPosition(null);
            setReservationSkip(0);
        }
    }, [openKey, rows]);
    useEffect(() => {
        const page = reservation.page;
        if (openKey === null || reservation.loading || page === null || page.skip !== reservationSkip)
            return;
        // A concurrent release can remove the page being requested. Return to the
        // last existing page instead of leaving an empty table with no way back.
        const lastSkip = page.total === 0
            ? 0
            : Math.floor((page.total - 1) / reservationPageSize) * reservationPageSize;
        if (reservationSkip > lastSkip)
            setReservationSkip(lastSkip);
    }, [openKey, reservation.loading, reservation.page, reservationSkip]);
    const changeFilter = (change: () => void) => {
        change();
    };
    const closeReservation = (returnFocus: boolean) => {
        const previous = openKey;
        setOpenPosition(null);
        setReservationSkip(0);
        if (!returnFocus || previous === null)
            return;
        // The button that opened the panel is where the user was.
        const index = rows.findIndex((item) => keyOf(item) === previous || item.planCostCenters?.some(source => keyOf({ ...item, costCenterId: source.costCenterId }) === previous));
        if (index >= 0) {
            document.getElementById(`budget-reservation-toggle-${index}`)?.focus();
        }
    };
    const toggleReservation = (position: BudgetPosition) => {
        const next = keyOf(position);
        if (openKey === next) {
            closeReservation(true);
            return;
        }
        setOpenPosition({
            costCenterId: position.costCenterId,
            cashFlowItemId: position.cashFlowItemId,
            budgetMonth: position.budgetMonth,
            ...(data.page.factSource === undefined ? {} : { expectedFactSnapshotId: data.page.factSource.snapshotId }),
        });
        setReservationSkip(0);
    };
    const reload = () => {
        // Even when the same key returns, its current reserve may have changed.
        // Do not display an old composition beside a freshly loaded amount.
        closeReservation(false);
        setReloadToken((current) => current + 1);
    };
    const totals = data.page.totals;
    /// The file covers the whole filtered area, so it is asked for with the
    /// filters and without the page. A second click while one is being built is
    /// refused by the button itself.
    const exportExcel = async () => {
        if (exporting)
            return;
        setExporting(true);
        setExportError(null);
        try {
            const download = await getBudgetExecutionExport({
                ...appliedFilters,
                ...(data.page.factSource === undefined ? {} : { expectedFactSnapshotId: data.page.factSource.snapshotId }),
            });
            const url = URL.createObjectURL(download.blob);
            try {
                const anchor = document.createElement('a');
                anchor.href = url;
                anchor.download = download.fileName;
                anchor.click();
            }
            finally {
                URL.revokeObjectURL(url);
            }
        }
        catch (error) {
            setExportError(excelExportFailureMessage(error, exportErrorMessage));
        }
        finally {
            setExporting(false);
        }
    };
    /// The panel under one position. It shows the current reserve only: the paid
    /// part of a position is never attributed to individual requests, because a
    /// payment is recorded against a request and not against a budget key.
    const renderReservation = () => {
        if (reservation.loading) {
            return <p role="status">Загружаем состав резерва…</p>;
        }
        if (reservation.error !== null) {
            return (<div role="alert" className="budget-reservation-error">
          <p>{reservation.error}</p>
          <button type="button" onClick={() => setReservationToken((current) => current + 1)}>
            Повторить
          </button>
        </div>);
        }
        if (reservation.missing || reservation.page === null) {
            return <p role="status">Бюджетная позиция не найдена. Обновите список.</p>;
        }
        const page = reservation.page;
        const pages = page.total === 0 ? 0 : Math.ceil(page.total / reservationPageSize);
        const current = Math.floor(page.skip / reservationPageSize) + 1;
        return (<>
        <div className="budget-reservation-head">
          <h3>Состав резерва</h3>
          <button type="button" onClick={() => closeReservation(true)}>Свернуть</button>
        </div>
        <p className="budget-reservation-summary">
          Всего зарезервировано:{' '}
          <span className="budget-execution-money">{formatWholeMoney(page.totalReservedAmountKzt)} ₸</span>
        </p>
        <p className="budget-reservation-summary">
          Заявок: {page.total} · строк заявок: {page.totalLineCount}
          {(page.visibleReservedAmountKzt ?? page.totalReservedAmountKzt) !== page.totalReservedAmountKzt && <>
            {' '}· по доступным заявкам: <span className="budget-execution-money">{formatWholeMoney(page.visibleReservedAmountKzt ?? page.totalReservedAmountKzt)} ₸</span>
          </>}
        </p>
        {page.total === 0
                ? <p role="status">{page.totalReservedAmountKzt > 0
                        ? 'Нет доступной детализации резерва.' : 'Ни одна заявка не удерживает этот резерв.'}</p>
                : (<>
              <table>
                <caption>Заявки, формирующие текущий резерв</caption>
                <thead>
                  <tr>
                    <th scope="col">Статус</th>
                    <th scope="col">Назначение</th>
                    <th scope="col">Плановая оплата</th>
                    <th scope="col">Строк</th>
                    <th scope="col" className="budget-execution-money">Зарезервировано, ₸</th>
                  </tr>
                </thead>
                <tbody>
                  {page.items.map((participant, index) => (<tr key={`${page.skip + index}`}>
                      <td>{expenseRequestStatusLabel(participant.status)}</td>
                      <td>{participant.purpose ?? 'Назначение не указано'}</td>
                      <td>{participant.plannedPaymentDate}</td>
                      <td>{participant.lineCount}</td>
                      <td className="budget-execution-money">{formatWholeMoney(participant.reservedAmountKzt)}</td>
                    </tr>))}
                </tbody>
              </table>
              {pages > 1 && (<nav aria-label="Страницы состава резерва" className="budget-execution-pagination">
                  <button type="button" disabled={page.skip === 0} onClick={() => setReservationSkip((value) => Math.max(value - reservationPageSize, 0))}>
                    Назад
                  </button>
                  <span aria-live="polite">Страница {current} из {pages}</span>
                  <button type="button" disabled={current >= pages} onClick={() => setReservationSkip((value) => value + reservationPageSize)}>
                    Вперёд
                  </button>
                </nav>)}
            </>)}
      </>);
    };
    return (<div className="budget-execution-page">
      <h1>Исполнение бюджета</h1>
      <div className="budget-execution-filters" aria-label="Фильтры исполнения бюджета">
        <label>
          Структура отчёта
          <select value={structure} onChange={event => changeFilter(() => setStructure(event.target.value as 'current' | 'approved'))}>
            <option value="current">По текущей структуре</option>
            <option value="approved">Как утверждено</option>
          </select>
        </label>
        <label>
          Месяц
          <input type="month" value={month} onChange={(event) => changeFilter(() => setMonth(event.target.value))}/>
        </label>
        <label>
          ЦФО
          <select value={costCenterIndex} onChange={(event) => changeFilter(() => {
            const nextIndex = event.target.value;
            if (nextIndex === 'missing')
                return;
            setCostCenterIndex(nextIndex);
            setCostCenterId(nextIndex === '' ? undefined : data.costCenters[Number(nextIndex)]?.id);
            if (nextIndex !== '')
                setCostCenterLabel(data.costCenters[Number(nextIndex)]?.name ?? 'Выбранный ЦФО');
        })}>
            <option value="">Все ЦФО</option>
            {costCenterIndex === 'missing' && <option value="missing">{costCenterLabel}</option>}
            {data.costCenters.map((item, index) => (<option value={index} key={item.id}>{item.name}</option>))}
          </select>
        </label>
        <label>
          Статья ДДС
          <select value={cashFlowItemIndex} onChange={(event) => changeFilter(() => {
            const nextIndex = event.target.value;
            if (nextIndex === 'missing')
                return;
            setCashFlowItemIndex(nextIndex);
            setCashFlowItemId(nextIndex === '' ? undefined : data.cashFlowItems[Number(nextIndex)]?.id);
            if (nextIndex !== '')
                setCashFlowItemLabel(data.cashFlowItems[Number(nextIndex)]?.name ?? 'Выбранная статья ДДС');
        })}>
            <option value="">Все статьи ДДС</option>
            {cashFlowItemIndex === 'missing' && <option value="missing">{cashFlowItemLabel}</option>}
            {data.cashFlowItems.map((item, index) => (<option value={index} key={item.id}>{item.name}</option>))}
          </select>
        </label>
        <div className="budget-execution-filter-actions">
        <button type="button" onClick={() => {
            closeReservation(false);
            setPageNumber(0);
            setAppliedFilters({ structure, costCenterId, cashFlowItemId, budgetMonth: month === '' ? undefined : `${month}-01` });
        }}>Применить</button>
        <button type="button" onClick={() => {
            const currentMonth = toBusinessDateInput().slice(0, 7);
            setStructure('current');
            setMonth(currentMonth);
            setCostCenterId(undefined);
            setCashFlowItemId(undefined);
            setCostCenterIndex('');
            setCashFlowItemIndex('');
            closeReservation(false);
            setPageNumber(0);
            setAppliedFilters({ structure: 'current', costCenterId: undefined, cashFlowItemId: undefined, budgetMonth: `${currentMonth}-01` });
        }}>Сбросить</button>
        <button type="button" onClick={reload} disabled={data.loading}>
          Обновить
        </button>
        <button type="button" onClick={() => { void exportExcel(); }} disabled={exporting || data.loading || data.error !== null} aria-busy={exporting}>
          Выгрузить в Excel
        </button>
        </div>
      </div>

      {(appliedFilters.structure !== structure || appliedFilters.costCenterId !== costCenterId
            || appliedFilters.cashFlowItemId !== cashFlowItemId || appliedFilters.budgetMonth !== (month ? `${month}-01` : undefined))
            && <p className="muted">Отбор изменён. Нажмите «Применить».</p>}
      {exporting && <p role="status">Готовим файл выгрузки…</p>}
      {exportError !== null && <p role="alert">{exportError}</p>}
      {!data.loading && data.error === null && data.page.factSource !== undefined && (<div>
        {executionCaptureDelayed(data.page.factSource.capturedAtUtc) && <p role="status" className="warning">
          Обновление факта задержано. Показан последний проверенный срез.
        </p>}
        <p className="budget-execution-quiet">
          Факт принят: {formatBusinessTimestamp(data.page.factSource.capturedAtUtc)}.{' '}
          Оплачено — выплаты факта ДДС с 2026 года по{' '}
          {new Date(Date.parse(`${data.page.factSource.cutoffExclusiveLocal}T00:00:00Z`) - 86400000).toLocaleDateString('ru-RU', { timeZone: 'UTC' })}
          {' '}включительно, без внутренних перемещений и депозитов. Зарезервировано — расходные заявки.
        </p>
        </div>)}

      {!data.loading && data.error === null && (<section className="budget-execution-totals" aria-labelledby="budget-execution-totals-heading">
          <h2 id="budget-execution-totals-heading">Итого по выбранной области</h2>
          <dl>
            <div>
              <dt>Позиций в выборке</dt>
              <dd>{totals.totalPositions}</dd>
            </div>
            <div>
              <dt>Лимит, ₸</dt>
              <dd className="budget-execution-money">
                {formatWholeMoney(totals.limitAmountKzt)}
              </dd>
            </div>
            <div>
              <dt>Зарезервировано, ₸</dt>
              <dd className="budget-execution-money">{formatWholeMoney(totals.reservedAmountKzt)}</dd>
            </div>
            <div>
              <dt>Оплачено, ₸</dt>
              <dd className="budget-execution-money">{formatWholeMoney(totals.paidAmountKzt)}</dd>
            </div>
            <div>
              <dt>Доступно, ₸</dt>
              <dd className={`budget-execution-money${totals.availableAmountKzt < 0 ? ' budget-execution-overrun' : ''}`}>
                {formatWholeMoney(totals.availableAmountKzt)}
              </dd>
            </div>
          </dl>
        </section>)}

      {data.loading && <p role="status">Загрузка исполнения бюджета…</p>}
      {data.error !== null && (<div role="alert">
          <p>{data.error}</p>
          <button type="button" onClick={reload}>Повторить</button>
        </div>)}
      {!data.loading && data.error === null && rows.length === 0 && (<p role="status">Бюджетные позиции не найдены.</p>)}
      {!data.loading && data.error === null && rows.length > 0 && (<div className="budget-execution-table">
        <table>
          <caption>Исполнение бюджета — текущие бюджетные позиции</caption>
          <thead>
            <tr>
              <th scope="col">Месяц</th>
              <th scope="col">ЦФО</th>
              <th scope="col">Статья ДДС</th>
              <th scope="col" className="budget-execution-money">Лимит, ₸</th>
              <th scope="col" className="budget-execution-money">Зарезервировано, ₸</th>
              <th scope="col" className="budget-execution-money">Оплачено, ₸</th>
              <th scope="col" className="budget-execution-money">Доступно, ₸</th>
              <th scope="col">Состояние лимита</th>
              <th scope="col">Состав резерва</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((item, index) => {
                const rowKey = keyOf(item);
                const isOpen = openKey === rowKey || !!item.planCostCenters?.some(source => keyOf({ ...item, costCenterId: source.costCenterId }) === openKey);
                const reserveSources = item.planCostCenters?.filter(source => source.reservedAmountKzt > 0);
                const effectiveAvailable = item.availableAmountKzt;
                const isOverrun = effectiveAvailable < 0;
                // The identifiers of a position never reach the DOM, so the
                // disclosure is addressed by its position in the page.
                const panelId = `budget-reservation-panel-${index}`;
                return (<Fragment key={rowKey}>
                  <tr>
                    <td>{formatBudgetMonth(item.budgetMonth)}</td>
                    <td>{item.costCenterId === '00000000-0000-0000-0000-000000000000'
                        ? '' : labelFor(data.costCenters, item.costCenterId, 'Неизвестный ЦФО')}
                      {item.planCostCenters && item.planCostCenters.length > 0 && <small className="budget-execution-plan-cfo">
                        ЦФО плана: {item.planCostCenters.map(source => source.code ?? source.name ?? 'Нет в справочнике').join(', ')}
                      </small>}
                    </td>
                    <td>{labelFor(data.cashFlowItems, item.cashFlowItemId, 'Неизвестная статья ДДС')}</td>
                    <td className="budget-execution-money">{formatWholeMoney(item.limitAmountKzt)}</td>
                    <td className="budget-execution-money">{formatWholeMoney(item.reservedAmountKzt)}</td>
                    <td className="budget-execution-money">{formatWholeMoney(item.paidAmountKzt)}</td>
                    <td className={`budget-execution-money${isOverrun ? ' budget-execution-overrun' : ''}`}>{formatWholeMoney(effectiveAvailable)}</td>
                    <td className={isOverrun ? 'budget-execution-overrun' : undefined}>{isOverrun ? 'Превышение' : 'В пределах'}</td>
                    <td>
                      {item.reservedAmountKzt === 0
                        ? <span className="budget-execution-quiet">Резерв отсутствует</span>
                        : ((reserveSources ?? [{ costCenterId: item.costCenterId, code: null }]).map((source, sourceIndex) => <button key={sourceIndex} type="button" id={sourceIndex === 0 ? `budget-reservation-toggle-${index}` : undefined} aria-expanded={openKey === keyOf({ ...item, costCenterId: source.costCenterId })} aria-controls={panelId} onClick={() => toggleReservation({ ...item, costCenterId: source.costCenterId })} onKeyDown={(event) => {
                                // The focus stays on this button after it opens
                                // the panel, so Escape has to be heard here too -
                                // not only inside the panel.
                                if (isOpen && event.key === 'Escape') {
                                    event.stopPropagation();
                                    closeReservation(true);
                                }
                            }}>
                            Показать состав резерва
                            {item.planCostCenters && ` — ${source.code ?? labelFor(data.costCenters, source.costCenterId, 'ЦФО плана')}`}
                          </button>))}
                    </td>
                  </tr>
                  {isOpen && (<tr className="budget-reservation-row">
                      <td colSpan={9}>
                        <section id={panelId} className="budget-reservation" aria-label="Состав резерва бюджетной позиции" onKeyDown={(event) => {
                            if (event.key === 'Escape') {
                                event.stopPropagation();
                                closeReservation(true);
                            }
                        }}>
                          {renderReservation()}
                        </section>
                      </td>
                    </tr>)}
                </Fragment>);
            })}
          </tbody>
        </table>
        </div>)}
      {!data.loading && data.error === null && (<div className="budget-execution-pagination">
          <span role="status" className="budget-execution-page-range">
            {data.page.total === 0 ? 'Строки 0 из 0' : `Строки ${data.page.skip + 1}–${Math.min(data.page.skip + rows.length, data.page.total)} из ${data.page.total}`}
          </span>
          <nav aria-label="Страницы исполнения бюджета">
          <button type="button" disabled={pageNumber === 0} onClick={() => {
                closeReservation(false);
                setPageNumber(0);
            }}>Первая</button>
          <button type="button" disabled={pageNumber === 0} onClick={() => {
                closeReservation(false);
                setPageNumber((current) => current - 1);
            }}>
            Назад
          </button>
          {budgetPageNumbers(pageNumber + 1, totalPages).map((number, index) => number === null
                ? <span key={`gap-${index}`} aria-hidden="true">…</span>
                : <button key={number} type="button" aria-label={`Страница ${number}`} aria-current={number === pageNumber + 1 ? 'page' : undefined} onClick={() => {
                        if (number === pageNumber + 1)
                            return;
                        closeReservation(false);
                        setPageNumber(number - 1);
                    }}>{number}</button>)}
          <span aria-live="polite">Страница {totalPages === 0 ? 0 : pageNumber + 1} из {totalPages}</span>
          <button type="button" disabled={pageNumber + 1 >= totalPages} onClick={() => {
                closeReservation(false);
                setPageNumber((current) => current + 1);
            }}>
            Вперёд
          </button>
          <button type="button" disabled={pageNumber + 1 >= totalPages} onClick={() => {
                closeReservation(false);
                setPageNumber(Math.max(totalPages - 1, 0));
            }}>Последняя</button>
          </nav>
          <label>
            Строк на странице{' '}
            <select value={pageSize} onChange={(event) => {
                const size = Number(event.target.value);
                if (!pageSizes.some((allowed) => allowed === size) || size === pageSize)
                    return;
                closeReservation(false);
                setPageNumber(0);
                setPageSize(size);
            }}>
              {pageSizes.map((size) => <option key={size} value={size}>{size}</option>)}
            </select>
          </label>
        </div>)}
    </div>);
}
