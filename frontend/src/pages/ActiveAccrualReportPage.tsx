import { useWorkspaceRefreshAction } from '../components/WorkspaceRefreshContext';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { activeAccrualColumnKeys, getActiveAccrualExport, type ActiveAccrualColumnKey, type ActiveAccrualFilters, type ActiveAccrualGrouping, type ActiveAccrualRow } from '../api';
import { useActiveAccrualReportData } from '../hooks/useActiveAccrualReportData';
import { excelExportFailureMessage } from '../api/excelExportFailure';
import '../styles/active-accruals.css';
import '../styles/list-columns.css';
const pageSize = 50;
const exportErrorMessage = 'Не удалось сформировать файл управленческих начислений.';
const moneyFormatter = new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
});
const ageZoneLabels = {
    none: 'Без выделения', yellow: 'Жёлтая', orange: 'Оранжевая', red: 'Красная',
};
const columnLabels: Record<ActiveAccrualColumnKey, string> = {
    accrualPeriod: 'Период начисления',
    fullyPaidDate: 'Полная оплата',
    ageDays: 'Дней без закрытия',
    ageZone: 'Возрастная зона',
    requestLine: 'Расходная заявка / строка',
    lineCount: 'Строк в заявке',
    organization: 'Организация',
    initiator: 'Инициатор',
    costCenter: 'ЦФО',
    cashFlowItem: 'Статья ДДС',
    expenseItem: 'Статья расходов',
    counterparty: 'Контрагент',
    contract: 'Договор',
    purpose: 'Назначение',
    closingProgress: 'Закрытие',
    originalAmount: 'Исходная сумма',
    confirmedClosedAmount: 'Закрыто',
    remainingAmount: 'Остаток',
    originalAmountKzt: 'Исходная сумма, KZT',
    confirmedClosedAmountKzt: 'Закрыто, KZT',
    remainingAmountKzt: 'Остаток, KZT',
};
type Props = {
    onOpenRequest?: (requestId: string) => void;
};
function amount(value: number, currency: string): string {
    return `${moneyFormatter.format(value)} ${currency}`;
}
function month(value: string): string {
    const [year, monthNumber] = value.split('-');
    return `${monthNumber}.${year}`;
}
function monthRange(from: string, through: string): string {
    return from === through ? month(from) : `${month(from)}–${month(through)}`;
}
function calendarDate(value: string): string {
    const [year, monthNumber, day] = value.split('-');
    return `${day}.${monthNumber}.${year}`;
}
function isAbortError(error: unknown): boolean {
    return ((typeof DOMException !== 'undefined' && error instanceof DOMException)
        || error instanceof Error)
        && error.name === 'AbortError';
}
function Row({ row, columns, onOpenRequest, }: {
    row: ActiveAccrualRow;
    columns: readonly ActiveAccrualColumnKey[];
    onOpenRequest?: (requestId: string) => void;
}) {
    return (<tr className={`active-accrual-age-${row.ageZone}`}>
      {columns.map((column) => {
            let content: ReactNode;
            switch (column) {
                case 'accrualPeriod':
                    content = monthRange(row.accrualPeriod, row.accrualPeriodThrough);
                    break;
                case 'fullyPaidDate':
                    content = calendarDate(row.fullyPaidBusinessDate);
                    break;
                case 'ageDays':
                    content = row.ageDays.toLocaleString('ru-RU');
                    break;
                case 'ageZone':
                    content = ageZoneLabels[row.ageZone];
                    break;
                case 'requestLine':
                    content = onOpenRequest === undefined
                        ? (row.isRequestSummary ? 'Заявка' : row.lineNumber)
                        : (<button type="button" className="active-accruals-open" onClick={() => onOpenRequest(row.expenseRequestId)} aria-label={row.isRequestSummary
                                ? 'Открыть свёрнутую заявку'
                                : `Открыть заявку, строка ${row.lineNumber}`}>
                  {row.isRequestSummary ? 'Открыть заявку' : row.lineNumber}
                </button>);
                    break;
                case 'lineCount':
                    content = row.lineCount.toLocaleString('ru-RU');
                    break;
                case 'organization':
                    content = row.organizationName;
                    break;
                case 'initiator':
                    content = row.initiatorName;
                    break;
                case 'costCenter':
                    content = row.costCenterName;
                    break;
                case 'cashFlowItem':
                    content = row.cashFlowItemName;
                    break;
                case 'expenseItem':
                    content = row.expenseItemName;
                    break;
                case 'counterparty':
                    content = row.counterpartyName ?? '—';
                    break;
                case 'contract':
                    content = row.contractName ?? '—';
                    break;
                case 'purpose':
                    content = row.purpose ?? '—';
                    break;
                case 'closingProgress':
                    content = row.closingProgress === 'partiallyClosed' ? 'Закрыто частично' : 'Не закрыто';
                    break;
                case 'originalAmount':
                    content = amount(row.originalAmount, row.currencyCode);
                    break;
                case 'confirmedClosedAmount':
                    content = amount(row.confirmedClosedAmount, row.currencyCode);
                    break;
                case 'remainingAmount':
                    content = amount(row.remainingAmount, row.currencyCode);
                    break;
                case 'originalAmountKzt':
                    content = amount(row.originalAmountKzt, 'KZT');
                    break;
                case 'confirmedClosedAmountKzt':
                    content = amount(row.confirmedClosedAmountKzt, 'KZT');
                    break;
                case 'remainingAmountKzt':
                    content = amount(row.remainingAmountKzt, 'KZT');
                    break;
            }
            return <td key={column} className={column.includes('Amount') || column === 'ageDays' || column === 'lineCount' ? 'active-accruals-number' : undefined}>{content}</td>;
        })}
    </tr>);
}
export function ActiveAccrualReportPage({ onOpenRequest }: Props) {
    const [organizationIndex, setOrganizationIndex] = useState('');
    const [organizationId, setOrganizationId] = useState<string | undefined>();
    const [costCenterIndex, setCostCenterIndex] = useState('');
    const [costCenterId, setCostCenterId] = useState<string | undefined>();
    const [counterpartyIndex, setCounterpartyIndex] = useState('');
    const [counterpartyId, setCounterpartyId] = useState<string | undefined>();
    const [contractIndex, setContractIndex] = useState('');
    const [contractId, setContractId] = useState<string | undefined>();
    const [collapseToRequest, setCollapseToRequest] = useState(false);
    const [groupBy, setGroupBy] = useState<ActiveAccrualGrouping>('none');
    const [periodFrom, setPeriodFrom] = useState('');
    const [periodThrough, setPeriodThrough] = useState('');
    const [pageNumber, setPageNumber] = useState(0);
    const [selectedColumns, setSelectedColumns] = useState<ActiveAccrualColumnKey[]>(() => [...activeAccrualColumnKeys]);
    const [reloadToken, setReloadToken] = useState(0);
    const [exporting, setExporting] = useState(false);
    const [exportError, setExportError] = useState<string | null>(null);
    const exportSequence = useRef(0);
    const exportController = useRef<AbortController | null>(null);
    const periodValid = periodFrom === '' || periodThrough === '' || periodFrom <= periodThrough;
    const draftFilters = useMemo<ActiveAccrualFilters>(() => ({
        organizationId,
        costCenterId,
        counterpartyId,
        contractId,
        accrualPeriodFrom: periodFrom === '' ? undefined : `${periodFrom}-01`,
        accrualPeriodThrough: periodThrough === '' ? undefined : `${periodThrough}-01`,
        detailLevel: collapseToRequest ? 'request' : 'line',
        ...(groupBy === 'none' ? {} : { groupBy }),
    }), [
        collapseToRequest,
        groupBy,
        contractId,
        costCenterId,
        counterpartyId,
        organizationId,
        periodFrom,
        periodThrough,
    ]);
    const [filters, setFilters] = useState<ActiveAccrualFilters>(() => draftFilters);
    const appliedPeriodValid = !filters.accrualPeriodFrom || !filters.accrualPeriodThrough
        || filters.accrualPeriodFrom <= filters.accrualPeriodThrough;
    const data = useActiveAccrualReportData({
        filters,
        catalogFilters: draftFilters,
        skip: pageNumber * pageSize,
        take: pageSize,
        enabled: appliedPeriodValid,
        reloadToken,
    });
    // Partition the server page at its boundaries; never sort or group by name
    // locally. A new tbody gives each group header its own accessible row scope.
    const sections: ActiveAccrualRow[][] = [];
    for (const row of data.page.items) {
        if (sections.length === 0 || (data.page.grouping !== undefined && row.startsGroup)) {
            sections.push([]);
        }
        sections[sections.length - 1].push(row);
    }
    useEffect(() => {
        if (organizationIndex === '')
            return;
        const nextIndex = data.organizations.findIndex((item) => item.id.toLowerCase() === organizationId?.toLowerCase());
        if (nextIndex < 0) {
            setOrganizationIndex('');
            setOrganizationId(undefined);
        }
        else if (String(nextIndex) !== organizationIndex) {
            setOrganizationIndex(String(nextIndex));
        }
    }, [data.organizations, organizationId, organizationIndex]);
    useEffect(() => {
        if (costCenterIndex === '')
            return;
        const nextIndex = data.costCenters.findIndex((item) => item.id.toLowerCase() === costCenterId?.toLowerCase());
        if (nextIndex < 0) {
            setCostCenterIndex('');
            setCostCenterId(undefined);
        }
        else if (String(nextIndex) !== costCenterIndex) {
            setCostCenterIndex(String(nextIndex));
        }
    }, [costCenterId, costCenterIndex, data.costCenters]);
    useEffect(() => {
        if (counterpartyIndex === '')
            return;
        const nextIndex = data.counterparties.findIndex((item) => item.id.toLowerCase() === counterpartyId?.toLowerCase());
        if (nextIndex < 0) {
            setCounterpartyIndex('');
            setCounterpartyId(undefined);
            setContractIndex('');
            setContractId(undefined);
        }
        else if (String(nextIndex) !== counterpartyIndex) {
            setCounterpartyIndex(String(nextIndex));
        }
    }, [counterpartyId, counterpartyIndex, data.counterparties]);
    useEffect(() => {
        if (contractIndex === '')
            return;
        const nextIndex = data.contracts.findIndex((item) => item.id.toLowerCase() === contractId?.toLowerCase());
        if (nextIndex < 0) {
            setContractIndex('');
            setContractId(undefined);
        }
        else if (String(nextIndex) !== contractIndex) {
            setContractIndex(String(nextIndex));
        }
    }, [contractId, contractIndex, data.contracts]);
    useEffect(() => () => {
        exportSequence.current += 1;
        exportController.current?.abort();
    }, []);
    const changeFilter = (change: () => void) => {
        exportSequence.current += 1;
        exportController.current?.abort();
        exportController.current = null;
        setExporting(false);
        change();
        setExportError(null);
    };
    const changeColumns = (columns: ActiveAccrualColumnKey[]) => {
        if (columns.length === 0)
            return;
        exportSequence.current += 1;
        exportController.current?.abort();
        exportController.current = null;
        setExporting(false);
        setExportError(null);
        const selected = new Set(columns);
        setSelectedColumns(activeAccrualColumnKeys.filter((column) => selected.has(column)));
    };
    useWorkspaceRefreshAction(() => {
        exportSequence.current += 1;
        exportController.current?.abort();
        exportController.current = null;
        setExporting(false);
        setExportError(null);
    });
    const refresh = () => {
        exportSequence.current += 1;
        exportController.current?.abort();
        exportController.current = null;
        setExporting(false);
        setPageNumber(0);
        setExportError(null);
        setReloadToken((current) => current + 1);
    };
    const exportExcel = async () => {
        if (exporting || !appliedPeriodValid)
            return;
        const sequence = exportSequence.current + 1;
        exportSequence.current = sequence;
        const controller = new AbortController();
        exportController.current = controller;
        setExporting(true);
        setExportError(null);
        try {
            const download = await getActiveAccrualExport(filters, controller.signal, selectedColumns);
            if (sequence !== exportSequence.current || controller.signal.aborted)
                return;
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
            if (sequence === exportSequence.current
                && !controller.signal.aborted
                && !isAbortError(error)) {
                setExportError(excelExportFailureMessage(error, exportErrorMessage));
            }
        }
        finally {
            if (sequence === exportSequence.current) {
                exportController.current = null;
                setExporting(false);
            }
        }
    };
    const totalPages = data.page.total === 0 ? 0 : Math.ceil(data.page.total / pageSize);
    const totals = data.page.totals;
    const leadingFooterColumns = selectedColumns.findIndex((column) => column === 'originalAmountKzt'
        || column === 'confirmedClosedAmountKzt'
        || column === 'remainingAmountKzt');
    const footerLabelSpan = leadingFooterColumns < 0
        ? selectedColumns.length
        : leadingFooterColumns;
    useEffect(() => {
        if (!data.loading && data.error === null && pageNumber >= totalPages && pageNumber !== 0) {
            setPageNumber(Math.max(0, totalPages - 1));
        }
    }, [data.error, data.loading, pageNumber, totalPages]);
    return (<div className="active-accruals-page">
      <h1>Активные управленческие начисления</h1>
      <div className="active-accruals-filters" aria-label="Фильтры активных управленческих начислений">
        <label>
          Организация
          <select value={organizationIndex} onChange={(event) => changeFilter(() => {
            const nextIndex = event.target.value;
            setOrganizationIndex(nextIndex);
            setOrganizationId(nextIndex === '' ? undefined : data.organizations[Number(nextIndex)]?.id);
            setContractIndex('');
            setContractId(undefined);
        })}>
            <option value="">Все организации</option>
            {data.organizations.map((item, index) => (<option key={item.id} value={index}>{item.name}</option>))}
          </select>
        </label>
        <label>
          Контрагент
          <select value={counterpartyIndex} onChange={(event) => changeFilter(() => {
            const nextIndex = event.target.value;
            setCounterpartyIndex(nextIndex);
            setCounterpartyId(nextIndex === ''
                ? undefined
                : data.counterparties[Number(nextIndex)]?.id);
            setContractIndex('');
            setContractId(undefined);
        })}>
            <option value="">Все контрагенты</option>
            {data.counterparties.map((item, index) => (<option key={item.id} value={index}>{item.name}</option>))}
          </select>
        </label>
        <label>
          Договор
          <select value={contractIndex} disabled={organizationId === undefined
            || counterpartyId === undefined
            || data.contractsLoading} onChange={(event) => changeFilter(() => {
            const nextIndex = event.target.value;
            setContractIndex(nextIndex);
            setContractId(nextIndex === '' ? undefined : data.contracts[Number(nextIndex)]?.id);
        })}>
            <option value="">Все договоры</option>
            {data.contracts.map((item, index) => (<option key={item.id} value={index}>{item.name}</option>))}
          </select>
        </label>
        <label>
          ЦФО
          <select value={costCenterIndex} onChange={(event) => changeFilter(() => {
            const nextIndex = event.target.value;
            setCostCenterIndex(nextIndex);
            setCostCenterId(nextIndex === '' ? undefined : data.costCenters[Number(nextIndex)]?.id);
        })}>
            <option value="">Все ЦФО</option>
            {data.costCenters.map((item, index) => (<option key={item.id} value={index}>{item.name}</option>))}
          </select>
        </label>
        <label>
          Группировка
          <select value={groupBy} onChange={(event) => {
            const value = event.target.value;
            if (value === 'none' || value === 'counterparty' || value === 'contract') {
                changeFilter(() => setGroupBy(value));
            }
        }}>
            <option value="none">Без группировки</option>
            <option value="counterparty">По контрагенту</option>
            <option value="contract">По договору</option>
          </select>
        </label>
        <label className="active-accruals-collapse">
          <input type="checkbox" checked={collapseToRequest} onChange={(event) => changeFilter(() => setCollapseToRequest(event.target.checked))}/>
          Свернуть до одной строки на заявку
        </label>
        <label>
          Период начисления с
          <input type="month" value={periodFrom} onChange={(event) => changeFilter(() => setPeriodFrom(event.target.value))}/>
        </label>
        <label>
          по
          <input type="month" value={periodThrough} onChange={(event) => changeFilter(() => setPeriodThrough(event.target.value))}/>
        </label>
        <div className="compact-filter-actions">
        <button type="button" disabled={!periodValid} onClick={() => {
            setFilters(draftFilters);
            setPageNumber(0);
        }}>Применить</button>
        <button type="button" onClick={() => {
            setOrganizationIndex('');
            setOrganizationId(undefined);
            setCostCenterIndex('');
            setCostCenterId(undefined);
            setCounterpartyIndex('');
            setCounterpartyId(undefined);
            setContractIndex('');
            setContractId(undefined);
            setPeriodFrom('');
            setPeriodThrough('');
            setCollapseToRequest(false);
            setGroupBy('none');
            setFilters({ detailLevel: 'line' });
            setPageNumber(0);
            setReloadToken(current => current + 1);
        }}>Сбросить</button>
        <button type="button" disabled={data.loading} onClick={refresh}>
          Обновить
        </button>
        <button type="button" disabled={exporting || !appliedPeriodValid} aria-busy={exporting} onClick={() => { void exportExcel(); }}>
          Выгрузить в Excel
        </button>
        </div>
      </div>

      <details className="list-column-picker active-accruals-column-picker">
        <summary>Колонки: {selectedColumns.length} из {activeAccrualColumnKeys.length}</summary>
        <fieldset>
          <legend>Видимые колонки экрана и Excel</legend>
          <div className="list-column-options">
            {activeAccrualColumnKeys.map((column) => (<label key={column}>
                <input type="checkbox" checked={selectedColumns.includes(column)} disabled={selectedColumns.length === 1 && selectedColumns[0] === column} onChange={(event) => changeColumns(event.target.checked
                ? [...selectedColumns, column]
                : selectedColumns.filter((selected) => selected !== column))}/>
                <span>{columnLabels[column]}</span>
              </label>))}
          </div>
          <div className="list-column-buttons">
            <button className="secondary-button compact" type="button" disabled={selectedColumns.length === activeAccrualColumnKeys.length} onClick={() => changeColumns([...activeAccrualColumnKeys])}>
              Показать все
            </button>
          </div>
        </fieldset>
      </details>

      {!periodValid && (<p role="alert">Начало периода начисления не может быть позже окончания.</p>)}
      {JSON.stringify(draftFilters) !== JSON.stringify(filters) && <p className="muted">Отбор изменён. Нажмите «Применить».</p>}
      {exporting && <p role="status">Готовим файл выгрузки…</p>}
      {exportError !== null && <p role="alert">{exportError}</p>}
      {data.loading && appliedPeriodValid && <p role="status">Загрузка управленческих начислений…</p>}
      {data.error !== null && appliedPeriodValid && (<div role="alert">
          <p>{data.error}</p>
          <button type="button" onClick={refresh}>
            Повторить
          </button>
        </div>)}

      {!data.loading && data.error === null && appliedPeriodValid && (<section className="active-accruals-totals" aria-labelledby="active-accruals-totals-heading">
          <h2 id="active-accruals-totals-heading">Итого по выбранной области</h2>
          <dl>
            <div>
              <dt>{filters.detailLevel === 'request' ? 'Заявок' : 'Строк'}</dt>
              <dd>{data.page.total}</dd>
            </div>
            <div><dt>Исходная сумма, KZT</dt><dd>{amount(totals.originalAmountKzt, 'KZT')}</dd></div>
            <div><dt>Подтверждено закрытием, KZT</dt><dd>{amount(totals.confirmedClosedAmountKzt, 'KZT')}</dd></div>
            <div><dt>Активный остаток, KZT</dt><dd>{amount(totals.remainingAmountKzt, 'KZT')}</dd></div>
          </dl>
        </section>)}

      {!data.loading && data.error === null && appliedPeriodValid && data.page.items.length === 0 && (<p role="status">Активные управленческие начисления не найдены.</p>)}
      {!data.loading && data.error === null && appliedPeriodValid && data.page.items.length > 0 && (<div className="active-accruals-table-wrap">
          <table className="active-accruals-table" style={{ minWidth: `${Math.max(700, selectedColumns.length * 105)}px` }}>
            <caption>Незакрытые остатки управленческих начислений</caption>
            <thead>
              <tr>
                {selectedColumns.map((column) => (<th scope="col" key={column}>{columnLabels[column]}</th>))}
              </tr>
            </thead>
            {sections.map((rows) => (<tbody key={`${rows[0].expenseRequestId}:${rows[0].isRequestSummary ? 'request' : rows[0].lineNumber}`}>
                {data.page.grouping !== undefined && (<tr className="active-accruals-group">
                    <th scope="rowgroup" colSpan={selectedColumns.length}>
                      {data.page.grouping === 'counterparty'
                        ? `Контрагент: ${rows[0].counterpartyName ?? 'Без контрагента'}`
                        : `Договор: ${rows[0].contractName ?? 'Без договора'}`}
                    </th>
                  </tr>)}
                {rows.map((row) => (<Row key={`${row.expenseRequestId}:${row.isRequestSummary ? 'request' : row.lineNumber}`} row={row} columns={selectedColumns} onOpenRequest={onOpenRequest}/>))}
              </tbody>))}
            <tfoot>
              <tr aria-label="Итого по выбранной области">
                {footerLabelSpan > 0 && (<th scope="row" colSpan={footerLabelSpan}>Итого по выбранной области</th>)}
                {selectedColumns.slice(footerLabelSpan).map((column) => (<td key={column} className="active-accruals-number">
                    {column === 'originalAmountKzt' ? amount(totals.originalAmountKzt, 'KZT')
                    : column === 'confirmedClosedAmountKzt'
                        ? amount(totals.confirmedClosedAmountKzt, 'KZT')
                        : amount(totals.remainingAmountKzt, 'KZT')}
                  </td>))}
              </tr>
            </tfoot>
          </table>
        </div>)}

      {!data.loading && data.error === null && (totalPages > 1 || pageNumber > 0) && (<nav aria-label="Страницы активных управленческих начислений" className="active-accruals-pagination">
          <button type="button" disabled={pageNumber === 0} onClick={() => setPageNumber((current) => current - 1)}>
            Назад
          </button>
          <span aria-live="polite">Страница {pageNumber + 1} из {totalPages}</span>
          <button type="button" disabled={pageNumber + 1 >= totalPages} onClick={() => setPageNumber((current) => current + 1)}>
            Вперёд
          </button>
        </nav>)}
    </div>);
}
