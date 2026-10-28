import { useWorkspaceRefreshGuard } from '../components/WorkspaceRefreshContext';
import { useMemo, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { createOrganization, createExpenseItem, expenseItemCreateErrorMessage, isExpenseItemCreateUncertain, isOrganizationCreateUncertain, organizationCreateErrorMessage, organizationUpdateErrorMessage, updateOrganization, createReferenceDataItem, deleteReferenceDataItem, referenceDataCreateErrorMessage, referenceDataDeleteErrorMessage, referenceDataUpdateErrorMessage, updateReferenceDataItem, type CreateOrganizationInput, type CreateExpenseItemInput, type ReferenceDataMaintenanceInput } from '../api';
import { CashFlowItemDirectoryPage } from './CashFlowItemDirectoryPage';
import { CostCenterEditor } from '../components/reference-directory/CostCenterEditor';
import { CostDepartments473 } from '../components/reference-directory/CostDepartments473';
import { DepartmentKind475 } from '../components/reference-directory/DepartmentKind475';
import { ReferenceDirectoryError } from '../components/reference-directory/ReferenceDirectoryError';
import { ReferenceDirectoryGrid } from '../components/reference-directory/ReferenceDirectoryGrid';
import { ReferenceDirectoryScopeSelector } from '../components/reference-directory/ReferenceDirectoryScopeSelector';
import { ReferenceDirectorySearchToolbar } from '../components/reference-directory/ReferenceDirectorySearchToolbar';
import { emptyMessages, matchesQuery } from '../components/reference-directory/referenceDirectoryText';
import { useReferenceDirectoryItems } from '../hooks/useReferenceDirectoryItems';
import { useReferenceDirectoryScope } from '../hooks/useReferenceDirectoryScope';
import { REFERENCE_DIRECTORIES, type ReferenceDirectoryId } from '../navigation';
import type { ReferenceDataItem } from '../types';
import { useFrozenCreate } from '../hooks/useFrozenCreate';
import { isReferenceDataCreateUncertain } from '../api/referenceDataMaintenanceErrors';
/// The reference directories.
///
/// Cash-flow items are read one server page at a time and every other
/// directory is still downloaded whole and filtered in the browser. That split
/// is deliberate: the cash-flow catalogue is the one that reached tens of
/// thousands of rows, and nothing measured says the other six have.
export function ReferenceDirectoryPage({ directoryId, canEditExpenseItems = false, canEditOrganizations = false, canManageReferenceData = false, onNavigationLockChange, }: {
    directoryId: ReferenceDirectoryId;
    canEditExpenseItems?: boolean;
    canEditOrganizations?: boolean;
    canManageReferenceData?: boolean;
    onNavigationLockChange?: (locked: boolean) => void;
}) {
    return directoryId === 'cash-flow-items'
        ? <CashFlowItemDirectoryPage canManage={canManageReferenceData || canEditExpenseItems || canEditOrganizations} onNavigationLockChange={onNavigationLockChange}/>
        : <LocalReferenceDirectoryPage directoryId={directoryId} canEditExpenseItems={canEditExpenseItems} canEditOrganizations={canEditOrganizations} canManageReferenceData={canManageReferenceData} onNavigationLockChange={onNavigationLockChange}/>;
}
type LocalReferenceDirectoryId = Exclude<ReferenceDirectoryId, 'cash-flow-items'>;
function LocalReferenceDirectoryPage({ directoryId, canEditExpenseItems, canEditOrganizations, canManageReferenceData, onNavigationLockChange, }: {
    directoryId: LocalReferenceDirectoryId;
    canEditExpenseItems: boolean;
    canEditOrganizations: boolean;
    canManageReferenceData: boolean;
    onNavigationLockChange?: (locked: boolean) => void;
}) {
    const definition = REFERENCE_DIRECTORIES[directoryId];
    const maintenanceCommand = useFrozenCreate<ReferenceDataMaintenanceInput>('reference-create:' + directoryId, isReferenceDataCreateUncertain);
    const expenseCreateCommand = useFrozenCreate<CreateExpenseItemInput>('expense-item-create', error => isExpenseItemCreateUncertain(error));
    const organizationCreateCommand = useFrozenCreate<CreateOrganizationInput>('organization-create', error => isOrganizationCreateUncertain(error));
    const creationBlocked = maintenanceCommand.blocked || expenseCreateCommand.blocked || organizationCreateCommand.blocked;
    const [query, setQuery] = useState('');
    const [editingCfo, setEditingCfo] = useState<ReferenceDataItem | null>(null);
    const [creatingCfo, setCreatingCfo] = useState(false);
    const [showOnlyDepartments, setShowOnlyDepartments] = useState(directoryId === 'cost-centers');
    const [cfoLocked, setCfoLocked] = useState(false);
    const [createOpen, setCreateOpen] = useState(false);
    const [createName, setCreateName] = useState('');
    const [createSaving, setCreateSaving] = useState(false);
    const [createError, setCreateError] = useState<string | null>(null);
    const [notice, setNotice] = useState<string | null>(null);
    const createAttempt = useRef<{
        input: CreateExpenseItemInput;
    } | null>(null);
    const [organizationCreateOpen, setOrganizationCreateOpen] = useState(false);
    const [organizationCreateCode, setOrganizationCreateCode] = useState('');
    const [organizationCreateName, setOrganizationCreateName] = useState('');
    const [organizationCreateSaving, setOrganizationCreateSaving] = useState(false);
    const [organizationCreateError, setOrganizationCreateError] = useState<string | null>(null);
    const organizationCreateAttempt = useRef<{
        input: CreateOrganizationInput;
    } | null>(null);
    const [editingItem, setEditingItem] = useState<ReferenceDataItem | null>(null);
    const [editCode, setEditCode] = useState('');
    const [editName, setEditName] = useState('');
    const [editSaving, setEditSaving] = useState(false);
    const [editError, setEditError] = useState<string | null>(null);
    const [maintenanceMode, setMaintenanceMode] = useState<'create' | 'edit' | null>(null);
    const [maintenanceItem, setMaintenanceItem] = useState<ReferenceDataItem | null>(null);
    const [maintenanceCode, setMaintenanceCode] = useState('');
    const [maintenanceName, setMaintenanceName] = useState('');
    const [maintenanceBin, setMaintenanceBin] = useState('');
    const [maintenanceNumericCode, setMaintenanceNumericCode] = useState('');
    const [maintenanceCurrencyId, setMaintenanceCurrencyId] = useState('');
    const [maintenanceContractDate, setMaintenanceContractDate] = useState('');
    const [maintenanceSaving, setMaintenanceSaving] = useState(false);
    const [maintenanceError, setMaintenanceError] = useState<string | null>(null);
    // Two independent readers: the scope and the rows keep separate request
    // sequences, so a late scope response cannot overwrite loaded rows.
    useWorkspaceRefreshGuard(creationBlocked || createSaving || organizationCreateSaving || editSaving || maintenanceSaving
        || createOpen || organizationCreateOpen || editingItem !== null || maintenanceMode !== null || cfoLocked || creatingCfo
        ? 'Сначала сохраните или закройте открытый редактор справочника.' : null);
    const scope = useReferenceDirectoryScope(definition.requiresScope);
    const directory = useReferenceDirectoryItems({
        directoryId,
        requiresScope: definition.requiresScope,
        selectedOrganization: scope.selectedOrganization,
        selectedCounterparty: scope.selectedCounterparty,
        scopeComplete: scope.scopeComplete,
    });
    const byId = useMemo(() => new Map(directory.items.map((item) => [item.id, item] as const)), [directory.items]);
    const visibleItems = useMemo(() => directoryId === 'contracts' ? directory.items : directory.items.filter((item) => matchesQuery(item, query) || (directoryId === 'cost-centers' && query.trim() !== ''
        && [item.groupingName ?? '', ...(item.ownerNames ?? [])].some(v => v.toLocaleLowerCase('ru').includes(query.trim().toLocaleLowerCase('ru'))))), [directory.items, query, directoryId]);
    const showScopePrompt = definition.requiresScope && !scope.scopeComplete;
    const codeLabel = directoryId === 'cost-centers' ? 'Префикс' : 'Код';
    const showTable = !directory.loading && directory.error === null
        && !showScopePrompt && visibleItems.length > 0;
    const showEmpty = !directory.loading && directory.error === null
        && !showScopePrompt && visibleItems.length === 0;
    const canCreateExpenseItem = directoryId === 'expense-items' && canEditExpenseItems;
    const canManage = canManageReferenceData || canEditExpenseItems || canEditOrganizations;
    const canEditOrganization = directoryId === 'organizations' && canManage;
    const canCreateOrganization = canEditOrganization;
    if (directoryId === 'cost-centers' && showOnlyDepartments)
        return <div className="directory-page department-directory480">
    <h1>{definition.title}</h1>
    <DepartmentKind475 costOnly disabled={cfoLocked} onChange={setShowOnlyDepartments}/>
    <CostDepartments473 canManage={canManage} onLockChange={value => { setCfoLocked(value); onNavigationLockChange?.(value); }}/>
  </div>;
    function openMaintenanceCreate() {
        if (!canManage || maintenanceSaving || creationBlocked || editingCfo || creatingCfo)
            return;
        if (directoryId === 'cost-centers') {
            setCreatingCfo(true);
            setNotice(null);
            return;
        }
        setMaintenanceMode('create');
        setMaintenanceItem(null);
        setMaintenanceCode('');
        setMaintenanceName('');
        setMaintenanceBin('');
        setMaintenanceNumericCode('');
        setMaintenanceCurrencyId('');
        setMaintenanceContractDate('');
        setMaintenanceError(null);
    }
    function openMaintenanceEdit(item: ReferenceDataItem) {
        if (!canManage || maintenanceSaving || creationBlocked || editingCfo || creatingCfo || maintenanceMode !== null)
            return;
        if (directoryId === 'cost-centers') {
            setEditingCfo(item);
            return;
        }
        setMaintenanceMode('edit');
        setMaintenanceItem(item);
        setMaintenanceCode(item.code);
        setMaintenanceName(item.name);
        setMaintenanceBin(item.bin ?? '');
        setMaintenanceNumericCode(item.numericCode ?? '');
        setMaintenanceCurrencyId('');
        setMaintenanceContractDate('');
        setMaintenanceError(null);
    }
    async function submitMaintenance(event: FormEvent) {
        event.preventDefault();
        if (!canManage || maintenanceMode === null || maintenanceSaving || editingCfo !== null || cfoLocked)
            return;
        const input: ReferenceDataMaintenanceInput = {
            ...(directoryId === 'currencies' || directoryId === 'counterparties' ? {} : { code: maintenanceCode.trim() }),
            name: maintenanceName.trim(),
            ...(directoryId === 'counterparties' ? { bin: maintenanceBin.trim() || null } : {}),
            ...(directoryId === 'currencies' ? { numericCode: maintenanceNumericCode.trim() } : {}),
            ...(directoryId === 'contracts' ? {
                organizationId: scope.selectedOrganization?.id,
                counterpartyId: scope.selectedCounterparty?.id,
                currencyId: maintenanceCurrencyId.trim(),
                contractDate: maintenanceContractDate,
            } : {}),
        };
        setMaintenanceSaving(true);
        setMaintenanceError(null);
        if (maintenanceMode === 'create') {
            const result = await maintenanceCommand.run(input, (body, key) => createReferenceDataItem(directoryId, body, key));
            setMaintenanceSaving(false);
            if (result.kind === 'confirmed') {
                setMaintenanceMode(null);
                setMaintenanceItem(null);
                setNotice('Изменения сохранены.');
                await directory.reload();
            }
            else if (result.kind !== 'inactive')
                setMaintenanceError(result.kind === 'uncertain'
                    ? 'Результат не подтверждён. Повторите ту же попытку с исходными данными.' : referenceDataCreateErrorMessage(result.error));
            return;
        }
        try {
            if (maintenanceItem)
                await updateReferenceDataItem(directoryId, maintenanceItem.id, input);
            setMaintenanceMode(null);
            setMaintenanceItem(null);
            setNotice('Изменения сохранены.');
            await directory.reload();
        }
        catch (error) {
            setMaintenanceError(referenceDataUpdateErrorMessage(error));
        }
        finally {
            setMaintenanceSaving(false);
        }
    }
    async function deleteMaintenance(item: ReferenceDataItem) {
        if (creationBlocked)
            return;
        if (!canManage || maintenanceSaving || !window.confirm(`Удалить «${item.name}»?`))
            return;
        setMaintenanceSaving(true);
        setMaintenanceError(null);
        try {
            await deleteReferenceDataItem(directoryId, item.id);
            setNotice('Элемент удалён.');
            await directory.reload();
        }
        catch (error) {
            setMaintenanceError(referenceDataDeleteErrorMessage(error));
        }
        finally {
            setMaintenanceSaving(false);
        }
    }
    function openCreate() {
        if (!canCreateExpenseItem || createSaving || creationBlocked)
            return;
        setCreateOpen(true);
        setCreateName('');
        setCreateError(null);
        setNotice(null);
        createAttempt.current = null;
    }
    function closeCreate() {
        if (createSaving || expenseCreateCommand.blocked)
            return;
        setCreateOpen(false);
        setCreateError(null);
        createAttempt.current = null;
    }
    async function submitCreate(event: FormEvent) {
        event.preventDefault();
        if (!canCreateExpenseItem || createSaving)
            return;
        if (createAttempt.current === null) {
            const name = createName.trim();
            if (name === '' || name.length > 250) {
                setCreateError('Укажите наименование до 250 символов.');
                return;
            }
            try {
                createAttempt.current = {
                    input: { name },
                };
            }
            catch {
                setCreateError('Не удалось подготовить создание. Повторите попытку.');
                return;
            }
        }
        const attempt = createAttempt.current;
        if (attempt === null)
            return;
        setCreateSaving(true);
        setCreateError(null);
        try {
            const result = await expenseCreateCommand.run(attempt.input, (body, key) => createExpenseItem(body, key));
            if (result.kind === 'inactive')
                return;
            if (result.kind !== 'confirmed') {
                setCreateError(result.kind === 'uncertain' ? 'Результат не подтверждён. Повторите ту же попытку с исходными данными.' : expenseItemCreateErrorMessage(result.error));
                if (result.kind === 'refused')
                    createAttempt.current = null;
                return;
            }
            createAttempt.current = null;
            setCreateOpen(false);
            setCreateName('');
            setNotice('Статья затрат добавлена.');
            await directory.reload();
        }
        catch (error) {
            setCreateError(expenseItemCreateErrorMessage(error));
            if (!isExpenseItemCreateUncertain(error))
                createAttempt.current = null;
        }
        finally {
            setCreateSaving(false);
        }
    }
    function openOrganizationCreate() {
        if (!canCreateOrganization || organizationCreateSaving || creationBlocked)
            return;
        setOrganizationCreateOpen(true);
        setOrganizationCreateCode('');
        setOrganizationCreateName('');
        setOrganizationCreateError(null);
        setNotice(null);
        organizationCreateAttempt.current = null;
    }
    function closeOrganizationCreate() {
        if (organizationCreateSaving || organizationCreateCommand.blocked)
            return;
        setOrganizationCreateOpen(false);
        setOrganizationCreateError(null);
        organizationCreateAttempt.current = null;
    }
    async function submitOrganizationCreate(event: FormEvent) {
        event.preventDefault();
        if (!canCreateOrganization || organizationCreateSaving)
            return;
        if (organizationCreateAttempt.current === null) {
            const code = organizationCreateCode.trim();
            const name = organizationCreateName.trim();
            if (code === '' || code.length > 100 || name === '' || name.length > 500) {
                setOrganizationCreateError('Укажите код длиной до 100 символов и наименование до 500 символов.');
                return;
            }
            try {
                organizationCreateAttempt.current = {
                    input: { code, name },
                };
            }
            catch {
                setOrganizationCreateError('Не удалось подготовить создание. Повторите попытку.');
                return;
            }
        }
        const attempt = organizationCreateAttempt.current;
        if (attempt === null)
            return;
        setOrganizationCreateSaving(true);
        setOrganizationCreateError(null);
        try {
            const result = await organizationCreateCommand.run(attempt.input, (body, key) => createOrganization(body, key));
            if (result.kind === 'inactive')
                return;
            if (result.kind !== 'confirmed') {
                setOrganizationCreateError(result.kind === 'uncertain' ? 'Результат не подтверждён. Повторите ту же попытку с исходными данными.' : organizationCreateErrorMessage(result.error));
                if (result.kind === 'refused')
                    organizationCreateAttempt.current = null;
                return;
            }
            organizationCreateAttempt.current = null;
            setOrganizationCreateOpen(false);
            setOrganizationCreateCode('');
            setOrganizationCreateName('');
            setNotice('Организация добавлена.');
            await directory.reload();
        }
        catch (error) {
            setOrganizationCreateError(organizationCreateErrorMessage(error));
            if (!isOrganizationCreateUncertain(error))
                organizationCreateAttempt.current = null;
        }
        finally {
            setOrganizationCreateSaving(false);
        }
    }
    function openEdit(item: ReferenceDataItem) {
        if (!canManage || editSaving || creationBlocked)
            return;
        if (directoryId !== 'organizations') {
            openMaintenanceEdit(item);
            return;
        }
        setEditingItem(item);
        setEditCode(item.code);
        setEditName(item.name);
        setEditError(null);
        setNotice(null);
    }
    function closeEdit() {
        if (editSaving)
            return;
        setEditingItem(null);
        setEditError(null);
    }
    async function submitEdit(event: FormEvent) {
        event.preventDefault();
        if (!canEditOrganization || editingItem === null || editSaving)
            return;
        const code = editCode.trim();
        const name = editName.trim();
        if (code === '' || code.length > 100 || name === '' || name.length > 500) {
            setEditError('Укажите код длиной до 100 символов и наименование до 500 символов.');
            return;
        }
        setEditSaving(true);
        setEditError(null);
        try {
            await updateOrganization(editingItem.id, { code, name });
            setEditingItem(null);
            setNotice('Организация изменена.');
            await directory.reload();
        }
        catch (error) {
            setEditError(organizationUpdateErrorMessage(error));
        }
        finally {
            setEditSaving(false);
        }
    }
    return (<div className={`directory-page${directoryId === 'cost-centers' ? ' department-directory480' : ''}`}>
      <h1>{definition.title}</h1>
      {directoryId === 'cost-centers' && <DepartmentKind475 costOnly={false} disabled={editingCfo !== null || creatingCfo || cfoLocked} onChange={setShowOnlyDepartments}/>}
      {directoryId === 'cost-centers' && canManage && !creatingCfo && <div className="department-actions475">
        <button type="button" className="primary-button" onClick={openMaintenanceCreate} disabled={maintenanceSaving || editingCfo !== null || cfoLocked}>+ Добавить ЦФО</button>
      </div>}
      {canCreateExpenseItem && <section className="directory-create" aria-labelledby="expense-item-create-title">
        <div className="directory-create-header">
          <h2 id="expense-item-create-title">Новая статья затрат</h2>
          {!createOpen && <button type="button" onClick={openCreate} disabled={createSaving}>
            Добавить статью
          </button>}
        </div>
        {createOpen && <form onSubmit={event => void submitCreate(event)}>
          <fieldset disabled={expenseCreateCommand.blocked} className="directory-create-fields">
            <label>Код
              <input value="Присвоится автоматически" readOnly/>
            </label>
            <label>Наименование
              <input value={createName} maxLength={250} autoComplete="off" onChange={event => setCreateName(event.target.value)} required/>
            </label>
          </fieldset>
          {createError && <p className="directory-error" role="alert">{createError}</p>}
          <div className="cash-flow-settings-actions">
            <button type="submit" disabled={createSaving}>
              {createSaving ? 'Сохранение…' : 'Создать статью'}
            </button>
            <button type="button" disabled={createSaving || expenseCreateCommand.blocked} onClick={closeCreate}>Отмена</button>
          </div>
        </form>}
      </section>}
      {canCreateOrganization && <section className="directory-create" aria-labelledby="organization-create-title">
        <div className="directory-create-header">
          <h2 id="organization-create-title">Новая организация</h2>
          {!organizationCreateOpen && <button type="button" onClick={openOrganizationCreate} disabled={organizationCreateSaving}>
            Добавить организацию
          </button>}
        </div>
        {organizationCreateOpen && <form onSubmit={event => void submitOrganizationCreate(event)}>
          <fieldset disabled={organizationCreateCommand.blocked} className="directory-create-fields">
            <label>Код
              <input value={organizationCreateCode} maxLength={100} autoComplete="off" onChange={event => setOrganizationCreateCode(event.target.value)} required/>
            </label>
            <label>Наименование
              <input value={organizationCreateName} maxLength={500} autoComplete="off" onChange={event => setOrganizationCreateName(event.target.value)} required/>
            </label>
          </fieldset>
          {organizationCreateError && <p className="directory-error" role="alert">{organizationCreateError}</p>}
          <div className="cash-flow-settings-actions">
            <button type="submit" disabled={organizationCreateSaving}>
              {organizationCreateSaving ? 'Сохранение…' : 'Создать организацию'}
            </button>
            <button type="button" disabled={organizationCreateSaving || organizationCreateCommand.blocked} onClick={closeOrganizationCreate}>Отмена</button>
          </div>
        </form>}
      </section>}
      {notice && <p role="status">{notice}</p>}
      {(editingCfo || creatingCfo) && <CostCenterEditor key={editingCfo?.id ?? 'new-cfo'} id={editingCfo?.id} canManage={canManage} onClose={() => { setEditingCfo(null); setCreatingCfo(false); setCfoLocked(false); onNavigationLockChange?.(false); }} onSaved={() => { setNotice('Изменения сохранены.'); return directory.reload(); }} onLockChange={value => { setCfoLocked(value); onNavigationLockChange?.(value); }}/>}

      {editingItem !== null && <section className="directory-create" aria-labelledby="organization-edit-title">
        <div className="directory-create-header">
          <h2 id="organization-edit-title">Редактирование организации</h2>
        </div>
        <form onSubmit={event => void submitEdit(event)}>
          <div className="directory-create-fields">
            <label>Код
              <input value={editCode} maxLength={100} autoComplete="off" onChange={event => setEditCode(event.target.value)} required/>
            </label>
            <label>Наименование
              <input value={editName} maxLength={500} autoComplete="off" onChange={event => setEditName(event.target.value)} required/>
            </label>
          </div>
          {editError && <p className="directory-error" role="alert">{editError}</p>}
          <div className="cash-flow-settings-actions">
            <button type="submit" disabled={editSaving}>
              {editSaving ? 'Сохранение…' : 'Сохранить изменения'}
            </button>
            <button type="button" disabled={editSaving} onClick={closeEdit}>Отмена</button>
          </div>
        </form>
      </section>}

      {canManage && directoryId !== 'organizations' && directoryId !== 'cost-centers'
            && (directoryId !== 'expense-items' || maintenanceMode !== null)
            && <section className="directory-create" aria-labelledby="reference-maintenance-title">
        <div className="directory-create-header">
          <h2 id="reference-maintenance-title">
            {directoryId === 'currencies'
                    ? (maintenanceMode === 'create' ? 'Новая валюта' : 'Редактирование валюты')
                    : directoryId === 'counterparties'
                        ? (maintenanceMode === 'create' ? 'Новый контрагент' : 'Редактирование контрагента')
                        : directoryId === 'expense-items' ? 'Редактирование статьи затрат' : definition.title}
          </h2>
          {!maintenanceMode && directoryId !== 'expense-items'
                    && <button type="button" onClick={openMaintenanceCreate} disabled={maintenanceSaving || editingCfo !== null || creatingCfo}>
              {directoryId === 'currencies' ? 'Добавить валюту' : 'Добавить'}
            </button>}
        </div>
        {maintenanceMode && <form onSubmit={event => void submitMaintenance(event)}>
          <fieldset disabled={maintenanceCommand.blocked} className="directory-create-fields">
            {directoryId !== 'currencies' && directoryId !== 'counterparties' && <label>Код<input value={maintenanceCode} onChange={event => setMaintenanceCode(event.target.value)} maxLength={directoryId === 'expense-items' ? 9 : directoryId === 'contracts' ? 30 : 100} required/></label>}
            <label>Наименование<input value={maintenanceName} onChange={event => setMaintenanceName(event.target.value)} maxLength={directoryId === 'expense-items' ? 250 : directoryId === 'currencies' ? 50 : directoryId === 'contracts' ? 150 : 500} required/></label>
            {directoryId === 'counterparties' && <label>БИН<input value={maintenanceBin} onChange={event => setMaintenanceBin(event.target.value)} inputMode="numeric" pattern="[0-9]{12}" maxLength={12} placeholder="12 цифр"/></label>}
            {directoryId === 'currencies' && <label>Цифровой код валюты<input value={maintenanceNumericCode} onChange={event => setMaintenanceNumericCode(event.target.value)} inputMode="numeric" pattern="[0-9]{3}" maxLength={3} required/></label>}
            {directoryId === 'contracts' && <>
              <label>Дата договора<input type="date" value={maintenanceContractDate} onChange={event => setMaintenanceContractDate(event.target.value)} required/></label>
              <label>ID валюты<input value={maintenanceCurrencyId} onChange={event => setMaintenanceCurrencyId(event.target.value)} placeholder="GUID валюты" required/></label>
            </>}
          </fieldset>
          {maintenanceError && <p className="directory-error" role="alert">{maintenanceError}</p>}
          <div className="cash-flow-settings-actions"><button type="submit" disabled={maintenanceSaving}>{maintenanceSaving ? 'Сохранение…' : 'Сохранить'}</button>
            <button type="button" disabled={maintenanceSaving || maintenanceCommand.blocked} onClick={() => {
                        if (!maintenanceCommand.blocked)
                            setMaintenanceMode(null);
                    }}>Отмена</button></div>
        </form>}
      </section>}
      {maintenanceError && maintenanceMode === null && <p className="directory-error" role="alert">{maintenanceError}</p>}

      {definition.requiresScope && (<ReferenceDirectoryScopeSelector organizations={scope.organizations} counterparties={scope.counterparties} organizationIndex={scope.organizationIndex} counterpartyIndex={scope.counterpartyIndex} loading={scope.loading} error={scope.error} onOrganizationIndexChange={value => {
                if (!creationBlocked)
                    scope.setOrganizationIndex(value);
            }} onCounterpartyIndexChange={value => {
                if (!creationBlocked)
                    scope.setCounterpartyIndex(value);
            }} onRetry={() => void scope.reload()}/>)}

      {directoryId === 'cost-centers' && <label className="department-search475"><span>Поиск подразделений</span>
        <input value={query} maxLength={200} onChange={event => setQuery(event.target.value)}/></label>}
      {directoryId !== 'contracts' && directoryId !== 'currencies' && directoryId !== 'organizations' && directoryId !== 'cost-centers' && !showScopePrompt && (<ReferenceDirectorySearchToolbar query={query} visibleCount={visibleItems.length} codeLabel={codeLabel} onQueryChange={setQuery} onClear={() => setQuery('')}/>)}

      {showScopePrompt && !scope.loading && scope.error === null && (<p className="directory-empty" role="status">
          Выберите организацию и контрагента, чтобы увидеть договоры.
        </p>)}

      {directory.loading && (<p className="directory-loading" role="status">Загрузка данных справочника…</p>)}

      {directory.error !== null && (<ReferenceDirectoryError message={directory.error} onRetry={() => void directory.reload()}/>)}

      {showEmpty && (<p className="directory-empty" role="status">
          {directoryId === 'contracts' || query === '' ? emptyMessages[directoryId] : 'По условию поиска ничего не найдено.'}
        </p>)}

      {showTable && (<ReferenceDirectoryGrid items={visibleItems} caption={directoryId === 'cost-centers' ? 'Справочник центров финансовой ответственности' : definition.caption} showsHierarchy={definition.showsHierarchy} showsBin={directoryId === 'counterparties'} showsSources={directoryId === 'counterparties'} showsNumericCode={directoryId === 'currencies'} showsRelatedCashFlowItems={directoryId === 'expense-items'} byId={byId} renderOwner={directoryId === 'cost-centers' ? item => item.ownerNames?.join(', ') || 'Не назначен' : undefined} renderGrouping={directoryId === 'cost-centers' ? item => item.groupingName || '—' : undefined} actionsDisabled={editingCfo !== null || creatingCfo || cfoLocked || (directoryId === 'cost-centers' && (maintenanceMode !== null || creationBlocked))} onEditItem={canManage ? openEdit : undefined} onDeleteItem={canManage ? item => void deleteMaintenance(item) : undefined}/>)}
    </div>);
}
