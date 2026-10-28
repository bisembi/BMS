import { useEffect, useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import { budgetOwnerAssignmentErrorMessage, createBudgetOwnerCostCenterAssignment, deleteBudgetOwnerCostCenterAssignment, updateBudgetOwnerCostCenterAssignment } from '../api';
import { listBudgetDirectoryEmployees, type BudgetDirectoryOption } from '../api/budgetDirectory';
import { getCostCenters } from '../api/referenceData';
import { ReferenceDirectoryError } from '../components/reference-directory/ReferenceDirectoryError';
import { ReferenceDirectorySearchToolbar } from '../components/reference-directory/ReferenceDirectorySearchToolbar';
import { BudgetOwnerCostCenterAssignmentGrid } from '../components/budget-owner-directory/BudgetOwnerCostCenterAssignmentGrid';
import { useBudgetOwnerCostCenterAssignments } from '../hooks/useBudgetOwnerCostCenterAssignments';
import type { BudgetOwnerCostCenterAssignment, ReferenceDataItem } from '../types';
import { createOperationKey } from '../utils/operationKey';
const optionPageSize = 100;
const maximumEmployeeOptions = 10000;
const optionsLoadError = 'Не удалось загрузить сотрудников и ЦФО. Повторите попытку.';
type SelectOption = {
    code: string;
    name: string;
    current?: boolean;
};
async function loadActiveEmployees(signal: AbortSignal): Promise<BudgetDirectoryOption[]> {
    const result: BudgetDirectoryOption[] = [];
    let skip = 0;
    let total = 0;
    do {
        const page = await listBudgetDirectoryEmployees('', skip, optionPageSize, signal);
        total = page.total;
        result.push(...page.items.filter(item => item.isActive));
        if (result.length > maximumEmployeeOptions) {
            throw new Error(optionsLoadError);
        }
        if (page.items.length === 0)
            break;
        skip += page.items.length;
    } while (skip < total);
    return result;
}
function asSelectOptions(items: readonly BudgetDirectoryOption[] | readonly ReferenceDataItem[], current?: SelectOption): SelectOption[] {
    const options = items.map(item => ({ code: item.code, name: item.name }));
    if (current && !options.some(option => option.code.toLocaleLowerCase('ru-RU') === current.code.toLocaleLowerCase('ru-RU'))) {
        options.unshift(current);
    }
    return options;
}
function matchesQuery(item: {
    employeeCode: string;
    employeeName: string;
    costCenterCode: string;
    costCenterName: string;
}, query: string): boolean {
    const needle = query.trim().toLocaleLowerCase('ru-RU');
    if (needle === '')
        return true;
    return item.employeeCode.toLocaleLowerCase('ru-RU').includes(needle)
        || item.employeeName.toLocaleLowerCase('ru-RU').includes(needle)
        || item.costCenterCode.toLocaleLowerCase('ru-RU').includes(needle)
        || item.costCenterName.toLocaleLowerCase('ru-RU').includes(needle);
}
export function BudgetOwnerCostCenterAssignmentDirectoryPage({ canManage = false }: {
    canManage?: boolean;
}) {
    const [query, setQuery] = useState('');
    const directory = useBudgetOwnerCostCenterAssignments();
    const [editorMode, setEditorMode] = useState<'create' | 'edit' | null>(null);
    const [editorItem, setEditorItem] = useState<BudgetOwnerCostCenterAssignment | null>(null);
    const [employeeCode, setEmployeeCode] = useState('');
    const [costCenterCode, setCostCenterCode] = useState('');
    const [saving, setSaving] = useState(false);
    const [editorError, setEditorError] = useState<string | null>(null);
    const [employees, setEmployees] = useState<BudgetDirectoryOption[]>([]);
    const [costCenters, setCostCenters] = useState<ReferenceDataItem[]>([]);
    const [optionsLoading, setOptionsLoading] = useState(false);
    const [optionsError, setOptionsError] = useState<string | null>(null);
    const [optionsRevision, setOptionsRevision] = useState(0);
    const visibleItems = useMemo(() => directory.items.filter((item) => matchesQuery(item, query)), [directory.items, query]);
    const isReady = !directory.loading && directory.error === null;
    useEffect(() => {
        if (!canManage) {
            setEmployees([]);
            setCostCenters([]);
            setOptionsLoading(false);
            setOptionsError(null);
            return;
        }
        let active = true;
        const controller = new AbortController();
        setOptionsLoading(true);
        setOptionsError(null);
        void Promise.all([loadActiveEmployees(controller.signal), getCostCenters()])
            .then(([employeeOptions, costCenterCatalog]) => {
            if (!active || controller.signal.aborted)
                return;
            setEmployees(employeeOptions);
            setCostCenters(costCenterCatalog.items);
        })
            .catch(() => {
            if (!active || controller.signal.aborted)
                return;
            setEmployees([]);
            setCostCenters([]);
            setOptionsError(optionsLoadError);
        })
            .finally(() => {
            if (active && !controller.signal.aborted)
                setOptionsLoading(false);
        });
        return () => {
            active = false;
            controller.abort();
        };
    }, [canManage, optionsRevision]);
    const employeeOptions = useMemo(() => asSelectOptions(employees, editorItem ? {
        code: editorItem.employeeCode,
        name: editorItem.employeeName,
        current: true,
    } : undefined), [editorItem, employees]);
    const costCenterOptions = useMemo(() => asSelectOptions(costCenters, editorItem ? {
        code: editorItem.costCenterCode,
        name: editorItem.costCenterName,
        current: true,
    } : undefined), [costCenters, editorItem]);
    function openCreate() {
        if (!canManage || saving)
            return;
        setEditorMode('create');
        setEditorItem(null);
        setEmployeeCode('');
        setCostCenterCode('');
        setEditorError(null);
    }
    function openEdit(item: BudgetOwnerCostCenterAssignment) {
        if (!canManage || saving)
            return;
        setEditorMode('edit');
        setEditorItem(item);
        setEmployeeCode(item.employeeCode);
        setCostCenterCode(item.costCenterCode);
        setEditorError(null);
    }
    async function submit(event: FormEvent) {
        event.preventDefault();
        if (!canManage || !editorMode || saving)
            return;
        const input = { employeeCode: employeeCode.trim(), costCenterCode: costCenterCode.trim() };
        setSaving(true);
        setEditorError(null);
        try {
            if (editorMode === 'create')
                await createBudgetOwnerCostCenterAssignment(input, createOperationKey());
            else if (editorItem)
                await updateBudgetOwnerCostCenterAssignment(editorItem, input);
            setEditorMode(null);
            setEditorItem(null);
            await directory.reload();
        }
        catch (error) {
            setEditorError(budgetOwnerAssignmentErrorMessage(error, editorMode === 'create' ? 'создать' : 'изменить'));
        }
        finally {
            setSaving(false);
        }
    }
    async function remove(item: BudgetOwnerCostCenterAssignment) {
        if (!canManage || saving || !window.confirm(`Удалить связь ${item.employeeCode} — ${item.costCenterName}?`))
            return;
        setSaving(true);
        setEditorError(null);
        try {
            await deleteBudgetOwnerCostCenterAssignment(item);
            await directory.reload();
        }
        catch (error) {
            setEditorError(budgetOwnerAssignmentErrorMessage(error, 'удалить'));
        }
        finally {
            setSaving(false);
        }
    }
    return (<div className="directory-page">
      <h1>Владельцы бюджета и ЦФО</h1>
      {canManage && <section className="directory-create" aria-labelledby="budget-owner-maintenance-title">
        <div className="directory-create-header"><h2 id="budget-owner-maintenance-title">Связь владельца бюджета и ЦФО</h2>
          {!editorMode && <button type="button" onClick={openCreate} disabled={saving}>Добавить</button>}</div>
        {editorMode && (<form onSubmit={event => void submit(event)}>
            <div className="directory-create-fields">
              <label>Сотрудник<select value={employeeCode} onChange={event => setEmployeeCode(event.target.value)} required disabled={saving || optionsLoading || optionsError !== null}>
                <option value="">{optionsLoading ? 'Загрузка сотрудников…' : 'Выберите сотрудника'}</option>
                {employeeOptions.map(option => <option key={option.code} value={option.code}>
                  {option.code} — {option.name}{option.current ? ' (текущий)' : ''}
                </option>)}
              </select></label>
              <label>ЦФО<select value={costCenterCode} onChange={event => setCostCenterCode(event.target.value)} required disabled={saving || optionsLoading || optionsError !== null}>
                <option value="">{optionsLoading ? 'Загрузка ЦФО…' : 'Выберите ЦФО'}</option>
                {costCenterOptions.map(option => <option key={option.code} value={option.code}>
                  {option.name}{option.current ? ' (текущий)' : ''}
                </option>)}
              </select></label>
            </div>
            {optionsError && <p className="directory-error" role="alert">
              {optionsError} <button type="button" disabled={saving} onClick={() => setOptionsRevision(value => value + 1)}>Повторить</button>
            </p>}
            {editorError && <p className="directory-error" role="alert">{editorError}</p>}
            <div className="cash-flow-settings-actions">
              <button type="submit" disabled={saving || optionsLoading || optionsError !== null}>{saving ? 'Сохранение…' : 'Сохранить'}</button>
              <button type="button" disabled={saving} onClick={() => setEditorMode(null)}>Отмена</button>
            </div>
          </form>)}
      </section>}
      {editorError && !editorMode && <p className="directory-error" role="alert">{editorError}</p>}
      <ReferenceDirectorySearchToolbar query={query} visibleCount={visibleItems.length} onQueryChange={setQuery} onClear={() => setQuery('')}/>
      {directory.loading && (<p className="directory-loading" role="status">Загрузка связей владельцев бюджета и ЦФО…</p>)}
      {directory.error !== null && (<ReferenceDirectoryError message={directory.error} onRetry={() => void directory.reload()}/>)}
      {isReady && visibleItems.length === 0 && (<p className="directory-empty" role="status">
          {query === ''
                ? 'Активные связи владельцев бюджета и ЦФО не найдены.'
                : 'По условию поиска ничего не найдено.'}
        </p>)}
      {isReady && visibleItems.length > 0 && (<BudgetOwnerCostCenterAssignmentGrid items={visibleItems} onEdit={canManage ? openEdit : undefined} onDelete={canManage ? item => void remove(item) : undefined} editable={canManage}/>)}
    </div>);
}
