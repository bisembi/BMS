import { useWorkspaceRefreshRevision, useWorkspaceRefreshGuard, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { CashFlowDirectoryGrid } from '../components/reference-directory/CashFlowDirectoryGrid';
import { CashFlowStructurePreview } from '../components/reference-directory/CashFlowStructurePreview';
import { getCostCenters, getExpenseItems } from '../api/referenceData';
import { ReferenceDirectoryError } from '../components/reference-directory/ReferenceDirectoryError';
import { ReferenceDirectoryPager } from '../components/reference-directory/ReferenceDirectoryPager';
import { ReferenceDirectorySearchToolbar } from '../components/reference-directory/ReferenceDirectorySearchToolbar';
import { emptyMessages } from '../components/reference-directory/referenceDirectoryText';
import { useCashFlowItemCatalogue } from '../hooks/useCashFlowItemCatalogue';
import { useCashFlowDirectoryEditor } from '../hooks/useCashFlowDirectoryEditor';
import { useFrozenCreate } from '../hooks/useFrozenCreate';
import { useCashFlowOrder } from '../hooks/useCashFlowOrder';
import '../styles/cash-flow-catalog-order.css';
import { isReferenceDataCreateUncertain } from '../api/referenceDataMaintenanceErrors';
import { REFERENCE_DIRECTORIES } from '../navigation';
import { createReferenceDataItem, deleteReferenceDataItem, referenceDataCreateErrorMessage, referenceDataDeleteErrorMessage, type ReferenceDataMaintenanceInput } from '../api';
import { CASH_FLOW_ACTIVITY_TYPES } from '../types';
import type { CashFlowActivityType, ReferenceDataItem } from '../types';
import { getCashFlowOrder, type CashFlowOrder } from '../api/cashFlowOrder';
import { directoryGroupKey, missingDirectoryValue } from '../hooks/cashFlowDirectoryFilters';
/// Server-filtered cash-flow items, with an explicit UI page-size choice.
export function CashFlowItemDirectoryPage({ canManage = false, onNavigationLockChange }: {
    canManage?: boolean;
    onNavigationLockChange?: (locked: boolean) => void;
}) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const definition = REFERENCE_DIRECTORIES['cash-flow-items'];
    const [centerFilter, setCenterFilter] = useState('');
    const [groupFilter, setGroupFilter] = useState('');
    const [centerFilterCaption, setCenterFilterCaption] = useState('');
    const [groupFilterCaption, setGroupFilterCaption] = useState('');
    const [filterSnapshot, setFilterSnapshot] = useState<CashFlowOrder | null>(null);
    const filterItems = filterSnapshot?.items ?? null;
    const [filtersLoading, setFiltersLoading] = useState(true);
    const [filtersError, setFiltersError] = useState(false);
    const [filtersRevision, setFiltersRevision] = useState(0);
    useEffect(() => {
        const controller = new AbortController();
        setFiltersError(false);
        setFiltersLoading(true);
        void getCashFlowOrder(controller.signal).then(value => {
            if (!controller.signal.aborted)
                setFilterSnapshot(value);
        }).catch(() => {
            if (!controller.signal.aborted)
                setFiltersError(true);
        })
            .finally(() => {
            if (!controller.signal.aborted)
                setFiltersLoading(false);
        });
        return () => controller.abort();
    }, [workspaceRefresh, filtersRevision]);
    const [editingCatalogue, setEditingCatalogue] = useState(false);
    const [structureOpen, setStructureOpen] = useState(false);
    const [structureRevision, setStructureRevision] = useState(0);
    function reloadCommittedCatalogue() {
        setStructureRevision(value => value + 1);
        setFiltersRevision(value => value + 1);
        if (order.active)
            void order.load();
        return catalogue.reload();
    }
    const [editorMode, setEditorMode] = useState<'create' | null>(null);
    const [editorName, setEditorName] = useState('');
    const [editorFlowDirection, setEditorFlowDirection] = useState<'Payment' | 'Receipt' | ''>('');
    const [editorGroupName, setEditorGroupName] = useState('');
    const [editorActivityType, setEditorActivityType] = useState<CashFlowActivityType>('Operating');
    const [editorError, setEditorError] = useState<string | null>(null);
    const [editorSaving, setEditorSaving] = useState(false);
    const createCommand = useFrozenCreate<ReferenceDataMaintenanceInput>('cash-flow-create', isReferenceDataCreateUncertain);
    const deletionInFlight = useRef(false);
    const orderReadInFlight = useRef(false);
    const assignmentLock = useRef(false);
    const [hasPending, setHasPending] = useState(false);
    const notifyLock = useRef(onNavigationLockChange);
    notifyLock.current = onNavigationLockChange;
    useEffect(() => {
        notifyLock.current?.(hasPending);
        const beforeUnload = (event: BeforeUnloadEvent) => {
            if (assignmentLock.current) {
                event.preventDefault();
                event.returnValue = '';
            }
        };
        window.addEventListener('beforeunload', beforeUnload);
        return () => window.removeEventListener('beforeunload', beforeUnload);
    }, [hasPending]);
    useEffect(() => () => notifyLock.current?.(false), []);
    const rowEditor = useCashFlowDirectoryEditor(() => {
        if (!canManage || editorSaving || editorMode !== null || orderReadInFlight.current
            || order.loading || order.blocked || order.busy || catalogue.loading || catalogue.error !== null)
            return false;
        assignmentLock.current = true;
        setHasPending(true);
        notifyLock.current?.(true);
        return true;
    }, () => { assignmentLock.current = false; setHasPending(false); notifyLock.current?.(false); }, reloadCommittedCatalogue);
    const order = useCashFlowOrder(() => {
        if (!canManage || !editingCatalogue || editorSaving || editorMode !== null || rowEditor.saving || rowEditor.frozen || rowEditor.hasProblems)
            return false;
        assignmentLock.current = true;
        setHasPending(true);
        notifyLock.current?.(true);
        return true;
    }, () => {
        const pending = rowEditor.count > 0;
        assignmentLock.current = pending;
        setHasPending(pending);
        notifyLock.current?.(pending);
        setStructureRevision(value => value + 1);
        setFiltersRevision(value => value + 1);
        catalogue.reload();
    });
    const orderItems = order.active ? order.snapshot?.items ?? null : null;
    const catalogue = useCashFlowItemCatalogue(orderItems ?? (structureOpen || centerFilter || groupFilter ? filterItems : null), centerFilter, groupFilter);
    const filterChoices = useMemo(() => {
        const centers = new Map<string, string>(), groups = new Map<string, string>();
        for (const item of orderItems ?? filterItems ?? []) {
            centers.set(item.costCenterId?.toLowerCase() || missingDirectoryValue, item.costCenterName || 'Без ЦФО');
            groups.set(directoryGroupKey(item), item.parentName?.trim() || 'Без группы');
        }
        return { centers: [...centers].sort((a, b) => a[1].localeCompare(b[1], 'ru')),
            groups: [...groups].sort((a, b) => a[1].localeCompare(b[1], 'ru')) };
    }, [filterItems, orderItems]);
    useEffect(() => {
        if (order.active && !assignmentLock.current && !rowEditor.count && !rowEditor.saving
            && !editorSaving && editorMode === null && !order.blocked)
            void order.load();
    }, [workspaceRefresh]);
    const [costCenters, setCostCenters] = useState<ReferenceDataItem[]>([]);
    const [expenseItems, setExpenseItems] = useState<ReferenceDataItem[]>([]);
    const [centersLoading, setCentersLoading] = useState(true);
    const [centersError, setCentersError] = useState(false);
    const [centersRevision, setCentersRevision] = useState(0);
    useEffect(() => {
        let active = true;
        if (!canManage || !editingCatalogue) {
            setCentersLoading(false);
            return () => { active = false; };
        }
        setCentersLoading(true);
        setCentersError(false);
        void Promise.all([getCostCenters(), getExpenseItems()]).then(([centers, expenses]) => {
            if (active) {
                setCostCenters(centers.items);
                setExpenseItems(expenses.items);
            }
        })
            .catch(() => {
            if (active) {
                setCostCenters([]);
                setExpenseItems([]);
                setCentersError(true);
            }
        })
            .finally(() => {
            if (active)
                setCentersLoading(false);
        });
        return () => { active = false; };
    }, [canManage, editingCatalogue, centersRevision, workspaceRefresh]);
    useWorkspaceRefreshGuard(hasPending || rowEditor.count > 0 || rowEditor.saving || editorSaving || editorMode !== null || createCommand.blocked || order.loading
        ? 'Сначала сохраните или отмените правки статей ДДС, включая строки на других страницах.' : null);
    useWorkspaceRefreshBusy(catalogue.loading || centersLoading || order.loading || order.busy);
    const showTable = !catalogue.loading && catalogue.error === null && catalogue.items.length > 0;
    const showEmpty = !catalogue.loading && catalogue.error === null && catalogue.items.length === 0;
    function toggleCatalogueEditing() {
        if (order.loading || order.blocked || orderReadInFlight.current)
            return;
        if (createCommand.blocked)
            return;
        if (editorSaving || rowEditor.saving || editorMode !== null)
            return;
        if (editingCatalogue && rowEditor.count > 0) {
            if (!rowEditor.canDiscard || !window.confirm('Завершить редактирование без сохранения? Все несохранённые изменения, включая строки на других страницах и скрытые поиском, будут потеряны.'))
                return;
            if (!rowEditor.discard())
                return;
            void catalogue.reload();
        }
        else if (!editingCatalogue && (!canManage || catalogue.loading || catalogue.error !== null
            || catalogue.query.trim() !== catalogue.appliedQuery))
            return;
        if (editingCatalogue) {
            if (!order.finish())
                return;
        }
        else {
            orderReadInFlight.current = true;
            void order.load().finally(() => { orderReadInFlight.current = false; });
        }
        setEditingCatalogue(value => !value);
        setEditorError(null);
    }
    function toggleOrderEditing() {
        // Recheck ownership and local locks at action time, not just in disabled UI.
        if (!canManage || !editingCatalogue || orderReadInFlight.current || order.loading || order.blocked
            || assignmentLock.current || deletionInFlight.current || rowEditor.count > 0 || rowEditor.saving
            || rowEditor.frozen || editorSaving || editorMode !== null || createCommand.blocked
            || catalogue.loading || catalogue.error !== null || catalogue.query.trim() !== catalogue.appliedQuery)
            return;
        orderReadInFlight.current = true;
        void order.load().finally(() => { orderReadInFlight.current = false; });
    }
    function openCreate() {
        if (order.loading || order.blocked)
            return;
        if (createCommand.blocked)
            return;
        if (!canManage || editorSaving || assignmentLock.current)
            return;
        setEditorMode('create');
        setEditorName('');
        setEditorGroupName('');
        setEditorFlowDirection('');
        setEditorActivityType('Operating');
        setEditorError(null);
    }
    async function submitEditor(event: FormEvent) {
        event.preventDefault();
        if (!canManage || editorMode === null || editorSaving || assignmentLock.current)
            return;
        const input: ReferenceDataMaintenanceInput = {
            name: editorName.trim(),
            flowDirection: editorFlowDirection || null, flowDirectionSpecified: true,
            parentName: editorGroupName.trim() || null,
            activityType: editorActivityType,
            parentIdSpecified: true,
        };
        setEditorError(null);
        const result = await createCommand.run(input, (body, key) => createReferenceDataItem('cash-flow-items', body, key));
        if (result.kind === 'confirmed') {
            setEditorMode(null);
            await reloadCommittedCatalogue();
        }
        else if (result.kind !== 'inactive')
            setEditorError(result.kind === 'uncertain'
                ? 'Результат не подтверждён. Повторите сохранение: будут отправлены исходные данные с тем же ключом.'
                : referenceDataCreateErrorMessage(result.error));
    }
    async function deleteItem(item: ReferenceDataItem) {
        if (order.loading || order.blocked)
            return;
        if (!canManage || !editingCatalogue || editorMode !== null || editorSaving || deletionInFlight.current || assignmentLock.current)
            return;
        deletionInFlight.current = true;
        setEditorSaving(true);
        setEditorError(null);
        try {
            await deleteReferenceDataItem('cash-flow-items', item.id);
            await reloadCommittedCatalogue();
        }
        catch (error) {
            setEditorError(referenceDataDeleteErrorMessage(error));
        }
        finally {
            deletionInFlight.current = false;
            setEditorSaving(false);
        }
    }
    return (<div className="directory-page">
      <h1>{definition.title}</h1>
      <div className="directory-actions cash-flow-structure-toolbar">
        <button type="button" aria-expanded={structureOpen} aria-controls="cash-flow-structure-container" disabled={!structureOpen && (hasPending || rowEditor.count > 0 || rowEditor.saving || editorSaving || editorMode !== null
            || createCommand.blocked || order.loading || order.busy || order.blocked)} onClick={() => setStructureOpen(value => !value)}>
          {structureOpen ? 'Скрыть структуру отчета' : 'Показать структуру отчета'}</button>
      </div>
      {structureOpen && <div id="cash-flow-structure-container"><CashFlowStructurePreview refreshRevision={workspaceRefresh} catalogueRevision={structureRevision} orderVersion={order.snapshot?.version} onClose={() => setStructureOpen(false)} catalogue={order.active ? order.snapshot : filterSnapshot} catalogueLoading={order.active ? order.loading : filtersLoading} catalogueError={order.active ? false : filtersError} onRetry={() => {
                if (order.active)
                    void order.load();
                else
                    setFiltersRevision(value => value + 1);
            }} query={catalogue.appliedQuery} center={centerFilter} group={groupFilter}/></div>}
      {(canManage || order.active || createCommand.blocked || (editingCatalogue && hasPending)) && <section className="directory-create" aria-labelledby="cash-flow-maintenance-title">
        <div className="directory-create-header"><h2 id="cash-flow-maintenance-title">Статья ДДС</h2>
          <div className="directory-actions">
            <button type="button" disabled={order.loading || order.blocked || createCommand.blocked || editorSaving || rowEditor.saving || editorMode !== null
                || (editingCatalogue ? hasPending && !rowEditor.canDiscard : catalogue.loading
                    || catalogue.error !== null || catalogue.query.trim() !== catalogue.appliedQuery)} aria-pressed={editingCatalogue} onClick={toggleCatalogueEditing}>
              {editingCatalogue ? 'Завершить редактирование' : 'Редактировать справочник'}</button>
            {editingCatalogue && <button type="button" disabled={!canManage || order.loading || order.blocked || !rowEditor.canSave || editorSaving || editorMode !== null} onClick={() => {
                    if (canManage)
                        void rowEditor.save();
                }}>{rowEditor.saving ? 'Сохранение…' : 'Сохранить изменения'}</button>}
            {canManage && <button type="button" onClick={openCreate} disabled={order.loading || order.blocked || editorSaving || hasPending || rowEditor.saving}>Добавить статью</button>}
            {editingCatalogue && !order.active && <button type="button" disabled={order.blocked || order.loading || rowEditor.count > 0 || rowEditor.saving || editorSaving || editorMode !== null || createCommand.blocked
                    || (!order.active && (catalogue.loading || catalogue.error !== null || catalogue.query.trim() !== catalogue.appliedQuery))} onClick={toggleOrderEditing}>
              {order.loading ? 'Загрузка структуры…' : 'Повторить загрузку порядка'}</button>}
          </div></div>
        {editorMode && <form className="cash-flow-item-maintenance-form" onSubmit={event => void submitEditor(event)}>
          <fieldset disabled={createCommand.blocked} className="directory-create-fields">
            <label>Наименование<input value={editorName} onChange={event => setEditorName(event.target.value)} maxLength={150} required/></label>
            <label>Направление<select value={editorFlowDirection} onChange={event => setEditorFlowDirection(event.target.value as 'Payment' | 'Receipt' | '')}>
              <option value="">Не задано</option><option value="Payment">Выплата</option><option value="Receipt">Поступление</option>
            </select></label>
            <label>Группа<input value={editorGroupName} onChange={event => setEditorGroupName(event.target.value)} maxLength={150} placeholder="Введите наименование группы" disabled={editorSaving}/></label>
            <label>Вид деятельности<select value={editorActivityType} onChange={event => setEditorActivityType(event.target.value as CashFlowActivityType)} disabled={editorSaving} required>
              {CASH_FLOW_ACTIVITY_TYPES.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
            </select></label>
          </fieldset>
          {editorError && <p className="directory-error" role="alert">{editorError}</p>}
          <div className="cash-flow-settings-actions"><button type="submit" disabled={!canManage || createCommand.busy}>{createCommand.busy ? 'Сохранение…' : createCommand.unknown ? 'Повторить ту же попытку' : 'Сохранить'}</button>
            <button type="button" disabled={createCommand.blocked} onClick={() => {
                    if (!createCommand.blocked)
                        setEditorMode(null);
                }}>Отмена</button></div>
        </form>}
      </section>}

      {(rowEditor.count > 0 || rowEditor.notice) && <div className="cash-flow-directory-edit-notice" role="status">
        <p>{rowEditor.saving ? 'Сохранение изменений…' : rowEditor.count > 0
                ? `Несохранённых строк: ${rowEditor.count}. Нажмите «Сохранить изменения».` : rowEditor.notice}</p>
        {rowEditor.count > 0 && rowEditor.notice && <p>{rowEditor.notice}</p>}
        {[...rowEditor.entries.values()].filter(entry => entry.error).map(entry => <p role="alert" key={entry.draft.item.id}>{entry.draft.item.name}: {entry.error}</p>)}
        {rowEditor.hasProblems && <button type="button" disabled={rowEditor.saving} onClick={() => {
                    if (window.confirm('Обновить проверку изменённых строк? Ваши введённые значения останутся в редакторе. Проверьте их перед повторным сохранением.'))
                        void rowEditor.refresh();
                }}>Обновить проверку изменений</button>}
      </div>}

      {order.message && <p role="status" className="cash-flow-order-notice">{order.message}
        {order.unknown && <button type="button" disabled={order.busy} onClick={order.retry}>Повторить ту же попытку</button>}
        {!order.blocked && order.active && <button type="button" disabled={order.loading || rowEditor.count > 0 || rowEditor.saving || editorMode !== null || editorSaving || createCommand.blocked} onClick={() => {
                    if (!assignmentLock.current && !deletionInFlight.current && !orderReadInFlight.current
                        && !rowEditor.count && !rowEditor.saving && !editorSaving && editorMode === null && !createCommand.blocked && !order.blocked)
                        void order.load();
                }}>Обновить структуру</button>}
      </p>}
      <fieldset className="directory-reader-controls" disabled={order.loading || order.blocked || createCommand.blocked || rowEditor.saving || editorSaving}>
      <ReferenceDirectorySearchToolbar query={catalogue.query} visibleCount={catalogue.total} onQueryChange={catalogue.setQuery} onClear={() => catalogue.setQuery('')}><div className="directory-actions cash-flow-directory-filters">
        <label>ЦФО<select aria-label="Фильтр ЦФО" value={centerFilter} disabled={filterItems === null} onChange={event => { setCenterFilter(event.target.value); setCenterFilterCaption(event.target.selectedOptions[0]?.label ?? event.target.value); }}><option value="">Все ЦФО</option>
          {centerFilter && !filterChoices.centers.some(([id]) => id === centerFilter)
            && <option value={centerFilter}>{centerFilterCaption || centerFilter}</option>}
          {filterChoices.centers.map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select></label>
        <label>Группа<select aria-label="Фильтр группы" value={groupFilter} disabled={filterItems === null} onChange={event => { setGroupFilter(event.target.value); setGroupFilterCaption(event.target.selectedOptions[0]?.label ?? event.target.value); }}><option value="">Все группы</option>
          {groupFilter && !filterChoices.groups.some(([id]) => id === groupFilter)
            && <option value={groupFilter}>{groupFilterCaption || groupFilter}</option>}
          {filterChoices.groups.map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select></label>
        <button type="button" disabled={!centerFilter && !groupFilter && !catalogue.query} onClick={() => { setCenterFilter(''); setGroupFilter(''); catalogue.setQuery(''); }}>Сбросить фильтры</button>
      </div></ReferenceDirectorySearchToolbar>
      {filtersError && <p role="alert">Не удалось загрузить списки фильтров.
        <button type="button" onClick={() => setFiltersRevision(value => value + 1)}>Повторить загрузку фильтров</button></p>}
      <ReferenceDirectoryPager skip={catalogue.skip} take={catalogue.take} pageSize={catalogue.pageSize} onPageSizeChange={catalogue.setPageSize} total={catalogue.total} loading={catalogue.loading} hasPreviousPage={catalogue.hasPreviousPage} hasNextPage={catalogue.hasNextPage} onPreviousPage={catalogue.goToPreviousPage} onNextPage={catalogue.goToNextPage}/>
      </fieldset>

      {catalogue.loading && (<p className="directory-loading" role="status">Загрузка данных справочника…</p>)}

      {catalogue.error !== null && (<ReferenceDirectoryError message={catalogue.error} onRetry={catalogue.reload}/>)}

      {showEmpty && (<p className="directory-empty" role="status">
          {catalogue.appliedQuery === '' && !centerFilter && !groupFilter
                ? emptyMessages['cash-flow-items']
                : 'По условию поиска ничего не найдено.'}
        </p>)}

      {showTable && (<CashFlowDirectoryGrid items={catalogue.items} offset={catalogue.skip} positions={new Map((orderItems ?? filterItems ?? []).map((item, index) => [item.id, index + 1]))} reorder={editingCatalogue && order.active ? { blocked: order.blocked || order.loading || !canManage || !order.snapshot?.canEdit
                    || rowEditor.frozen || rowEditor.hasProblems || rowEditor.saving || editorSaving || editorMode !== null
                    || catalogue.query.trim() !== catalogue.appliedQuery,
                total: orderItems?.length ?? 0,
                moveToPosition: (source, position) => {
                    if (canManage && editingCatalogue
                        && !rowEditor.frozen && !rowEditor.hasProblems && !rowEditor.saving && !editorSaving && editorMode === null
                        && catalogue.query.trim() === catalogue.appliedQuery)
                        void order.moveToPosition(source, position);
                },
                move: (source, target, placement) => {
                    if (canManage && editingCatalogue
                        && !rowEditor.frozen && !rowEditor.hasProblems && !rowEditor.saving && !editorSaving && editorMode === null
                        && catalogue.query.trim() === catalogue.appliedQuery)
                        void order.move(source, target, placement);
                } } : undefined} editing={canManage && editingCatalogue} disabled={!canManage || order.loading || order.blocked || editorSaving || editorMode !== null || catalogue.query.trim() !== catalogue.appliedQuery} choicesLoading={centersLoading || centersError} centers={costCenters} expenses={expenseItems} editor={rowEditor} onDelete={item => void deleteItem(item)}/>)}
      {canManage && editingCatalogue && centersError && <p role="alert" className="directory-error">Не удалось загрузить ЦФО и статьи затрат.
        <button type="button" disabled={hasPending || rowEditor.saving} onClick={() => setCentersRevision(value => value + 1)}>Повторить загрузку списков</button></p>}
      {editorError && editorMode === null && <p className="directory-error" role="alert">{editorError}</p>}
    </div>);
}
