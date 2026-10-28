import { useWorkspaceRefreshRevision, useWorkspaceRefreshGuard, useWorkspaceRefreshBusy } from '../WorkspaceRefreshContext';
import { useEffect, useRef, useState } from 'react';
import { getCostCenterEditor, updateCostCenterEditor, costCenterEditorUncertain, costCenterEditorConflict, costCenterEditorDenied, costCenterEditorErrorMessage, createCostCenterEditor, costCenterCreateErrorMessage, type CreateCostCenterEditorInput, type CostCenterEditorState, type UpdateCostCenterEditorInput } from '../../api/costCenterEditor';
import { listBudgetDirectoryEmployees, type BudgetDirectoryOption } from '../../api/budgetDirectory';
import { useFrozenCreate } from '../../hooks/useFrozenCreate';
import './costCenterEditor.css';
export function CostCenterEditor({ id, canManage, onClose, onSaved, onLockChange, onlyCostDepartment = false }: {
    id?: string;
    canManage: boolean;
    onClose: () => void;
    onSaved: () => Promise<void>;
    onLockChange: (locked: boolean) => void;
    onlyCostDepartment?: boolean;
}) {
    const creating = id === undefined;
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [state, setState] = useState<CostCenterEditorState | null>(null);
    const [code, setCode] = useState(''), [name, setName] = useState(''), [group, setGroup] = useState('');
    const [owner, setOwner] = useState<BudgetDirectoryOption | null>(null);
    const [dirty, setDirty] = useState(false), [loading, setLoading] = useState(true);
    const [needsRefresh, setNeedsRefresh] = useState(false), [error, setError] = useState<string | null>(null);
    const [notice, setNotice] = useState<string | null>(null);
    const [employeeRevision, setEmployeeRevision] = useState(0), [denied, setDenied] = useState(false);
    const [checkingAccess, setCheckingAccess] = useState(false);
    const checking = useRef(false);
    const [employees, setEmployees] = useState<BudgetDirectoryOption[]>([]);
    const [employeeError, setEmployeeError] = useState<string | null>(null), [employeeLoading, setEmployeeLoading] = useState(false);
    const editCommand = useFrozenCreate<UpdateCostCenterEditorInput>('cost-center-editor:' + id, costCenterEditorUncertain);
    const createCommand = useFrozenCreate<CreateCostCenterEditorInput>('cost-center-create', costCenterEditorUncertain);
    const command = creating ? createCommand : editCommand;
    useWorkspaceRefreshGuard(dirty || command.blocked || checkingAccess ? 'Сначала сохраните или отмените изменения ЦФО; неподтверждённую команду нужно повторить.' : null);
    useWorkspaceRefreshBusy(loading || employeeLoading);
    const live = useRef(true), lock = useRef(onLockChange);
    lock.current = onLockChange;
    useEffect(() => { live.current = true; return () => { live.current = false; lock.current(false); }; }, []);
    useEffect(() => { lock.current(dirty || command.blocked || loading); }, [dirty, command.blocked, loading]);
    useEffect(() => {
        const controller = new AbortController();
        setLoading(true);
        setError(null);
        setState(null);
        if (!canManage) {
            setLoading(false);
            return () => controller.abort();
        }
        if (creating) {
            setCode('');
            setName('');
            setGroup('');
            setOwner(null);
            setDirty(false);
            setLoading(false);
            return () => controller.abort();
        }
        void getCostCenterEditor(id, controller.signal).then(s => {
            if (controller.signal.aborted)
                return;
            setState(s);
            setCode(s.code);
            setName(s.name);
            setGroup(s.groupingName ?? '');
            setOwner(null);
            setDirty(false);
            setNeedsRefresh(false);
            setDenied(false);
            setLoading(false);
        }).catch(e => {
            if (!controller.signal.aborted) {
                setError(costCenterEditorErrorMessage(e));
                setLoading(false);
            }
        });
        return () => controller.abort();
    }, [id, creating, canManage, workspaceRefresh]);
    const editable = canManage && !denied && (creating || state !== null) && !loading && !command.blocked && !needsRefresh;
    useEffect(() => {
        const controller = new AbortController();
        setEmployees([]);
        setEmployeeError(null);
        setEmployeeLoading(editable && !onlyCostDepartment);
        if (!editable || onlyCostDepartment)
            return () => controller.abort();
        // The selector contains the entire roster, not only the first server page.
        // Read all pages invisibly; reject a moving/duplicate catalogue rather than silently omit people.
        void (async () => {
            const all: BudgetDirectoryOption[] = [];
            let expectedTotal: number | null = null;
            do {
                const p = await listBudgetDirectoryEmployees('', all.length, 100, controller.signal);
                if (controller.signal.aborted)
                    return [];
                expectedTotal ??= p.total;
                if (p.total !== expectedTotal || p.items.length === 0 && all.length < expectedTotal)
                    throw new Error('Employee catalogue changed.');
                all.push(...p.items);
            } while (all.length < expectedTotal);
            if (new Set(all.map(e => e.id.toLowerCase())).size !== all.length)
                throw new Error('Duplicate employee.');
            return all;
        })().then(all => {
            if (controller.signal.aborted)
                return;
            setEmployees(all);
            setEmployeeLoading(false);
        }).catch(() => {
            if (!controller.signal.aborted) {
                setEmployeeError('Не удалось загрузить сотрудников.');
                setEmployeeLoading(false);
            }
        });
        return () => controller.abort();
    }, [editable, employeeRevision, workspaceRefresh, onlyCostDepartment]);
    function change(action: () => void) {
        if (!editable)
            return;
        action();
        setDirty(true);
        setNotice(null);
    }
    async function save(event: React.FormEvent) {
        event.preventDefault();
        if (!canManage || denied || checking.current || (!creating && !state) || loading || needsRefresh || command.busy || (!dirty && !command.unknown))
            return;
        setError(null);
        setNotice(null);
        const result = creating
            ? await createCommand.run({ code: code.trim(), name: name.trim(), groupingName: group || null, ownerEmployeeId: owner?.id ?? null,
                ...(onlyCostDepartment ? { onlyCostDepartment: true } : {}) }, createCostCenterEditor)
            : await editCommand.run({ code: code.trim(), name: name.trim(), groupingName: group || null, ownerSpecified: owner !== null,
                ownerEmployeeId: owner?.id ?? null, expectedConcurrencyStamp: state!.concurrencyStamp }, (input, key) => updateCostCenterEditor(id!, input, key));
        if (!live.current || result.kind === 'inactive')
            return;
        if (result.kind === 'confirmed') {
            // The write is confirmed; finish editing without waiting for a list GET.
            // Unknown outcomes still keep their draft and frozen attempt below.
            setDirty(false);
            onClose();
            void onSaved();
        }
        else {
            setError(creating ? costCenterCreateErrorMessage(result.error) : costCenterEditorErrorMessage(result.error));
            if (costCenterEditorDenied(result.error))
                setDenied(true);
            if (!creating && result.kind === 'refused' && costCenterEditorConflict(result.error))
                setNeedsRefresh(true);
        }
    }
    async function checkAccess() {
        if (!canManage || command.busy || checking.current)
            return;
        checking.current = true;
        setCheckingAccess(true);
        try {
            // Read-only authority probe. Never replaces the draft, CAS or retained attempt.
            if (creating)
                await listBudgetDirectoryEmployees('', 0, 1);
            else
                await getCostCenterEditor(id!);
            if (live.current) {
                setDenied(false);
                setError(null);
                setNotice('Доступ подтверждён. Можно повторить исходную попытку сохранения.');
            }
        }
        catch (failure) {
            if (live.current) {
                setDenied(true);
                setError(costCenterEditorErrorMessage(failure));
            }
        }
        finally {
            checking.current = false;
            if (live.current)
                setCheckingAccess(false);
        }
    }
    const title = onlyCostDepartment ? (creating ? 'Новое подразделение затрат' : 'Редактирование подразделения затрат')
        : (creating ? 'Новый ЦФО' : 'Редактирование ЦФО');
    return <section className="directory-create cost-center-editor" aria-label={title} aria-busy={loading || command.busy}>
    <h2>{title}</h2>
    {loading && <p role="status">Загрузка…</p>}
    {!canManage && <p role="alert">Нет доступа к изменению ЦФО.</p>}
    <form onSubmit={e => void save(e)}>
      {canManage && (creating || state) && <>
      <fieldset disabled={!editable} className="directory-create-fields">
        <label>Код<input required value={code} maxLength={100} onChange={e => change(() => setCode(e.target.value))}/></label>
        <label>Наименование<input required value={name} maxLength={500} onChange={e => change(() => setName(e.target.value))}/></label>
        <label>Входит в<input value={group} maxLength={500} placeholder="Произвольная группировка" onChange={e => change(() => setGroup(e.target.value))}/></label>
        {!onlyCostDepartment && <label>Владелец<select value="" onChange={e => {
                    const option = employees[Number(e.target.value)];
                    if (e.target.value !== '' && option)
                        change(() => setOwner(option));
                }}><option value="">{owner ? owner.name : creating ? 'Не назначен' : 'Оставить текущие назначения'}</option>
          {employees.map((e, i) => <option key={e.id} value={i}>{e.name}</option>)}
        </select></label>}
      </fieldset>
      {!creating && state && <p>Текущий владелец: {state.owners.length ? state.owners.map(o => o.name
                    + (!o.employeeIsActive || !o.roleIsActive ? ' (неактивен)' : '')).join(', ') : 'Не назначен'}</p>}
      {owner && <p role="status">{creating ? 'Владелец' : 'Новый владелец'}: {owner.name}.{!creating && ' Прежние назначения этого ЦФО будут заменены.'}
        <button type="button" disabled={!editable} onClick={() => change(() => setOwner(null))}>{creating ? 'Не назначать' : 'Оставить прежнего'}</button></p>}
      {employeeLoading && <p role="status">Загрузка сотрудников…</p>}
      {employeeError && <p role="alert">{employeeError}<button type="button" disabled={!editable} onClick={() => setEmployeeRevision(v => v + 1)}>Повторить загрузку</button></p>}
      </>}
      <div className="cash-flow-settings-actions cost-center-actions">
        {canManage && (creating || state) && <button className="cost-center-save" type="submit" disabled={!canManage || denied || checkingAccess || loading || command.busy || needsRefresh || (!dirty && !command.unknown)}>
          {command.busy ? 'Сохранение…' : command.unknown ? 'Повторить ту же попытку' : 'Сохранить'}</button>}
        <button className="cost-center-cancel" type="button" disabled={command.blocked} onClick={() => {
            if (!dirty || window.confirm('Отменить несохранённые изменения?'))
                onClose();
        }}>Отменить</button>
      </div>
    </form>
    {error && <p role="alert" className="directory-error">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    {canManage && denied && command.unknown && <button type="button" disabled={command.busy || checkingAccess} onClick={() => void checkAccess()}>{checkingAccess ? 'Проверка доступа…' : 'Проверить доступ'}</button>}
  </section>;
}
