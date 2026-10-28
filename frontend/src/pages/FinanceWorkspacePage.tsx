import { useWorkspaceRefreshGuard } from '../components/WorkspaceRefreshContext';
import { ArrowDownLeft, ArrowLeftRight, ArrowUpRight, BookOpenCheck, CheckCircle2, ChevronLeft, ChevronRight, CircleAlert, FileSearch, Filter, Landmark, ListOrdered, RefreshCw, Search, Wallet } from 'lucide-react';
import { memo, useMemo, useRef, useState } from 'react';
import { FinancePreparationForm } from '../components/FinancePreparationForm';
import { DebtorsByName470 } from '../components/DebtorsByName470';
import { ExpenseRequestPaymentDetailsPanel } from '../components/expense-request-form/ExpenseRequestPaymentDetailsPanel';
import { expenseRequestStatusCodes, expenseRequestStatusLabel } from '../expenseRequestStatus';
import { FINANCE_WORKSPACES, type FinanceWorkspaceId } from '../financeWorkspaces';
import { emptyFinanceFilters, financePageSize, useFinanceCatalogs, useFinanceRequests, type FinanceFilters } from '../hooks/useFinanceWorkspaceData';
import type { ExpenseRequestListItem, ExpenseRequestStatus, ReferenceDataItem } from '../types';
import { formatFinanceAmount as amount, type FinancePreparation } from '../utils/financeWorkspaceDraft';
import { safeActorLabel } from '../utils/expenseRequestSystemFields';
import '../styles/finance-workspaces.css';
const icons = { debtors: FileSearch, 'payment-priorities': ListOrdered, 'cashless-payments': Landmark,
    reconciliations: BookOpenCheck, 'debt-offsets': ArrowLeftRight, 'debt-writeoffs': Wallet,
    'cashless-receipts': ArrowDownLeft, 'cashless-withdrawals': ArrowUpRight };
