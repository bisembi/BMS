import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { showsExpenseRequestActions, showsHideDrafts } from '../utils/activeRole';
import { FileSpreadsheet, Plus, RefreshCw, SlidersHorizontal } from 'lucide-react';
import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import { getCostCenters, getCounterparties, getExpenseRequestExport, getOrganizations, listExpenseRequests, copyCompleteExpenseRequest, deleteExpenseRequestDraft, getExpenseRequest } from '../api';
import { ExpenseRequestListFilters } from '../components/expense-request-list/ExpenseRequestListFilters';
import { ExpenseRequestListGrid } from '../components/expense-request-list/ExpenseRequestListGrid';
import { ExpenseRequestDeleteDialog } from '../components/expense-request-list/ExpenseRequestDeleteDialog';
import type { ExpenseRequestListSort } from '../utils/expenseRequestListSort';
import { listExpenseRequestInitiators, type ExpenseRequestInitiatorOption } from '../api/expenseRequestInitiators';
import { ExpenseRequestListPagination, type ExpenseRequestPageSize } from '../components/expense-request-list/ExpenseRequestListPagination';
import { countActiveExpenseRequestListFilters, createdPeriodArguments, emptyExpenseRequestListFilters, referenceToken, resolveInitiatorId, initiatorToken, toExpenseRequestListQuery, validateExpenseRequestListFilters, type ExpenseRequestListCatalogs, type ExpenseRequestListFilterState, type ExpenseRequestListQuery } from '../utils/expenseRequestListFilters';
import { expenseRequestExportErrorMessage, expenseRequestListErrorMessage, expenseRequestCopyErrorMessage } from '../utils/uiErrorMessages';
import type { ExpenseRequestListItem, ReferenceDataItem, SavedExpenseRequest } from '../types';
import { expenseRequestListView, type ExpenseRequestListSnapshot } from '../utils/expenseRequestListView354';
type Props = {
    activeRoleCode?: string | null;
    reloadToken: number;
    onCreate: () => void;
    onOpen: (id: string) => void;
    onOpenPaymentRegister?: () => void;
    canCreate: boolean;
    initialOnlyMyTasks?: boolean;
    currentActorExternalId?: string | null;
    onCopied?: (copy: SavedExpenseRequest) => void;
    onNavigationLockChange?: (locked: boolean) => void;
};
function listWithQuery(skip: number, take: number, query: ExpenseRequestListQuery, sort?: ExpenseRequestListSort, hideDrafts = false) {
    const filters = [
        query.isUrgent,
        query.plannedPaymentDate,
        query.organizationId,
        query.status,
        query.costCenterId,
        query.counterpartyId,
        query.initiatorExternalId,
    ] as const;
    // The same bounded server read supplies confirmed settlement dates. An
    // explicitly offline demo has no payment ledger, so it keeps unknown dates.
    const confirmedDates = !(import.meta.env.DEV && import.meta.env.VITE_USE_MOCK_API === 'true');
    const read = (...args: Parameters<typeof listExpenseRequests>) => {
        if (!confirmedDates)
            return listExpenseRequests(...args);
        const withDates: Parameters<typeof listExpenseRequests> = [...args];
        withDates[13] = true;
        return listExpenseRequests(...withDates);
    };
    if (hideDrafts)
        return read(skip, take, ...filters, query.createdFromUtc, query.createdBeforeUtc, query.onlyMyTasks, undefined, false, sort, query.search, query.initiatorEmployeeId, true);
    if (query.initiatorEmployeeId)
        return read(skip, take, ...filters, query.createdFromUtc, query.createdBeforeUtc, query.onlyMyTasks, undefined, false, sort, query.search, query.initiatorEmployeeId);
    if (query.search)
        return read(skip, take, ...filters, query.createdFromUtc, query.createdBeforeUtc, query.onlyMyTasks, undefined, false, sort, query.search);
    if (sort)
        return read(skip, take, ...filters, query.createdFromUtc, query.createdBeforeUtc, query.onlyMyTasks, undefined, false, sort);
    return query.onlyMyTasks === true
        ? read(skip, take, ...filters, query.createdFromUtc, query.createdBeforeUtc, true)
        : read(skip, take, ...filters, ...createdPeriodArguments(query));
}
function exportWithQuery(query: ExpenseRequestListQuery, hideDrafts = false) {
    const filters = [
        query.isUrgent,
        query.plannedPaymentDate,
        query.organizationId,
        query.status,
        query.costCenterId,
        query.counterpartyId,
        query.initiatorExternalId,
    ] as const;
    if (hideDrafts)
        return getExpenseRequestExport(...filters, query.createdFromUtc, query.createdBeforeUtc, query.onlyMyTasks, query.search, query.initiatorEmployeeId, true);
    if (query.initiatorEmployeeId)
        return getExpenseRequestExport(...filters, query.createdFromUtc, query.createdBeforeUtc, query.onlyMyTasks, query.search, query.initiatorEmployeeId);
    if (query.search)
        return getExpenseRequestExport(...filters, query.createdFromUtc, query.createdBeforeUtc, query.onlyMyTasks, query.search);
    return query.onlyMyTasks === true
        ? getExpenseRequestExport(...filters, query.createdFromUtc, query.createdBeforeUtc, true)
        : getExpenseRequestExport(...filters, ...createdPeriodArguments(query));
}
function refreshedReferenceChoices(previous: readonly ReferenceDataItem[], fresh: readonly ReferenceDataItem[], selectedTokens: readonly string[]) {
    const items = [...fresh];
    const tokens = new Map<string, string>();
    for (const old of previous.filter(item => selectedTokens.includes(referenceToken(item)))) {
        let current = items.find(item => item.id.toLowerCase() === old.id.toLowerCase());
        if (!current) {
            current = { ...old, name: old.name.includes(' (нет в обновлённом справочнике)') ? old.name : `${old.name} (нет в обновлённом справочнике)` };
            items.push(current);
        }
        tokens.set(referenceToken(old), referenceToken(current));
    }
    return { items, tokens };
}
export function ExpenseRequestListPage({ activeRoleCode, reloadToken, onCreate, onOpen, onOpenPaymentRegister, canCreate, currentActorExternalId = null, onCopied, onNavigationLockChange, }: Props) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [take, setTake] = useState<ExpenseRequestPageSize>(50);
    const showHideDrafts = showsHideDrafts(activeRoleCode);
    const [hideDrafts, setHideDrafts] = useState(true);
    const effectiveHideDrafts = showHideDrafts && hideDrafts;
    // Legacy embedding props must not create an invisible removed filter.
    const initialFilters = emptyExpenseRequestListFilters;
    const [skip, setSkip] = useState(0);
    const [actionBusy, setActionBusy] = useState(false);
    const actionInFlight = useRef(false);
    const navigationLock = useRef(onNavigationLockChange);
    navigationLock.current = onNavigationLockChange;
    useEffect(() => () => navigationLock.current?.(false), []);
    const [deleteTarget, setDeleteTarget] = useState<{
        id: string;
        number: string;
        version: string;
    } | null>(null);
    const [deleteError, setDeleteError] = useState<string | null>(null);
    const canMutate = (item: ExpenseRequestListItem) => item.canOpen && canCreate
        && currentActorExternalId !== null && item.initiatorExternalId === currentActorExternalId;
    const canCopy = (item: ExpenseRequestListItem) => item.canOpen && canCreate
        && currentActorExternalId !== null;
    const [sort, setSort] = useState<ExpenseRequestListSort>();
    const [snapshot, setSnapshot] = useState<ExpenseRequestListSnapshot | null>(null);
    // Two bounded first-page snapshots, local to this mounted list, never persisted.
    const firstPages = useRef<ExpenseRequestListSnapshot[]>([]);
    const [refreshToken, setRefreshToken] = useState(0);
    const [draftFilters, setDraftFilters] = useState<ExpenseRequestListFilterState>(initialFilters);
    const [appliedFilters, setAppliedFilters] = useState<ExpenseRequestListFilterState>(initialFilters);
    const [appliedQuery, setAppliedQuery] = useState<ExpenseRequestListQuery>(() => toExpenseRequestListQuery(initialFilters, { organizations: [], costCenters: [], counterparties: [], initiators: [] }));
    const [applyToken, setApplyToken] = useState(0);
    const [filterError, setFilterError] = useState<string | null>(null);
    const [organizations, setOrganizations] = useState<ReferenceDataItem[]>([]);
    const [costCenters, setCostCenters] = useState<ReferenceDataItem[]>([]);
    const [counterparties, setCounterparties] = useState<ReferenceDataItem[]>([]);
    const [initiators, setInitiators] = useState<ExpenseRequestInitiatorOption[]>([]);
    const [initiatorError, setInitiatorError] = useState<string | null>(null);
    const [initiatorLoading, setInitiatorLoading] = useState(true);
    const [organizationError, setOrganizationError] = useState<string | null>(null);
    const [costCenterError, setCostCenterError] = useState<string | null>(null);
    const [counterpartyError, setCounterpartyError] = useState<string | null>(null);
    const [organizationLoading, setOrganizationLoading] = useState(true);
    const [counterpartyLoading, setCounterpartyLoading] = useState(true);
    const [costCenterLoading, setCostCenterLoading] = useState(true);
    const [loading, setLoading] = useState(true);
    const [exporting, setExporting] = useState(false);
    const [filtersExpanded, setFiltersExpanded] = useState(false);
    const filtersPanelId = useId();
    const [error, setError] = useState<string | null>(null);
    const [loadFailed, setLoadFailed] = useState(false);
    const requestSequence = useRef(0);
    const listContext = JSON.stringify([activeRoleCode, currentActorExternalId, canCreate,
        appliedQuery, sort, applyToken, reloadToken, workspaceRefresh, take]);
    const requestedView = JSON.stringify([listContext, skip, effectiveHideDrafts, refreshToken]);
    const latestView = useRef(requestedView);
    latestView.current = requestedView;
    const { page: visiblePage, totalKnown } = expenseRequestListView(snapshot, firstPages.current, listContext, skip, take, effectiveHideDrafts);
    const { items, total } = visiblePage;
    const budgetIndicatorsUnavailable = visiblePage.budgetIndicatorsUnavailable === true;
    const listLoading = loading || snapshot?.context !== listContext
        || snapshot.page.skip !== skip || snapshot.page.take !== take || snapshot.hideDrafts !== effectiveHideDrafts;
    const catalogsRef = useRef<ExpenseRequestListCatalogs>({
        organizations: [],
        costCenters: [],
        counterparties: [],
        initiators: [],
    });
    const liveFilters = useRef({ draft: draftFilters, applied: appliedFilters });
    liveFilters.current = { draft: draftFilters, applied: appliedFilters };
    function acceptCatalog(field: 'organizations' | 'costCenters' | 'counterparties', token: 'organizationToken' | 'costCenterToken' | 'counterpartyToken', fresh: ReferenceDataItem[]) {
        const choices = refreshedReferenceChoices(catalogsRef.current[field], fresh, [liveFilters.current.draft[token], liveFilters.current.applied[token]]);
        catalogsRef.current = { ...catalogsRef.current, [field]: choices.items };
        setDraftFilters(current => ({ ...current, [token]: choices.tokens.get(current[token]) ?? current[token] }));
        setAppliedFilters(current => ({ ...current, [token]: choices.tokens.get(current[token]) ?? current[token] }));
        return choices.items;
    }
    const organizationNames = useMemo(
    // The organization catalogue Code is the firm's maintained prefix (ALM, RM, ...).
    // Never derive an abbreviation from the legal name or fall back to a raw UUID.
    () => new Map(organizations.map((item) => [item.id, item.code.trim() || '—'])), [organizations]);
    const costCenterNames = useMemo(() => new Map(costCenters.map((item) => [item.id, item.name])), [costCenters]);
    const counterpartyNames = useMemo(() => new Map(counterparties.map((item) => [item.id, item.name])), [counterparties]);
    useWorkspaceRefreshBusy(listLoading || exporting || organizationLoading || counterpartyLoading || costCenterLoading || initiatorLoading);
    useEffect(() => {
        let cancelled = false;
        setInitiatorLoading(true);
        void listExpenseRequestInitiators().then(fresh => {
            if (cancelled)
                return;
            const selected = [liveFilters.current.draft.initiator, liveFilters.current.applied.initiator];
            const retained = (catalogsRef.current.initiators ?? []).filter(employee => selected.includes(initiatorToken(employee))
                && !fresh.some(item => item.id.toLowerCase() === employee.id.toLowerCase()));
            const choices = [...fresh, ...retained];
            catalogsRef.current = { ...catalogsRef.current, initiators: choices };
            setInitiators(choices);
            setInitiatorError(null);
        }).catch(() => {
            if (!cancelled)
                setInitiatorError('Не удалось загрузить список сотрудников. Обновите страницу.');
        }).finally(() => {
            if (!cancelled)
                setInitiatorLoading(false);
        });
        return () => { cancelled = true; };
    }, [workspaceRefresh]);
    useEffect(() => {
        let cancelled = false;
        setOrganizationLoading(true);
        void getOrganizations()
            .then((catalog) => {
            if (cancelled)
                return;
            setOrganizations(acceptCatalog('organizations', 'organizationToken', catalog.items));
            setOrganizationError(null);
        })
            .catch(() => {
            if (cancelled)
                return;
            if (workspaceRefresh === 0) {
                catalogsRef.current = { ...catalogsRef.current, organizations: [] };
                setOrganizations([]);
            }
            setOrganizationError(workspaceRefresh === 0 ? 'Справочник организаций временно недоступен. Идентификаторы скрыты.'
                : 'Не удалось обновить справочник организаций. Прежний список и текущий отбор сохранены.');
        }).finally(() => {
            if (!cancelled)
                setOrganizationLoading(false);
        });
        return () => {
            cancelled = true;
        };
    }, [workspaceRefresh]);
    useEffect(() => {
        let cancelled = false;
        setCounterpartyLoading(true);
        void getCounterparties()
            .then((catalog) => {
            if (cancelled)
                return;
            setCounterparties(acceptCatalog('counterparties', 'counterpartyToken', catalog.items));
            setCounterpartyError(null);
        })
            .catch(() => {
            if (cancelled)
                return;
            if (workspaceRefresh === 0) {
                catalogsRef.current = { ...catalogsRef.current, counterparties: [] };
                setCounterparties([]);
            }
            setCounterpartyError(workspaceRefresh === 0 ? 'Справочник контрагентов временно недоступен. Идентификаторы скрыты.'
                : 'Не удалось обновить справочник контрагентов. Прежний список и текущий отбор сохранены.');
        }).finally(() => {
            if (!cancelled)
                setCounterpartyLoading(false);
        });
        return () => {
            cancelled = true;
        };
    }, [workspaceRefresh]);
    useEffect(() => {
        let cancelled = false;
        setCostCenterLoading(true);
        void getCostCenters()
            .then((catalog) => {
            if (cancelled)
                return;
            setCostCenters(acceptCatalog('costCenters', 'costCenterToken', catalog.items));
            setCostCenterError(null);
        })
            .catch(() => {
            if (cancelled)
                return;
            if (workspaceRefresh === 0) {
                catalogsRef.current = { ...catalogsRef.current, costCenters: [] };
                setCostCenters([]);
            }
            setCostCenterError(workspaceRefresh === 0 ? 'Справочник ЦФО временно недоступен. Идентификаторы скрыты.'
                : 'Не удалось обновить справочник ЦФО. Прежний список и текущий отбор сохранены.');
        }).finally(() => {
            if (!cancelled)
                setCostCenterLoading(false);
        });
        return () => {
            cancelled = true;
        };
    }, [workspaceRefresh]);
    const load = useCallback(async () => {
        const sequence = ++requestSequence.current;
        setLoading(true);
        setLoadFailed(false);
        setError(null);
        try {
            const page = await listWithQuery(skip, take, appliedQuery, sort, effectiveHideDrafts);
            if (sequence !== requestSequence.current || latestView.current !== requestedView)
                return;
            // Cache identity follows the requested page, just like the list's pager.
            const fresh = { context: listContext, hideDrafts: effectiveHideDrafts, page: { ...page, skip, take } };
            if (skip === 0)
                firstPages.current = [fresh, ...firstPages.current.filter(entry => entry.context === listContext && entry.hideDrafts !== effectiveHideDrafts)].slice(0, 2);
            setSnapshot(fresh);
        }
        catch {
            if (sequence !== requestSequence.current || latestView.current !== requestedView)
                return;
            setError(expenseRequestListErrorMessage());
            firstPages.current = [];
            setSnapshot({ context: listContext, hideDrafts: effectiveHideDrafts,
                page: { items: [], skip, take, total: 0 } });
            setLoadFailed(true);
        }
        finally {
            if (sequence === requestSequence.current && latestView.current === requestedView)
                setLoading(false);
        }
    }, [appliedQuery, skip, take, sort, effectiveHideDrafts, listContext, requestedView]);
    const refreshList = () => {
        firstPages.current = [];
        setRefreshToken(token => token + 1);
    };
    useEffect(() => {
        void load();
        return () => { requestSequence.current += 1; };
    }, [applyToken, load, reloadToken, workspaceRefresh]);
    const copyRow = async (item: ExpenseRequestListItem) => {
        if (!canCopy(item) || actionInFlight.current || deleteTarget)
            return;
        actionInFlight.current = true;
        setActionBusy(true);
        setError(null);
        navigationLock.current?.(true);
        try {
            const copied = await copyCompleteExpenseRequest(item.id);
            if (onCopied)
                onCopied(copied);
            else
                onOpen(copied.id);
            refreshList();
        }
        catch (failure) {
            setError(expenseRequestCopyErrorMessage(failure));
        }
        finally {
            actionInFlight.current = false;
            setActionBusy(false);
            navigationLock.current?.(false);
        }
    };
    const prepareDelete = async (item: ExpenseRequestListItem) => {
        if (!canMutate(item) || item.status !== 'DRAFT' || actionInFlight.current || deleteTarget)
            return;
        actionInFlight.current = true;
        setActionBusy(true);
        setError(null);
        navigationLock.current?.(true);
        let prepared = false;
        try {
            const current = await getExpenseRequest(item.id);
            if (current.status !== 'DRAFT' || current.initiatorExternalId !== currentActorExternalId
                || current.editAuthority?.canEdit !== true) {
                setError('Удалить можно только свой доступный для изменения черновик. Обновите список.');
                return;
            }
            setDeleteError(null);
            setDeleteTarget({ id: item.id, number: item.requestNumber ?? 'без номера', version: current.concurrencyStamp });
            prepared = true;
        }
        catch {
            setError('Не удалось проверить черновик перед удалением. Обновите список и повторите попытку.');
        }
        finally {
            actionInFlight.current = false;
            setActionBusy(false);
            if (!prepared)
                navigationLock.current?.(false);
        }
    };
    const confirmDelete = async () => {
        if (!deleteTarget || actionInFlight.current)
            return;
        actionInFlight.current = true;
        setActionBusy(true);
        setDeleteError(null);
        try {
            await deleteExpenseRequestDraft(deleteTarget.id, deleteTarget.version);
            requestSequence.current += 1;
            firstPages.current = [];
            setRefreshToken(token => token + 1);
            setDeleteTarget(null);
            navigationLock.current?.(false);
            if (items.length === 1 && skip > 0)
                setSkip(Math.max(0, skip - take));
        }
        catch {
            setDeleteError('Удаление не подтверждено. Если черновик изменён или отправлен на согласование, закройте окно и обновите список. Иначе повторите попытку.');
        }
        finally {
            actionInFlight.current = false;
            setActionBusy(false);
        }
    };
    const applyFilters = () => {
        if (draftFilters.initiator && !resolveInitiatorId(draftFilters.initiator, catalogsRef.current.initiators ?? [])) {
            setFilterError('Выберите инициатора из списка сотрудников.');
            return;
        }
        const validationMessage = validateExpenseRequestListFilters(draftFilters);
        if (validationMessage !== null) {
            setFilterError(validationMessage);
            return;
        }
        setFilterError(null);
        setAppliedFilters(draftFilters);
        setAppliedQuery(toExpenseRequestListQuery(draftFilters, catalogsRef.current));
        setSkip(0);
        setApplyToken((token) => token + 1);
    };
    const resetFilters = () => {
        setFilterError(null);
        setDraftFilters(emptyExpenseRequestListFilters);
        setAppliedFilters(emptyExpenseRequestListFilters);
        setAppliedQuery(toExpenseRequestListQuery(emptyExpenseRequestListFilters, catalogsRef.current));
        setSkip(0);
        setApplyToken((token) => token + 1);
    };
    const updateDraftFilters = (patch: Partial<ExpenseRequestListFilterState>) => {
        setDraftFilters((current) => ({ ...current, ...patch }));
    };
    const activeFilterCount = countActiveExpenseRequestListFilters(appliedFilters);
    const exportExcel = async () => {
        setExporting(true);
        setError(null);
        try {
            const download = await exportWithQuery(appliedQuery, effectiveHideDrafts);
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
            setError(expenseRequestExportErrorMessage(error));
        }
        finally {
            setExporting(false);
        }
    };
    return (<div className="list-page">
      <div className="list-heading">
        <div>
          <h1>Расходные заявки</h1>
        </div>
      </div>

      <div className="list-toolbar expense-request-list-toolbar">
        {canCreate && (<button className="primary-button" type="button" onClick={onCreate}><Plus size={16}/>Создать</button>)}
        <button className="secondary-button compact" type="button" onClick={refreshList} disabled={listLoading}>
          <RefreshCw size={15}/>Обновить
        </button>
        <button className="secondary-button compact" type="button" aria-expanded={filtersExpanded} aria-controls={filtersPanelId} onClick={() => setFiltersExpanded(current => !current)}>
          <SlidersHorizontal size={15} aria-hidden="true"/>
          {filtersExpanded ? 'Свернуть фильтры' : 'Фильтры'}
        </button>
        <button className="secondary-button compact" type="button" onClick={() => void exportExcel()} disabled={exporting}>
          <FileSpreadsheet size={15}/>{exporting ? 'Формирование…' : 'Excel'}
        </button>
        {activeFilterCount > 0 && <span className="list-filter-count" aria-live="polite">
          Применено фильтров: {activeFilterCount}
        </span>}
        <span className="list-toolbar-spacer"/>
        {showHideDrafts && <label className="list-filter-field list-filter-checkbox request-hide-drafts" title="Скрывает черновики и отменённые заявки. Удалённые заявки не показываются.">
          <input type="checkbox" checked={hideDrafts} disabled={actionBusy || exporting} onChange={event => { setSkip(0); setHideDrafts(event.target.checked); }}/>
          Скрыть черновики
        </label>}
        {onOpenPaymentRegister && <button type="button" className="secondary-button compact" onClick={onOpenPaymentRegister}>Выгрузить реестр</button>}
        <ExpenseRequestListPagination skip={skip} take={take} total={total} itemCount={items.length} loading={listLoading} totalPending={!totalKnown} onSkipChange={setSkip} onTakeChange={next => {
            if (next !== take) {
                firstPages.current = [];
                setSkip(0);
                setTake(next);
            }
        }} sizeDisabled={actionBusy || exporting}/>
      </div>

      <ExpenseRequestListFilters disclosure={{ expanded: filtersExpanded, panelId: filtersPanelId }} draft={draftFilters} onChange={updateDraftFilters} onApply={applyFilters} onReset={resetFilters} organizations={organizations} costCenters={costCenters} counterparties={counterparties} initiators={initiators} activeFilterCount={activeFilterCount} error={filterError}/>

      {error && <div className="notice error" role="alert">{error}</div>}
      {budgetIndicatorsUnavailable && <div className="notice warning" role="status">
        Данные ДДС не актуализированы. Проверка бюджета и сортировка по бюджету временно недоступны.
        {sort?.by === 'budget' && ' Заявки показаны в обычном порядке.'}
        {' Суммы и статусы заявок сохранены. Финансовые операции требуют актуальных данных.'}
      </div>}
      {organizationError && <div className="notice error" role="status">{organizationError}</div>}
      {costCenterError && <div className="notice error" role="status">{costCenterError}</div>}
      {counterpartyError && <div className="notice error" role="status">{counterpartyError}</div>}
      {initiatorError && <div className="notice error" role="status">{initiatorError}</div>}

      <ExpenseRequestListGrid showActions={showsExpenseRequestActions(activeRoleCode)} onCopy={item => void copyRow(item)} onDelete={item => void prepareDelete(item)} canMutate={canMutate} canCopy={canCopy} actionsBusy={actionBusy || deleteTarget !== null || listLoading} sort={sort ?? { by: 'requestDate', direction: 'desc' }} onSort={(by) => {
            setSkip(0);
            setSort(current => ({ by, direction: current?.by === by && current.direction === 'asc' ? 'desc' : 'asc' }));
        }} items={items} loading={listLoading} organizationNames={organizationNames} costCenterNames={costCenterNames} counterpartyNames={counterpartyNames} onOpen={onOpen} failed={loadFailed}/>
      {deleteTarget && <ExpenseRequestDeleteDialog number={deleteTarget.number} busy={actionBusy} error={deleteError} onConfirm={() => void confirmDelete()} onCancel={() => {
                if (!actionInFlight.current) {
                    setDeleteTarget(null);
                    navigationLock.current?.(false);
                }
            }}/>}
      <div className="list-summary">{loadFailed ? 'Список временно недоступен.'
            : `Показано на странице: ${items.length}. Всего доступно: ${totalKnown ? total : 'уточняется'}.`}</div>
    </div>);
}
