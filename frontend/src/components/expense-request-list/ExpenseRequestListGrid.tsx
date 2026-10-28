import { Copy, Trash2 } from 'lucide-react';
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import type { ExpenseRequestListSort, ExpenseRequestSortKey } from '../../utils/expenseRequestListSort';
import { fitRequestColumns } from './fitRequestColumns';
import { formatDate, formatPlannedPaymentDate, statusWithStageLabel } from '../../utils/expenseRequestListFormat';
import type { ExpenseRequestListItem } from '../../types';
import { listMoneyCents } from '../../api/expenseRequestListBusinessColumns';
import { safeActorLabel } from '../../utils/expenseRequestSystemFields';
import { businessColumnDefinitions, type BusinessColumnKey } from './ExpenseRequestListColumnPicker';
import { openableTableRow } from '../../utils/openableTableRow';
import { formatExpenseRequestDate } from '../../utils/expenseRequestDate326';
import { formatBusinessTimestamp } from '../../utils/businessTime';
import { expenseRequestStatusTone } from '../../expenseRequestStatus';
import { formatActualPaymentDate } from '../../utils/expenseRequestActualPaymentDate367';
const wholeMoneyFormatter = new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
});
// Display only: keep the exact server amount and all calculations untouched.
// Decimal-string business columns must not round-trip through binary64.
function formatListAmount(value: string | number): string {
    if (typeof value === 'number')
        return wholeMoneyFormatter.format(value);
    const cents = listMoneyCents(value);
    if (cents === null)
        throw new Error('Некорректная сумма списка.');
    return ((cents + 50n) / 100n).toLocaleString('ru-RU');
}
type Props = {
    items: readonly ExpenseRequestListItem[];
    loading: boolean;
    organizationNames: ReadonlyMap<string, string>;
    costCenterNames: ReadonlyMap<string, string>;
    counterpartyNames: ReadonlyMap<string, string>;
    onOpen: (id: string) => void;
    optionalColumns?: readonly BusinessColumnKey[];
    failed?: boolean;
    sort?: ExpenseRequestListSort;
    onSort?: (key: ExpenseRequestSortKey) => void;
    onCopy?: (item: ExpenseRequestListItem) => void;
    onDelete?: (item: ExpenseRequestListItem) => void;
    canMutate?: (item: ExpenseRequestListItem) => boolean;
    canCopy?: (item: ExpenseRequestListItem) => boolean;
    actionsBusy?: boolean;
    showActions?: boolean;
};
const basisLabels: Readonly<Record<string, string>> = {
    Contract: 'Договор', InvoiceWithoutContract: 'Счёт без договора',
    PublicOffer: 'Публичная оферта', OtherBasis: 'Другое основание',
};
const closingLabels = { notClosed: 'Не закрыта', partiallyClosed: 'Закрыта частично', closed: 'Закрыта полностью' };
function businessCell(item: ExpenseRequestListItem, key: BusinessColumnKey): string {
    if (!item.canOpen || item.businessColumns === undefined)
        return 'Недоступно';
    const columns = item.businessColumns;
    switch (key) {
        case 'contract': return columns.contractName ?? '—';
        case 'basis': return columns.paymentBasisCode === null ? '—' : basisLabels[columns.paymentBasisCode];
        case 'paid': return `${formatListAmount(columns.paidAmount)} ${item.currencyCode || 'KZT'}`;
        case 'remaining': return `${formatListAmount(columns.remainingAmount)} ${item.currencyCode || 'KZT'}`;
        case 'fullyPaidDate': return formatDate(columns.fullyPaidDate);
        case 'closing': return closingLabels[columns.closingState];
    }
}
function referenceName(names: ReadonlyMap<string, string>, identifier: string | null | undefined): string | undefined {
    if (identifier == null)
        return undefined;
    const exact = names.get(identifier);
    if (exact !== undefined)
        return exact;
    // UUIDs are a case-insensitive public contract. Keep the map opaque, but
    // do not turn a valid upper/lower-case representation into a missing label.
    const normalized = identifier.toLocaleLowerCase('en-US');
    for (const [key, name] of names) {
        if (key.toLocaleLowerCase('en-US') === normalized)
            return name;
    }
    return undefined;
}
export function ExpenseRequestListGrid({ items, loading, organizationNames, costCenterNames, counterpartyNames, onOpen, optionalColumns = [], failed = false, sort, onSort, onCopy, onDelete, canMutate = () => false, canCopy = () => false, actionsBusy = false, showActions = true, }: Props) {
    const [now, setNow] = useState(() => new Date());
    useEffect(() => {
        const refresh = () => setNow(new Date());
        const timer = window.setInterval(refresh, 30000);
        window.addEventListener('focus', refresh);
        return () => { window.clearInterval(timer); window.removeEventListener('focus', refresh); };
    }, []);
    const heading = (key: ExpenseRequestSortKey, label: string, content: ReactNode = label, amount = false) => <th scope="col" key={key} aria-label={key === 'plannedPaymentDate' ? label : undefined} data-request-sort={key} aria-sort={sort?.by === key ? sort.direction === 'asc' ? 'ascending' : 'descending' : undefined} className={amount ? 'amount-cell' : key === 'budget' || key === 'status' ? `request-${key}-cell` : undefined}>
      {onSort ? <button type="button" className="request-sort" onClick={() => onSort(key)} title={sort?.by === key && sort.direction === 'asc' ? 'Сортировать по убыванию' : 'Сортировать по возрастанию'}>
        {content}
      </button> : content}
    </th>;
    const columns = businessColumnDefinitions.filter((item) => optionalColumns.includes(item.key));
    const columnCount = 11 + Number(showActions) + columns.length;
    const tableRef = useRef<HTMLTableElement>(null);
    useLayoutEffect(() => {
        const table = tableRef.current;
        if (!table)
            return;
        let active = true;
        fitRequestColumns(table);
        void document.fonts?.ready.then(() => {
            if (active && table.isConnected)
                fitRequestColumns(table);
        });
        return () => { active = false; };
    }, [items, organizationNames, costCenterNames, counterpartyNames, optionalColumns, now, showActions]);
    return (<div className="request-grid-wrap" aria-busy={loading}>
      <table ref={tableRef} className="request-grid" data-bms-fit-columns="content" aria-label="Расходные заявки">
        <thead>
          <tr>
            {heading('requestNumber', 'Заявка')}
            {heading('requestDate', 'Дата заявки')}
            {heading('urgency', 'Срочность')}
            {heading('plannedPaymentDate', 'Планируемая дата', <><span className="request-header-line">Планируемая</span>{' '}<span className="request-header-line">дата</span></>)}
            {heading('organization', 'Фирма')}
            {heading('initiator', 'Инициатор')}
            {heading('counterparty', 'Контрагент')}
            {heading('amount', 'Сумма', 'Сумма', true)}
            {heading('budget', 'Бюджет')}
            {columns.map(({ key, label }) => <th scope="col" key={key}>{label}</th>)}
            {heading('status', 'Статус')}
            <th scope="col" className="request-date-cell">Дата оплаты</th>
            {showActions && <th scope="col" className="request-actions-cell">Действия</th>}
          </tr>
        </thead>
        <tbody>
          {items.map((item) => (<tr {...openableTableRow(`Открыть ${item.requestNumber ?? 'расходную заявку'}`, () => onOpen(item.id), !item.canOpen)} key={item.id} className={[
                item.canOpen && 'request-grid-row-openable',
                !item.canOpen && 'request-grid-row-inactive',
                item.status === 'CANCELLED' && 'request-grid-row-cancelled',
            ].filter(Boolean).join(' ') || undefined}>
              <td>{item.canOpen
                ? <button className="request-link" type="button" onClick={() => onOpen(item.id)}>{item.requestNumber ?? 'Расходная заявка'}</button>
                : <span className="request-link request-link-inactive">{item.requestNumber ?? 'Расходная заявка'}</span>}</td>
                <td className="request-date-cell"><time dateTime={item.requestDateUtc ?? item.createdAtUtc} title={formatBusinessTimestamp(item.requestDateUtc ?? item.createdAtUtc)}>
                  {formatExpenseRequestDate(item.requestDateUtc ?? item.createdAtUtc, now)}</time></td>
              <td><span className={`urgency-chip ${item.isUrgent ? 'urgent' : 'normal'}`}>{item.isUrgent ? 'Срочная' : 'Обычная'}</span></td>
              <td className="request-date-cell">{formatPlannedPaymentDate(item.plannedPaymentDate)}</td>
              <td>{referenceName(organizationNames, item.organizationId) ?? '—'}</td>
              <td>{safeActorLabel(item.initiatorDisplayName ?? item.initiatorExternalId)}</td>
              <td title={item.employeeNames?.filter(Boolean).join('; ')}>{item.employeeNames
                ? item.employeeNames.filter(Boolean).join('; ') || 'Сотрудники не указаны'
                : item.isNewCounterparty
                    ? item.candidateCounterpartyName || 'Новый контрагент'
                    : referenceName(counterpartyNames, item.counterpartyId) ?? '—'}</td>
              <td className="amount-cell">{formatListAmount(item.businessColumns?.obligationAmount ?? item.totalAmount)} {item.currencyCode || 'KZT'}</td>
              <td className="request-budget-cell"><span className={`budget-indicator ${item.isOverBudget === true ? 'over' : item.isOverBudget === false ? 'within' : 'unknown'}`}>
                {item.isOverBudget === true ? 'Сверх бюджета' : item.isOverBudget === false ? 'В рамках бюджета' : '—'}
              </span></td>
              {columns.map(({ key }) => <td key={key} className={key === 'paid' || key === 'remaining' ? 'amount-cell' : undefined}>
                {businessCell(item, key)}
              </td>)}
              <td className="request-status-cell"><span className={`status-chip ${item.status.toLocaleLowerCase('en-US')}`} data-status-tone={expenseRequestStatusTone(item.status, item.currentStage)}>{statusWithStageLabel(item.status, item.currentStage)}</span></td>
              <td className="request-date-cell">{formatActualPaymentDate(item)}</td>
              {showActions && <td className="request-actions-cell"><span className="request-row-actions">{item.canOpen
                    ? <>
                  {onCopy && <button className="row-action neutral" type="button" aria-label={`Копировать ${item.requestNumber ?? 'расходную заявку'}`} title={canCopy(item) ? 'Создать копию' : 'Для копирования нужен доступ к заявке и право создания РЗ'} disabled={actionsBusy || !canCopy(item)} onClick={() => onCopy(item)}><Copy size={15}/></button>}
                  {onDelete && item.status === 'DRAFT' && <button className="row-action neutral" type="button" aria-label={`Удалить ${item.requestNumber ?? 'черновик'}`} title={canMutate(item) ? 'Удалить черновик' : 'Удаление доступно автору черновика'} disabled={actionsBusy || !canMutate(item)} onClick={() => onDelete(item)}><Trash2 size={15}/></button>}
                </>
                    : <span className="row-action-inactive" aria-label="Заявка доступна только в списке">—</span>}</span></td>}
            </tr>))}
          {!loading && items.length === 0 && (<tr><td colSpan={columnCount} className="empty-grid">{failed ? 'Не удалось загрузить список.' : 'Заявок по выбранным условиям нет.'}</td></tr>)}
          {loading && (<tr><td colSpan={columnCount} className="empty-grid">Загрузка…</td></tr>)}
        </tbody>
      </table>
    </div>);
}
