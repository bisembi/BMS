import { useWorkspaceRefreshRevision, useWorkspaceRefreshGuard, useWorkspaceRefreshBusy } from './WorkspaceRefreshContext';
import { useEffect, useRef, useState } from 'react';
import { getCfoState, writeCfoState, isUncertainWorkflowError, type CfoState, type CfoCommand } from '../api/cfoWorkflow';
import { useCommandLock } from './WorkspaceCommandLockContext';
export function EmployeeCfoEditor({ employeeId, disabled, onChanged, onLocked }: {
    employeeId: string;
    disabled: boolean;
    onChanged: () => void;
    onLocked?: (locked: boolean) => void;
}) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [state, setState] = useState<CfoState | null>(null);
    const [center, setCenter] = useState('');
    const [role, setRole] = useState<CfoCommand['roleCode']>('BUDGET_OWNER');
    const [error, setError] = useState('');
    const [busy, setBusy] = useState(false);
    const [unknown, setUnknown] = useState(false);
    const [reloadRequired, setReloadRequired] = useState(false);
    const [revision, setRevision] = useState(0);
    const frozen = useRef<{
        id: string;
        input: CfoCommand;
        key: string;
    } | null>(null);
    const command = useCommandLock('employee-cfo:' + employeeId);
    useWorkspaceRefreshGuard(busy || unknown || center !== '' ? 'Сначала завершите назначение ЦФО сотрудника.' : null);
    useWorkspaceRefreshBusy(state === null && error === '');
    useEffect(() => { setCenter(''); }, [employeeId]);
    useEffect(() => {
        const abort = new AbortController();
        setState(null);
        setError('');
        void getCfoState(employeeId, abort.signal).then(result => {
            if (!abort.signal.aborted) {
                setState(result);
                setReloadRequired(false);
            }
        })
            .catch(() => {
            if (!abort.signal.aborted)
                setError('Не удалось загрузить назначения ЦФО.');
        });
        return () => abort.abort();
    }, [employeeId, revision, workspaceRefresh]);
    useEffect(() => { onLocked?.(busy || unknown); return () => onLocked?.(false); }, [busy, unknown, onLocked]);
    async function save(isActive: boolean, costCenterId = center, roleCode = role) {
        if (busy || !state || reloadRequired || disabled)
            return;
        const attempt = frozen.current ?? { id: employeeId,
            input: { costCenterId, roleCode, isActive, expectedConcurrencyStamp: state.concurrencyStamp }, key: crypto.randomUUID() };
        const ticket = command.acquire();
        if (!ticket)
            return;
        frozen.current = attempt;
        setBusy(true);
        setError('');
        let retained = false, changed = false;
        try {
            const result = await writeCfoState(attempt.id, attempt.input, attempt.key);
            if (!command.isCurrent(ticket) || frozen.current !== attempt)
                return;
            setState(result);
            setUnknown(false);
            frozen.current = null;
            changed = true;
        }
        catch (cause) {
            if (!command.isCurrent(ticket) || frozen.current !== attempt)
                return;
            const uncertain = unknown || isUncertainWorkflowError(cause);
            retained = uncertain;
            setUnknown(uncertain);
            setReloadRequired(!uncertain);
            if (!uncertain)
                frozen.current = null;
            setError(uncertain ? 'Результат не подтверждён. Повторите ту же попытку.' : 'Назначение не сохранено. Обновите состояние перед новой правкой.');
        }
        finally {
            if (command.isCurrent(ticket)) {
                setBusy(false);
                if (retained)
                    command.retain(ticket);
                else
                    command.release(ticket);
            }
        }
        if (changed)
            onChanged();
    }
    return <section aria-label="ЦФО сотрудника">
    <h3>ЦФО сотрудника</h3>
    {state && <ul>{state.assignments.filter(a => a.isActive).map(a => <li key={a.costCenterId + a.roleCode}>
      {a.costCenterName} — {a.roleCode === 'BUDGET_OWNER' ? 'Владелец ЦФО' : 'Инициатор ЦФО'}
      {' '}<button type="button" disabled={disabled || busy || unknown || reloadRequired} onClick={() => void save(false, a.costCenterId, a.roleCode)}>Отключить назначение</button>
    </li>)}</ul>}
    <fieldset disabled={disabled || busy || unknown || reloadRequired || state === null}>
      <label>ЦФО <select value={center} onChange={e => setCenter(e.target.value)}>
        <option value="">Выберите ЦФО</option>{state?.costCenters.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
      </select></label>{' '}
      <label>Роль в ЦФО <select value={role} onChange={e => setRole(e.target.value as CfoCommand['roleCode'])}>
        <option value="BUDGET_OWNER">Владелец ЦФО</option><option value="CFO_INITIATOR">Инициатор ЦФО</option>
      </select></label>{' '}
      <button type="button" disabled={!center} onClick={() => void save(true)}>Добавить назначение ЦФО</button>
    </fieldset>
    {unknown && <button type="button" disabled={busy || disabled} onClick={() => void save(true)}>Повторить ту же попытку</button>}
    {reloadRequired && <button type="button" onClick={() => { setReloadRequired(false); setRevision(v => v + 1); }}>Обновить назначения</button>}
    {error && <p role="alert">{error}</p>}
  </section>;
}
