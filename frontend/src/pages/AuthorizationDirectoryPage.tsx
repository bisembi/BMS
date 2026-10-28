import { useWorkspaceRefreshRevision, useWorkspaceRefreshGuard, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { authorizationDirectoryCreateErrorMessage, authorizationDirectoryErrorMessage, createAuthorizationDirectoryEmployee, getAuthorizationDirectoryEmployee, isAuthorizationDirectoryConflict, isAuthorizationDirectoryResultUncertain, isAuthorizationDirectoryValidationError, listAuthorizationDirectoryRoles, listAuthorizationDirectoryEmployees, updateAuthorizationDirectoryRole, updateAuthorizationDirectoryActor, type AuthorizationDirectoryEmployee, type AuthorizationDirectoryPage, type AuthorizationDirectoryRoleOption, type CreateAuthorizationDirectoryEmployeeInput, type UpdateAuthorizationDirectoryActorInput } from '../api/authorizationDirectory';
import { ReferenceDirectoryPager } from '../components/reference-directory/ReferenceDirectoryPager';
import { EmployeeCfoEditor } from '../components/EmployeeCfoEditor';
import { listCfoAssignments, type CfoAssignment } from '../api/cfoWorkflow';
import { useCommandLock } from '../components/WorkspaceCommandLockContext';
const pageSize = 25;
type Attempt = {
    input: UpdateAuthorizationDirectoryActorInput;
    key: string;
};
type CreateAttempt = {
    input: CreateAuthorizationDirectoryEmployeeInput;
    key: string;
};
export function AuthorizationDirectoryPage({ canEdit, canManageRoles = false, onNavigationLockChange }: {
    canEdit: boolean;
    canManageRoles?: boolean;
    onNavigationLockChange?: (locked: boolean) => void;
}) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const workspaceCommands = useCommandLock('authorization-reader');
    const [query, setQuery] = useState('');
    const [skip, setSkip] = useState(0);
    const [revision, setRevision] = useState(0);
    const [page, setPage] = useState<AuthorizationDirectoryPage | null>(null);
    const [selectedId, setSelectedId] = useState<string | null>(null);
    const [draftActor, setDraftActor] = useState('');
    const [draftActive, setDraftActive] = useState(false);
    const [dirty, setDirty] = useState(false);
    const [saving, setSaving] = useState(false);
    const [uncertain, setUncertain] = useState(false);
    const [refreshRequired, setRefreshRequired] = useState(false);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [notice, setNotice] = useState<string | null>(null);
    const [createOpen, setCreateOpen] = useState(false);
    const [createCode, setCreateCode] = useState('');
    const [createName, setCreateName] = useState('');
    const [createActor, setCreateActor] = useState('');
    const [createActive, setCreateActive] = useState(true);
    const [createSaving, setCreateSaving] = useState(false);
    const [createError, setCreateError] = useState<string | null>(null);
    const [rolesLoading, setRolesLoading] = useState(false);
    const [assignmentsLoading, setAssignmentsLoading] = useState(false);
    const [roleOptions, setRoleOptions] = useState<AuthorizationDirectoryRoleOption[]>([]);
    const [roleId, setRoleId] = useState('');
    const [roleActive, setRoleActive] = useState(true);
    const [roleSaving, setRoleSaving] = useState(false);
    const [roleError, setRoleError] = useState<string | null>(null);
    const [roleNotice, setRoleNotice] = useState<string | null>(null);
    const [cfoAssignments, setCfoAssignments] = useState<CfoAssignment[]>([]);
    const [cfoLocked, setCfoLocked] = useState(false);
    const roleGeneration = useRef(0);
    const generation = useRef(0);
    const attempt = useRef<Attempt | null>(null);
    const createAttempt = useRef<CreateAttempt | null>(null);
    const notifyLock = useRef(onNavigationLockChange);
    const editorTitle = useRef<HTMLHeadingElement>(null);
    const completedWorkspaceRefresh = useRef(workspaceRefresh);
    const editorIntentEpoch = useRef(0);
    const currentEditor = useRef({ selectedId, dirty, saving, uncertain, roleSaving, cfoLocked, createOpen });
    currentEditor.current = { selectedId, dirty, saving, uncertain, roleSaving, cfoLocked, createOpen };
    notifyLock.current = onNavigationLockChange;
    useEffect(() => () => notifyLock.current?.(false), []);
    useEffect(() => {
        const request = ++generation.current;
        const selectedAtStart = currentEditor.current.selectedId;
        const intentEpochAtStart = editorIntentEpoch.current;
        const controller = new AbortController();
        setLoading(true);
        setError(null);
        void listAuthorizationDirectoryEmployees(query, skip, pageSize, controller.signal)
            .then(answer => {
            if (controller.signal.aborted || request !== generation.current)
                return;
            if (workspaceCommands.hasViewCommands()) {
                setLoading(false);
                return;
            }
            if (completedWorkspaceRefresh.current !== workspaceRefresh) {
                completedWorkspaceRefresh.current = workspaceRefresh;
                const editor = currentEditor.current;
                const freshSelected = answer.items.find(item => item.employeeId.toLowerCase() === editor.selectedId?.toLowerCase());
                if (freshSelected && selectedAtStart?.toLowerCase() === editor.selectedId?.toLowerCase()
                    && intentEpochAtStart === editorIntentEpoch.current
                    && !editor.dirty && !editor.saving && !editor.uncertain && !editor.roleSaving && !editor.cfoLocked && !editor.createOpen) {
                    setDraftActor(freshSelected.actorExternalId ?? '');
                    setDraftActive(freshSelected.actorMappingIsActive);
                }
            }
            setPage(answer);
            setSelectedId(current => {
                if (current === null || answer.items.some(item => item.employeeId.toLowerCase() === current.toLowerCase())) {
                    return current;
                }
                setDirty(false);
                setRefreshRequired(false);
                setUncertain(false);
                attempt.current = null;
                notifyLock.current?.(false);
                return null;
            });
            setLoading(false);
        })
            .catch(failure => {
            if (controller.signal.aborted || request !== generation.current)
                return;
            setError(authorizationDirectoryErrorMessage(failure));
            setLoading(false);
        });
        return () => controller.abort();
    }, [query, revision, skip, workspaceRefresh]);
    useEffect(() => {
        if (!canEdit || selectedId === null) {
            setRolesLoading(false);
            return;
        }
        const request = ++roleGeneration.current;
        const controller = new AbortController();
        setRoleOptions([]);
        setRolesLoading(true);
        void listAuthorizationDirectoryRoles(controller.signal)
            .then(rolesAnswer => {
            if (controller.signal.aborted || request !== roleGeneration.current)
                return;
            setRoleOptions(rolesAnswer);
        }).catch(failure => {
            if (controller.signal.aborted || request !== roleGeneration.current)
                return;
            setRoleError(authorizationDirectoryErrorMessage(failure));
        }).finally(() => {
            if (!controller.signal.aborted && request === roleGeneration.current)
                setRolesLoading(false);
        });
        return () => controller.abort();
    }, [canEdit, selectedId, workspaceRefresh]);
    useWorkspaceRefreshGuard(dirty || saving || roleSaving || createSaving || uncertain || cfoLocked || createOpen
        ? 'Сначала сохраните или отмените изменения сотрудника; неподтверждённую команду нужно повторить.' : null);
    useWorkspaceRefreshBusy(loading || rolesLoading || assignmentsLoading);
    const selected = page?.items.find(item => item.employeeId.toLowerCase() === selectedId?.toLowerCase()) ?? null;
    useEffect(() => {
        if (selectedId === null)
            return;
        editorTitle.current?.focus({ preventScroll: true });
        editorTitle.current?.scrollIntoView?.({ block: 'start' });
    }, [selectedId]);
    const locked = dirty || saving || uncertain || refreshRequired || roleSaving || cfoLocked;
    useEffect(() => { notifyLock.current?.(locked); }, [locked]);
    useEffect(() => {
        if (!canEdit) {
            setAssignmentsLoading(false);
            return;
        }
        setAssignmentsLoading(true);
        const abort = new AbortController();
        void listCfoAssignments(abort.signal).then(result => {
            if (!abort.signal.aborted)
                setCfoAssignments(result);
        })
            .catch(() => {
            if (!abort.signal.aborted)
                setError('Не удалось загрузить назначения ЦФО.');
        })
            .finally(() => {
            if (!abort.signal.aborted)
                setAssignmentsLoading(false);
        });
        return () => abort.abort();
    }, [canEdit, revision, workspaceRefresh]);
    function select(item: AuthorizationDirectoryEmployee) {
        if (locked)
            return;
        editorIntentEpoch.current += 1;
        setSelectedId(item.employeeId);
        setDraftActor(item.actorExternalId ?? '');
        setDraftActive(item.actorMappingIsActive);
        setDirty(false);
        setUncertain(false);
        setRefreshRequired(false);
        setError(null);
        setNotice(null);
        setRoleId('');
        setRoleActive(true);
        setRoleError(null);
        setRoleNotice(null);
        attempt.current = null;
        notifyLock.current?.(false);
    }
    async function saveRole() {
        if (!canManageRoles || selected === null || !roleId || locked)
            return;
        setRoleSaving(true);
        notifyLock.current?.(true);
        setRoleError(null);
        setRoleNotice(null);
        try {
            const current = await getAuthorizationDirectoryEmployee(selected.employeeId);
            const result = await updateAuthorizationDirectoryRole(current.employeeId, { expectedConcurrencyStamp: current.concurrencyStamp, roleId, isActive: roleActive }, crypto.randomUUID());
            setPage(current => current === null ? current : {
                ...current,
                items: current.items.some(item => item.employeeId.toLowerCase() === result.employeeId.toLowerCase())
                    ? current.items.map(item => item.employeeId.toLowerCase() === result.employeeId.toLowerCase() ? result : item)
                    : [result, ...current.items],
            });
            setSelectedId(result.employeeId);
            setDraftActor(result.actorExternalId ?? '');
            setDraftActive(result.actorMappingIsActive);
            setRoleNotice('Роль сотрудника сохранена.');
            setRevision(value => value + 1);
        }
        catch (failure) {
            setRoleError(authorizationDirectoryErrorMessage(failure));
        }
        finally {
            setRoleSaving(false);
            notifyLock.current?.(false);
        }
    }
    function prepareAdd(item: AuthorizationDirectoryEmployee) {
        select(item);
        setDraftActor('');
        setDraftActive(true);
    }
    function prepareDelete(item: AuthorizationDirectoryEmployee) {
        select(item);
        setDraftActor('');
        setDraftActive(false);
        setDirty(true);
        setNotice('Удаление подготовлено. Нажмите «Сохранить связь».');
        notifyLock.current?.(true);
    }
    function openCreate() {
        if (!canManageRoles || locked || createSaving)
            return;
        setCreateOpen(true);
        setCreateCode('');
        setCreateName('');
        setCreateActor('');
        setCreateActive(true);
        setCreateError(null);
        createAttempt.current = null;
    }
    function closeCreate() {
        if (createSaving)
            return;
        setCreateOpen(false);
        setCreateError(null);
        createAttempt.current = null;
    }
    async function createEmployee(event: FormEvent) {
        event.preventDefault();
        if (!canManageRoles || locked || createSaving)
            return;
        if (createAttempt.current === null) {
            const code = createCode.trim();
            const name = createName.trim();
            const actorExternalId = createActor.trim() === '' ? null : createActor.trim();
            if (code === '' || name === '' || (createActive && actorExternalId === null)) {
                setCreateError('Укажите код, ФИО и доменную учётную запись активного сотрудника.');
                return;
            }
            try {
                createAttempt.current = {
                    key: crypto.randomUUID(),
                    input: { code, name, actorExternalId, isActive: createActive },
                };
            }
            catch {
                setCreateError('Не удалось подготовить создание. Повторите попытку.');
                return;
            }
        }
        const sent = createAttempt.current;
        if (sent === null)
            return;
        setCreateSaving(true);
        setCreateError(null);
        try {
            await createAuthorizationDirectoryEmployee(sent.input, sent.key);
            createAttempt.current = null;
            setCreateOpen(false);
            setNotice('Сотрудник добавлен. Он доступен в справочниках владельцев и участников ЦФО.');
            setSkip(0);
            setRevision(value => value + 1);
        }
        catch (failure) {
            setCreateError(authorizationDirectoryCreateErrorMessage(failure));
            if (isAuthorizationDirectoryResultUncertain(failure)) {
                // Keep the exact request and idempotency key for a safe retry.
                return;
            }
            createAttempt.current = null;
        }
        finally {
            setCreateSaving(false);
        }
    }
    function reload() {
        if (saving || roleSaving || cfoLocked)
            return;
        generation.current += 1;
        setSelectedId(null);
        setDirty(false);
        setUncertain(false);
        setRefreshRequired(false);
        setNotice(null);
        attempt.current = null;
        notifyLock.current?.(false);
        setRevision(value => value + 1);
    }
    function markDirty() {
        setDirty(true);
        setNotice(null);
        notifyLock.current?.(true);
    }
    async function save(event: FormEvent) {
        event.preventDefault();
        if (!canManageRoles || selected === null || saving || roleSaving || cfoLocked || loading || refreshRequired)
            return;
        if (!uncertain && !dirty)
            return;
        if (attempt.current === null) {
            const actorExternalId = draftActor.trim() === '' ? null : draftActor.trim();
            if (draftActive && actorExternalId === null) {
                setError('Для активной связи укажите доменную учётную запись.');
                return;
            }
            try {
                attempt.current = {
                    key: crypto.randomUUID(),
                    input: {
                        expectedConcurrencyStamp: selected.concurrencyStamp,
                        actorExternalId,
                        isActive: draftActive,
                    },
                };
            }
            catch {
                setError('Не удалось подготовить сохранение. Повторите попытку.');
                return;
            }
        }
        const sent = attempt.current;
        if (sent === null)
            return;
        setSaving(true);
        setError(null);
        try {
            const result = await updateAuthorizationDirectoryActor(selected.employeeId, sent.input, sent.key);
            setPage(current => current === null ? current : {
                ...current,
                items: current.items.map(item => item.employeeId.toLowerCase() === result.employeeId.toLowerCase()
                    ? result : item),
            });
            setDraftActor(result.actorExternalId ?? '');
            setDraftActive(result.actorMappingIsActive);
            setDirty(false);
            setUncertain(false);
            setRefreshRequired(false);
            setNotice('Связь AD сохранена.');
            attempt.current = null;
            notifyLock.current?.(false);
        }
        catch (failure) {
            const unknown = isAuthorizationDirectoryResultUncertain(failure);
            setUncertain(unknown);
            setRefreshRequired(!unknown && !isAuthorizationDirectoryValidationError(failure));
            setError(authorizationDirectoryErrorMessage(failure));
            if (isAuthorizationDirectoryConflict(failure)) {
                setNotice('Запись изменилась. Загрузите актуальное состояние перед новой правкой.');
            }
            if (!unknown)
                attempt.current = null;
        }
        finally {
            setSaving(false);
        }
    }
    return (<div className="directory-page">
      <h1>Сотрудники и их роли</h1>
      {canManageRoles && <section className="directory-create" aria-labelledby="authorization-directory-create-title">
        <div className="directory-create-header">
          <h2 id="authorization-directory-create-title">Новый сотрудник</h2>
          {!createOpen && <button type="button" disabled={locked || createSaving} onClick={openCreate}>
            Добавить сотрудника
          </button>}
        </div>
        {createOpen && <form onSubmit={event => void createEmployee(event)}>
          <div className="directory-create-fields">
            <label>Код сотрудника
              <input value={createCode} maxLength={20} autoComplete="off" onChange={event => setCreateCode(event.target.value)} required/>
            </label>
            <label>ФИО
              <input value={createName} maxLength={150} autoComplete="name" onChange={event => setCreateName(event.target.value)} required/>
            </label>
            <label>AD-учётная запись{createActive ? '' : ' (необязательно для неактивного сотрудника)'}
              <input value={createActor} maxLength={256} autoComplete="off" placeholder="AKMT\\user" onChange={event => setCreateActor(event.target.value)} required={createActive}/>
            </label>
            <label className="directory-create-check">
              <input type="checkbox" checked={createActive} onChange={event => {
                    const active = event.target.checked;
                    setCreateActive(active);
                    if (!active)
                        setCreateActor('');
                }}/>
              Сотрудник активен
            </label>
          </div>
          {createError && <p className="directory-error" role="alert">{createError}</p>}
          <div className="cash-flow-settings-actions">
            <button type="submit" disabled={createSaving}>
              {createSaving ? 'Сохранение…' : 'Создать сотрудника'}
            </button>
            <button type="button" disabled={createSaving} onClick={closeCreate}>Отмена</button>
          </div>
        </form>}
      </section>}
      <label className="directory-search">
        Поиск сотрудника по коду или имени
        <input type="search" value={query} maxLength={200} disabled={locked} onChange={event => { setQuery(event.target.value); setSkip(0); setSelectedId(null); }}/>
      </label>
      {loading && <p role="status">Загрузка сотрудников…</p>}
      {error && <p className="directory-error" role="alert">{error}</p>}
      {notice && <p role="status">{notice}</p>}
      {page?.items.length === 0 && !loading && <p role="status">Сотрудники не найдены.</p>}
      {page !== null && page.items.length > 0 && (<div className="directory-table-wrap">
          <table className="directory-table">
            <caption>Сотрудники и доменные связи</caption>
            <thead><tr><th scope="col">Сотрудник</th><th scope="col">Роль</th><th scope="col">ЦФО</th><th scope="col">Состояние</th><th scope="col">Действие</th></tr></thead>
            <tbody>{page.items.map(item => (<tr key={item.employeeId}>
                <td>{item.name}{!item.isActive && ' (неактивен)'}</td>
                <td>{item.roles.length > 0 ? item.roles.join(', ') : 'Не назначена'}</td>
                <td>{cfoAssignments.filter(a => a.isActive && a.employeeId === item.employeeId).map(a => a.costCenterName + (a.roleCode === 'CFO_INITIATOR' ? ' (инициатор)' : ' (владелец)')).join('; ') || '—'}</td>
                <td>{item.actorMappingIsActive ? 'Активна' : 'Неактивна'}</td>
                <td className="directory-actions">
                  {canManageRoles && !item.actorMappingIsActive && <button type="button" className="directory-item-link" disabled={locked} onClick={() => prepareAdd(item)}>Добавить</button>}
                  {canManageRoles && item.actorMappingIsActive && <button type="button" className="directory-item-link" disabled={locked} onClick={() => prepareDelete(item)}>Удалить</button>}
                  <button type="button" className="directory-item-link" disabled={locked} onClick={() => select(item)}>{canEdit ? 'Редактировать' : 'Просмотреть'}</button>
                </td>
              </tr>))}</tbody>
          </table>
        </div>)}
      {page !== null && <nav aria-label="Страницы списка сотрудников">
        <ReferenceDirectoryPager skip={page.skip} take={page.take} total={page.total} loading={loading || locked} hasPreviousPage={page.skip > 0} hasNextPage={page.skip + page.take < page.total} onPreviousPage={() => setSkip(Math.max(0, page.skip - pageSize))} onNextPage={() => setSkip(page.skip + pageSize)}/>
      </nav>}
      {selected !== null && (<section className="cash-flow-settings" aria-labelledby="authorization-directory-editor-title" aria-busy={saving || roleSaving}>
          <h2 id="authorization-directory-editor-title" ref={editorTitle} tabIndex={-1}>
            {canEdit ? 'Редактирование сотрудника' : 'Просмотр сотрудника'}
          </h2>
          <p><strong>{selected.code}</strong> — {selected.name}</p>
          {(canEdit || cfoLocked) && <EmployeeCfoEditor key={selected.employeeId} employeeId={selected.employeeId} disabled={!canEdit || loading || dirty || saving || uncertain || refreshRequired || roleSaving} onChanged={() => setRevision(v => v + 1)} onLocked={setCfoLocked}/>}
          <form onSubmit={event => void save(event)}>
            <fieldset disabled={cfoLocked || !canManageRoles || saving || roleSaving || refreshRequired}>
              <legend>Идентификация Windows</legend>
              <label className="cash-flow-settings-select">
                Каноническая учётная запись DOMAIN\\user
                <input value={draftActor} maxLength={256} autoComplete="off" onChange={event => { setDraftActor(event.target.value); markDirty(); }}/>
              </label>
              <label>
                <input type="checkbox" checked={draftActive} onChange={event => { setDraftActive(event.target.checked); markDirty(); }}/>
                Связь активна
              </label>
            </fieldset>
            {canManageRoles && <div className="cash-flow-settings-actions">
              <button type="submit" disabled={cfoLocked || saving || roleSaving || refreshRequired || (!dirty && !uncertain)}>
                {saving ? 'Сохранение…' : uncertain ? 'Повторить то же сохранение' : 'Сохранить связь'}
              </button>
              <button type="button" disabled={cfoLocked || saving || roleSaving} onClick={reload}>Загрузить актуальное состояние</button>
            </div>}
            {uncertain && <p className="directory-note" role="note">
              Результат записи не подтверждён. Повтор использует ту же команду и тот же ключ.
            </p>}
          </form>
          {canManageRoles && <form onSubmit={event => { event.preventDefault(); void saveRole(); }}>
            <fieldset disabled={locked}>
              <legend>Роли сотрудника</legend>
              {selected.roles.length > 0 && <p className="directory-note">Текущие роли: {selected.roles.join(', ')}</p>}
              <label className="cash-flow-settings-select">
                Роль
                <select value={roleId} disabled={roleOptions.length === 0} onChange={event => setRoleId(event.target.value)}>
                  <option value="">Выберите роль</option>
                  {roleOptions.map(role => <option key={role.roleId} value={role.roleId}>{role.name}</option>)}
                </select>
              </label>
              <label>
                <input type="checkbox" checked={roleActive} onChange={event => setRoleActive(event.target.checked)}/>
                Роль активна
              </label>
            </fieldset>
            {roleError && <p className="directory-error" role="alert">{roleError}</p>}
            {roleNotice && <p role="status">{roleNotice}</p>}
            <div className="cash-flow-settings-actions">
              <button type="submit" disabled={locked || !roleId || roleOptions.length === 0}>
                {roleSaving ? 'Сохранение…' : 'Сохранить роль'}
              </button>
            </div>
          </form>}
        </section>)}
    </div>);
}