const integer = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 });
const date = (value?: string | null) => value ? value.slice(0, 10).split('-').reverse().join('.') : 'Не указана';
const requestTitle = (item: ExpenseRequestListItem) => item.purpose?.trim() || 'Расходная заявка без назначения';
const initiatorLabel = (item: ExpenseRequestListItem) => safeActorLabel(item.initiatorDisplayName ?? item.initiatorExternalId);
const indexNames = (items: ReferenceDataItem[]) => new Map(items.map(item => [item.id.toLowerCase(), item.name]));
const lookup = (names: ReadonlyMap<string, string>, id?: string | null) => (id ? names.get(id.toLowerCase()) : undefined) ?? 'Название недоступно';
// Thousands of native options must not be rebuilt for every search keystroke,
// selection, request response or shell notification. Keep the full authorized
// directory and the native selector; reuse only this mounted view's elements.
const ReferenceOptions = memo(function ReferenceOptions({ items }: {
    items: ReferenceDataItem[];
}) {
    return <>{items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</>;
});
type Props = {
    workspaceId: FinanceWorkspaceId;
    draft: FinancePreparation;
    onDraftChange: (value: FinancePreparation) => void;
    onOpenRequest: (id: string) => void;
    onOpenCalendar: () => void;
};
export function FinanceWorkspacePage({ workspaceId, draft, onDraftChange, onOpenRequest, onOpenCalendar }: Props) {
    useWorkspaceRefreshGuard(Object.values(draft).some(value => value !== '')
        ? 'Локальный черновик подготовки сохранён в этой вкладке. Завершите или очистите его перед обновлением.' : null);
    const definition = FINANCE_WORKSPACES[workspaceId];
    const isDebtors = workspaceId === 'debtors';
    const [byNames, setByNames] = useState(false);
    const Icon = icons[workspaceId];
    const initial = { ...emptyFinanceFilters, status: definition.initialStatus ?? '' } satisfies FinanceFilters;
    const [draftFilters, setDraftFilters] = useState<FinanceFilters>(initial);
    const [filters, setFilters] = useState<FinanceFilters>(initial);
    const [skip, setSkip] = useState(0);
    const [generation, setGeneration] = useState(0);
    const [search, setSearch] = useState('');
    const [tab, setTab] = useState<'requests' | 'preparation'>(definition.draft ? 'preparation' : 'requests');
    const [selected, setSelected] = useState<{
        key: string;
        id: string;
    } | null>(null);
    const catalog = useFinanceCatalogs(Boolean(definition.draft));
    const data = useFinanceRequests(filters, skip, generation, definition.businessColumns, !isDebtors || !byNames);
    const region = useRef<HTMLDivElement>(null);
    const detailsRegion = useRef<HTMLElement>(null);
    const page = data.page;
    const names = useMemo(() => ({ organizations: indexNames(catalog.items.organizations),
        costCenters: indexNames(catalog.items.costCenters), counterparties: indexNames(catalog.items.counterparties) }), [catalog.items]);
    const selectedItem = selected?.key === data.key ? page?.items.find(item => item.id === selected.id && item.canOpen) : undefined;
    const items = useMemo(() => {
        const text = search.trim().toLocaleLowerCase('ru-RU');
        if (!text)
            return page?.items ?? [];
        return (page?.items ?? []).filter(item => !text || [requestTitle(item), initiatorLabel(item),
            lookup(names.organizations, item.organizationId), lookup(names.costCenters, item.defaultCostCenterId),
            lookup(names.counterparties, item.counterpartyId), expenseRequestStatusLabel(item.status)]
            .some(value => value?.toLocaleLowerCase('ru-RU').includes(text)));
    }, [page, search, names]);
    const changeFilter = <K extends keyof FinanceFilters>(key: K, value: FinanceFilters[K]) => setDraftFilters(current => ({ ...current, [key]: value }));
    const apply = () => { setFilters({ ...draftFilters }); setSkip(0); setGeneration(value => value + 1); setSelected(null); };
    const reset = () => { setDraftFilters(initial); setFilters(initial); setSkip(0); setSearch(''); setGeneration(value => value + 1); setSelected(null); };
    const select = (item: ExpenseRequestListItem) => {
        if (!item.canOpen)
            return;
        setSelected({ key: data.key, id: item.id });
        if (definition.draft)
            onDraftChange({ ...draft, relatedRequestTitle: requestTitle(item) });
        window.setTimeout(() => detailsRegion.current?.focus(), 0);
    };
    const movePage = (next: number) => { setSkip(next); setSelected(null); region.current?.focus(); };
    return <div className="finance-workspace" data-workspace={workspaceId}>
    <header className="finance-heading">
      <div className="finance-heading-main">{!isDebtors && <span className="finance-heading-icon"><Icon size={25}/></span>}<div>
        <h1>{definition.title}</h1><p className="finance-description">{definition.description}</p>
      </div></div>
      <span className="finance-mode"><span />{definition.draft ? 'Подготовка без проведения' : 'Просмотр данных'}</span>
    </header>

    {!isDebtors && <><div className="finance-boundary"><CircleAlert size={18} aria-hidden="true"/><p>{definition.boundary}</p></div>
    <div className="finance-toolbar">
      <div className="finance-tabs" role="group" aria-label="Режим рабочего экрана">
        {definition.draft && <button type="button" aria-pressed={tab === 'preparation'} onClick={() => setTab('preparation')}><BookOpenCheck size={16}/>Подготовка документа</button>}
        <button type="button" aria-pressed={tab === 'requests'} onClick={() => setTab('requests')}><FileSearch size={16}/>{definition.sourceLabel}</button>
      </div>
      <button type="button" className="finance-link" onClick={onOpenCalendar}>Платежный календарь <ArrowUpRight size={15}/></button>
    </div></>}

    {catalog.error && <div className="finance-error" role="alert">Справочники не загрузились. Имена не подменяются идентификаторами, выбор реквизитов недоступен. <button type="button" onClick={catalog.retry}>Повторить загрузку справочников</button></div>}

    {tab === 'preparation' && definition.draft && <FinancePreparationForm workspaceId={workspaceId} value={draft} onChange={onDraftChange} catalogs={catalog.items} catalogsUnavailable={catalog.loading || catalog.error} basis={draft.relatedRequestTitle || null} onChooseBasis={() => setTab('requests')}/>}

    {tab === 'requests' && <>
      {!isDebtors && <div className="finance-metrics" aria-label="Область загруженных данных">
        <div><span>Заявок по фильтрам API</span><strong>{page ? integer.format(page.total) : '—'}</strong><small>{data.failed ? 'Данные недоступны' : 'Серверная выборка'}</small></div>
        <div><span>На загруженной странице</span><strong>{page ? integer.format(page.items.length) : '—'}</strong><small>Не весь реестр</small></div>
        <div><span>Срочных на странице</span><strong>{page ? page.items.filter(item => item.isUrgent).length : '—'}</strong><small>Признак из расходной заявки</small></div>
        <div><span>Источник</span><strong className="finance-metric-label">Расходные заявки</strong><small>Доступ определяется сервером</small></div>
      </div>}
      <form className="finance-filters" onSubmit={event => { event.preventDefault(); apply(); }}>
        <div className="finance-card-heading"><Filter size={17}/><h2>Отбор заявок</h2>{isDebtors && <button type="button" className="debtors-mode470" onClick={() => { setByNames(v => !v); setSelected(null); }}>{byNames ? 'По заявкам' : 'По именам'}</button>}</div>
        <div className="finance-filter-grid">
          <label>Организация для отбора<select value={draftFilters.organizationId} disabled={catalog.loading || catalog.error} onChange={event => changeFilter('organizationId', event.target.value)}><option value="">Все доступные организации</option><ReferenceOptions items={catalog.items.organizations}/></select></label>
          <label>ЦФО для отбора<select value={draftFilters.costCenterId} disabled={catalog.loading || catalog.error} onChange={event => changeFilter('costCenterId', event.target.value)}><option value="">Все доступные ЦФО</option><ReferenceOptions items={catalog.items.costCenters}/></select></label>
          <label>Контрагент для отбора<select value={draftFilters.counterpartyId} disabled={catalog.loading || catalog.error} onChange={event => changeFilter('counterpartyId', event.target.value)}><option value="">Все доступные контрагенты</option><ReferenceOptions items={catalog.items.counterparties}/></select></label>
          <label>Статус заявки<select value={draftFilters.status} disabled={workspaceId === 'debtors'} onChange={event => changeFilter('status', event.target.value as ExpenseRequestStatus | '')}><option value="">Все статусы</option>{expenseRequestStatusCodes.map(status => <option key={status} value={status}>{expenseRequestStatusLabel(status)}</option>)}</select></label>
          <label>Плановая дата оплаты<input type="date" value={draftFilters.plannedDate} onChange={event => changeFilter('plannedDate', event.target.value)}/></label>
          <label>Срочность<select value={draftFilters.urgency} onChange={event => changeFilter('urgency', event.target.value as FinanceFilters['urgency'])}><option value="">Любая срочность</option><option value="urgent">Только срочные</option><option value="regular">Обычные</option></select></label>
        </div>
        <div className="finance-filter-actions"><button className="finance-primary" type="submit">Применить отбор</button><button type="button" onClick={reset}>Сбросить отбор</button>
        </div>
      </form>

      {isDebtors && byNames ? <DebtorsByName470 filters={filters} generation={generation}/> : <section className="finance-register" aria-label={definition.sourceLabel}>
        <div className="finance-register-tools"><div><h2>{definition.sourceLabel}</h2><span>{definition.draft ? 'Выберите доступную заявку, чтобы использовать её название в черновике.' : 'Выберите заявку для подробностей и подтверждённых платежей.'}</span></div>
          <label className="finance-search"><Search size={16}/><span className="sr-only">Поиск на загруженной странице</span><input value={search} onChange={event => setSearch(event.target.value)} maxLength={256} placeholder="Поиск на странице"/></label>
          <button type="button" disabled={data.loading} onClick={() => { setGeneration(value => value + 1); setSelected(null); }}><RefreshCw size={16}/>Обновить</button>
        </div>
        {data.loading && <div className="finance-empty" role="status"><span className="finance-loading-dot"/><h3>Загружаем заявки</h3><p>Ожидаем ответ сервера для текущего отбора.</p></div>}
        {data.failed && <div className="finance-error" role="alert"><strong>Не удалось загрузить заявки.</strong><p>Это ошибка загрузки, а не отсутствие задолженности или платежей.</p><button type="button" onClick={() => setGeneration(value => value + 1)}>Повторить загрузку заявок</button></div>}
        {page && <>
          <div className="finance-table-scroll" ref={region} tabIndex={0} role="region" aria-label="Таблица заявок-оснований">
            <table><caption>{definition.sourceLabel} — текущая страница</caption><thead><tr>
              <th scope="col">Заявка / назначение</th><th scope="col">Организация · ЦФО</th><th scope="col">Контрагент</th>
              <th scope="col">{workspaceId === 'debtors' ? 'Дата полной оплаты' : 'Плановая оплата'}</th>
              <th scope="col" className="finance-money">{workspaceId === 'debtors' ? 'Погашено обязательства' : 'Сумма заявки'}</th><th scope="col">Статус</th><th scope="col">Действие</th>
            </tr></thead><tbody>{items.map(item => <tr key={item.id} className={selectedItem?.id === item.id ? 'is-selected' : undefined}>
              <td><strong>{requestTitle(item)}</strong><small>{initiatorLabel(item)}</small>{item.isUrgent && <span className="finance-urgent">Срочная</span>}</td>
              <td>{lookup(names.organizations, item.organizationId)}<small>{lookup(names.costCenters, item.defaultCostCenterId)}</small></td>
              <td>{item.isNewCounterparty && item.candidateCounterpartyName ? item.candidateCounterpartyName : lookup(names.counterparties, item.counterpartyId)}</td>
              <td>{date(workspaceId === 'debtors' ? item.businessColumns?.fullyPaidDate : item.plannedPaymentDate)}</td>
              <td className="finance-money">{amount(workspaceId === 'debtors' ? item.businessColumns?.paidAmount : item.totalAmount)}<small>{item.currencyCode ?? 'Валюта не указана'}</small></td>
              <td><span className="finance-status">{expenseRequestStatusLabel(item.status)}</span>{workspaceId === 'debtors' && <small>{item.businessColumns?.closingState === 'partiallyClosed' ? 'Частично закрыта' : item.businessColumns?.closingState === 'closed' ? 'Закрытие зарегистрировано' : item.businessColumns?.closingState === 'notClosed' ? 'Не закрыта' : 'Закрытие не получено'}</small>}</td>
              <td><button type="button" disabled={!item.canOpen} onClick={() => select(item)} aria-label={`Выбрать заявку: ${requestTitle(item)}`}>{selectedItem?.id === item.id ? 'Выбрана' : 'Подробнее'}</button>{!item.canOpen && <small>Нет доступа к карточке</small>}</td>
            </tr>)}</tbody></table>
            {items.length === 0 && <div className="finance-empty" role="status"><FileSearch size={28}/><h3>{search ? 'На этой странице совпадений нет' : 'Заявок в выборке нет'}</h3><p>{search ? 'Поиск не охватывает остальные страницы. Измените поиск или используйте серверный отбор выше.' : 'Измените отбор или обновите данные. Эта выборка не является выпиской по всем банковским операциям.'}</p></div>}
          </div>
          <div className="finance-pagination"><span role="status">{page.items.length ? `${page.skip + 1}–${page.skip + page.items.length}` : '0'} из {integer.format(page.total)} заявок{search && ` · совпадений на странице: ${items.length}`}</span>
            {(page.total > page.take || skip > 0) && <nav aria-label="Страницы заявок-оснований"><button type="button" disabled={skip === 0 || data.loading} onClick={() => movePage(Math.max(0, skip - financePageSize))}><ChevronLeft size={16}/>Назад</button><button type="button" disabled={data.loading || page.skip + page.take >= page.total} onClick={() => movePage(skip + financePageSize)}>Далее<ChevronRight size={16}/></button></nav>}
          </div>
        </>}
      </section>}
      {!byNames && selectedItem && <section className="finance-selected" ref={detailsRegion} tabIndex={-1} aria-label="Выбранная заявка">
        <div className="finance-selected-heading"><div><span className="finance-eyebrow">Основание</span><h2>{requestTitle(selectedItem)}</h2></div><button type="button" onClick={() => setSelected(null)} aria-label="Закрыть сведения о заявке">Закрыть</button></div>
        <dl><div><dt>Сумма заявки</dt><dd>{amount(selectedItem.businessColumns?.obligationAmount ?? selectedItem.totalAmount)} {selectedItem.currencyCode}</dd></div><div><dt>Статус</dt><dd>{expenseRequestStatusLabel(selectedItem.status)}</dd></div><div><dt>Плановая дата оплаты</dt><dd>{date(selectedItem.plannedPaymentDate)}</dd></div></dl>
        <div className="finance-form-actions"><button type="button" className="finance-primary" onClick={() => onOpenRequest(selectedItem.id)}>Открыть карточку заявки <ArrowUpRight size={16}/></button>{definition.draft && <button type="button" onClick={() => setTab('preparation')}><CheckCircle2 size={16}/>Перейти к черновику с этим основанием</button>}</div>
        <ExpenseRequestPaymentDetailsPanel key={selectedItem.id} requestId={selectedItem.id}/>
      </section>}
    </>}
  </div>;
}
