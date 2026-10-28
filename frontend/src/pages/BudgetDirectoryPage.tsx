import { useWorkspaceRefreshRevision, useWorkspaceRefreshGuard, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { budgetDirectoryErrorMessage, getBudgetDirectory, isBudgetDirectoryConflict, isBudgetDirectoryResultUncertain, isBudgetDirectoryValidationError, listBudgetDirectoryCostCenters, listBudgetDirectoryEmployees, updateBudgetDirectory, type BudgetDirectoryMember, type BudgetDirectoryOption, type BudgetDirectoryPageResult, type BudgetDirectorySettings, type UpdateBudgetDirectoryInput } from '../api/budgetDirectory';
import { ReferenceDirectoryPager } from '../components/reference-directory/ReferenceDirectoryPager';
import { CostCenterPurposeCheckbox457 } from '../components/reference-directory/CostCenterPurposeCheckbox457';
import './budgetDirectory457.css';
const pageSize = 25;
const maximumMembers = 1000;
const sameId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();
type OptionLoader = (query: string, skip: number, take: number, signal?: AbortSignal) => Promise<BudgetDirectoryPageResult>;
/** Each option list owns its query, request generation and cancellation. */
function useOptionPage(load: OptionLoader, enabled = true) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [query, setQueryValue] = useState('');
    const [skip, setSkipValue] = useState(0);
    const [revision, setRevision] = useState(0);
    const [page, setPage] = useState<BudgetDirectoryPageResult | null>(null);
    const [loading, setLoading] = useState(enabled);
    const [error, setError] = useState<string | null>(null);
    const generation = useRef(0);
    const active = useRef<AbortController | null>(null);
    useWorkspaceRefreshBusy(loading);
    function invalidate() {
        generation.current += 1;
        active.current?.abort();
        setPage(null);
        setError(null);
        setLoading(enabled);
    }
    useEffect(() => {
        const request = ++generation.current;
        const controller = new AbortController();
        active.current = controller;
        setPage(null);
        setError(null);
        setLoading(enabled);
        if (!enabled)
            return () => { controller.abort(); generation.current += 1; };
        void load(query, skip, pageSize, controller.signal).then(answer => {
            if (controller.signal.aborted || request !== generation.current)
                return;
            // A smaller current result can invalidate a previously legitimate page.
            // Go back to its last page rather than display a reversed range.
            if (skip > 0 && skip >= answer.total) {
                setSkipValue(answer.total === 0 ? 0 : Math.floor((answer.total - 1) / pageSize) * pageSize);
                return;
            }
            setPage(answer);
            setLoading(false);
        }).catch(failure => {
            if (controller.signal.aborted || request !== generation.current)
                return;
            setError(budgetDirectoryErrorMessage(failure));
            setLoading(false);
        });
        return () => { controller.abort(); generation.current += 1; };
    }, [enabled, load, query, revision, skip, workspaceRefresh]);
    return {
        query, skip, page, loading, error,
        setQuery(value: string) {
            if (value === query)
                return;
            invalidate();
            setQueryValue(value);
            setSkipValue(0);
        },
        setSkip(value: number) {
            const next = Math.max(0, value);
            if (next === skip)
                return;
            invalidate();
            setSkipValue(next);
        },
        reload() { invalidate(); setRevision(value => value + 1); },
    };
}
type OptionModel = ReturnType<typeof useOptionPage>;
function OptionSearch({ model, label, disabled = false }: {
    model: OptionModel;
    label: string;
    disabled?: boolean;
}) {
    return (<div className="directory-toolbar">
      <label className="directory-search">
        {label}
        <input type="search" value={model.query} maxLength={200} disabled={disabled} onChange={event => model.setQuery(event.target.value)}/>
      </label>
      <button type="button" className="secondary-button" disabled={disabled || model.query === ''} onClick={() => model.setQuery('')}>Очистить поиск</button>
    </div>);
}
function OptionPager({ model, label, disabled = false }: {
    model: OptionModel;
    label: string;
    disabled?: boolean;
}) {
    const page = model.page;
    if (!page)
        return null;
    return (<nav aria-label={label}>
      <ReferenceDirectoryPager skip={page.skip} take={page.take} total={page.total} loading={disabled || model.loading} hasPreviousPage={page.skip > 0} hasNextPage={page.skip + page.take < page.total} onPreviousPage={() => model.setSkip(page.skip - pageSize)} onNextPage={() => model.setSkip(page.skip + pageSize)}/>
    </nav>);
}
type Attempt = {
    input: UpdateBudgetDirectoryInput;
    key: string;
};
function BudgetDirectoryCard({ id, onClose, onNavigationLock, canEdit }: {
    id: string;
    onClose: () => void;
    onNavigationLock: (locked: boolean) => void;
    canEdit: boolean;
}) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [settings, setSettings] = useState<BudgetDirectorySettings | null>(null);
    const [abbreviation, setAbbreviation] = useState('');
    const [members, setMembers] = useState<BudgetDirectoryMember[]>([]);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [dirty, setDirty] = useState(false);
    const [needsRefresh, setNeedsRefresh] = useState(false);
    const [uncertain, setUncertain] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [notice, setNotice] = useState<string | null>(null);
    const [revision, setRevision] = useState(0);
    const heading = useRef<HTMLHeadingElement>(null);
    const generation = useRef(0);
    const activeRead = useRef<AbortController | null>(null);
    const mounted = useRef(false);
    const busy = useRef(false);
    const blocked = useRef(false);
    const attempt = useRef<Attempt | null>(null);
    const notifyLock = useRef(onNavigationLock);
    notifyLock.current = onNavigationLock;
    useWorkspaceRefreshGuard(dirty || saving || uncertain ? 'Сначала сохраните или отмените изменения настроек бюджета; неподтверждённую команду нужно повторить.' : null);
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        mounted.current = true;
        heading.current?.focus();
        return () => {
            mounted.current = false;
            generation.current += 1;
            activeRead.current?.abort();
            // A command already sent is never aborted by navigation/unmount.
            // Completion is ignored after unmount, and the original key is not reused for another CFO.
        };
    }, []);
    useEffect(() => {
        const request = ++generation.current;
        const controller = new AbortController();
        activeRead.current = controller;
        setLoading(true);
        setError(null);
        blocked.current = true;
        void getBudgetDirectory(id, controller.signal).then(answer => {
            if (!mounted.current || controller.signal.aborted || request !== generation.current)
                return;
            setSettings(answer);
            setAbbreviation(answer.abbreviation ?? '');
            setMembers(answer.members.map(member => ({ ...member })));
            setDirty(false);
            setNeedsRefresh(false);
            setUncertain(false);
            setNotice(previous => previous === 'Настройки сохранены. Загружается текущее состояние.'
                ? 'Настройки сохранены.' : null);
            attempt.current = null;
            blocked.current = false;
            notifyLock.current(false);
        }).catch(failure => {
            if (!mounted.current || controller.signal.aborted || request !== generation.current)
                return;
            setError(budgetDirectoryErrorMessage(failure));
            // A failed verification read does not settle a previously unknown write.
            // Keep its immutable retry available; auth/not-found refusals still lock it.
            const retryFrozenWrite = attempt.current !== null && isBudgetDirectoryResultUncertain(failure);
            setNeedsRefresh(!retryFrozenWrite);
            // A refused initial read leaves settings null, so no employee request or editor can mount.
            // A refused refresh retains dirty values, but leaves editing and saving locked.
        }).finally(() => {
            if (mounted.current && !controller.signal.aborted && request === generation.current)
                setLoading(false);
        });
        return () => { controller.abort(); generation.current += 1; };
    }, [id, revision, workspaceRefresh]);
    const current = settings !== null && sameId(settings.costCenterId, id);
    const editable = canEdit && current && !loading && !saving && !needsRefresh && !uncertain;
    const employees = useOptionPage(listBudgetDirectoryEmployees, editable);
    const activeMemberCount = members.filter(member => member.isActive).length;
    function changed() {
        setDirty(true);
        setNotice(null);
        notifyLock.current(true);
    }
    function editMember(employeeId: string, update: Partial<Pick<BudgetDirectoryMember, 'kind' | 'isActive'>>) {
        if (!editable || blocked.current || busy.current)
            return;
        const existing = members.find(member => sameId(member.employeeId, employeeId));
        if (!existing || (update.isActive === true && (!existing.employeeIsActive
            || (!existing.isActive && activeMemberCount >= maximumMembers))))
            return;
        setMembers(previous => previous.map(member => sameId(member.employeeId, employeeId)
            ? { ...member, ...update } : member));
        changed();
    }
    function addEmployee(option: BudgetDirectoryOption) {
        if (!editable || blocked.current || busy.current || !option.isActive)
            return;
        if (members.some(member => sameId(member.employeeId, option.id)) || activeMemberCount >= maximumMembers)
            return;
        setMembers(previous => previous.some(member => sameId(member.employeeId, option.id)) ? previous : [...previous, {
                employeeId: option.id, code: option.code, name: option.name,
                kind: 'PARTICIPANT', isActive: true, employeeIsActive: option.isActive,
            }]);
        changed();
    }
    function reload() {
        if (busy.current)
            return;
        generation.current += 1;
        activeRead.current?.abort();
        blocked.current = true;
        setLoading(true);
        setNotice(null);
        notifyLock.current(true);
        setRevision(value => value + 1);
    }
    async function save(event: FormEvent) {
        event.preventDefault();
        if (settings === null || !current || busy.current || loading || needsRefresh || (!dirty && !uncertain))
            return;
        if (!uncertain && (!editable || blocked.current))
            return;
        if (uncertain && attempt.current === null)
            return;
        if (!uncertain && activeMemberCount > maximumMembers) {
            setError('В одном ЦФО допускается не более 1 000 активных владельцев и участников.');
            return;
        }
        // Freeze command/key before awaiting. No list/card/picker response can mutate this attempt.
        if (attempt.current === null) {
            try {
                attempt.current = {
                    key: crypto.randomUUID(),
                    input: {
                        expectedConcurrencyStamp: settings.concurrencyStamp,
                        abbreviation,
                        // The server keeps inactive history. Only the current active set
                        // is the replacement command; omission revokes and does not erase history.
                        members: members.filter(member => member.isActive).map(member => ({
                            employeeId: member.employeeId, kind: member.kind, isActive: member.isActive,
                        })),
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
        const request = generation.current;
        busy.current = true;
        blocked.current = true;
        notifyLock.current(true);
        setSaving(true);
        setError(null);
        setNotice(null);
        try {
            // Do not give the write the read controller: changing a search or reloading cannot abort it.
            await updateBudgetDirectory(id, sent.input, sent.key);
            if (!mounted.current || request !== generation.current)
                return;
            attempt.current = null;
            setUncertain(false);
            setNotice('Настройки сохранены. Загружается текущее состояние.');
            setLoading(true);
            // An idempotency replay can carry a historical stamp. Only a fresh read enables another edit.
            setRevision(value => value + 1);
        }
        catch (failure) {
            if (!mounted.current || request !== generation.current)
                return;
            const unknown = isBudgetDirectoryResultUncertain(failure);
            const invalidInput = isBudgetDirectoryValidationError(failure);
            setUncertain(unknown);
            setNeedsRefresh(!unknown && !invalidInput);
            if (invalidInput)
                blocked.current = false;
            setError(budgetDirectoryErrorMessage(failure));
            if (isBudgetDirectoryConflict(failure)) {
                setNotice('Ваш ввод сохранён. Загрузите текущее состояние перед новой правкой.');
            }
            if (!unknown)
                attempt.current = null;
        }
        finally {
            if (mounted.current && request === generation.current) {
                busy.current = false;
                setSaving(false);
            }
        }
    }
    return (<section className="cash-flow-settings" aria-labelledby="budget-directory-card-title" aria-busy={loading || saving} onKeyDown={event => {
            if (event.key === 'Escape' && !busy.current && !dirty && !uncertain) {
                event.preventDefault();
                onClose();
            }
        }}>
      <header>
        <h2 id="budget-directory-card-title" tabIndex={-1} ref={heading}>Настройки бюджета по ЦФО</h2>
        <button type="button" disabled={saving || uncertain} onClick={() => {
            if (!busy.current && !uncertain)
                onClose();
        }}>{dirty ? 'Отменить изменения и закрыть' : 'Закрыть карточку'}</button>
      </header>
      {loading && <p role="status">Загрузка настроек ЦФО…</p>}
      {error && <p role="alert">{error}</p>}
      {notice && <p role="status">{notice}</p>}
      {current && (<form onSubmit={event => void save(event)}>
          <p><strong>{settings.name}</strong></p>
          {!settings.numberingInitialized && <p role="note">
            Нумерация для этого ЦФО ещё не подготовлена.
          </p>}
          <fieldset disabled={!editable}>
            <legend>Сокращение ЦФО и участники бюджета</legend>
            <label className="cash-flow-settings-select">
              Сокращение ЦФО
              <input value={abbreviation} required maxLength={32} autoComplete="off" aria-describedby="budget-directory-abbreviation-help" onChange={event => {
                if (!editable || blocked.current || busy.current)
                    return;
                setAbbreviation(event.target.value);
                changed();
            }}/>
            </label>
            <div className="directory-table-wrap">
              <table className="directory-table">
                <caption>Владельцы и участники бюджета этого ЦФО</caption>
                <thead><tr>
                  <th scope="col">Сотрудник</th><th scope="col">Участие</th>
                  <th scope="col">Доступ активен</th>
                </tr></thead>
                <tbody>{members.map(member => (<tr key={member.employeeId}>
                    <td>{member.code} — {member.name}
                      {!member.employeeIsActive && <p role="note">Сотрудник неактивен</p>}
                    </td>
                    <td>
                      <select value={member.kind} aria-label={`Участие: ${member.code} — ${member.name}`} disabled={!member.isActive || !member.employeeIsActive} onChange={event => editMember(member.employeeId, { kind: event.target.value === 'OWNER' ? 'OWNER' : 'PARTICIPANT' })}>
                        <option value="OWNER">Владелец</option>
                        <option value="PARTICIPANT">Участник</option>
                      </select>
                    </td>
                    <td>
                      <input type="checkbox" checked={member.isActive} disabled={!member.isActive && (!member.employeeIsActive || activeMemberCount >= maximumMembers)} aria-label={`Доступ активен: ${member.code} — ${member.name}`} onChange={event => editMember(member.employeeId, { isActive: event.target.checked })}/>
                    </td>
                  </tr>))}</tbody>
              </table>
            </div>
            {members.length === 0 && <p role="status">Владельцы и участники ещё не назначены.</p>}
            <h3>Добавить сотрудника</h3>
            <OptionSearch model={employees} label="Поиск сотрудника по коду или имени" disabled={!editable}/>
            {employees.loading && <p role="status">Загрузка сотрудников…</p>}
            {employees.error && <p role="alert">{employees.error}{' '}
              <button type="button" onClick={employees.reload}>Повторить загрузку сотрудников</button>
            </p>}
            {employees.page && employees.page.items.length === 0 && <p role="status">Сотрудники не найдены.</p>}
            {employees.page && employees.page.items.length > 0 && (<div className="directory-table-wrap">
                <table className="directory-table">
                  <caption>Результат поиска сотрудников</caption>
                  <thead><tr><th scope="col">Сотрудник</th><th scope="col">Действие</th></tr></thead>
                  <tbody>{employees.page.items.map(option => {
                    const assigned = members.some(member => sameId(member.employeeId, option.id));
                    return <tr key={option.id}>
                      <td>{option.code} — {option.name}{!option.isActive && ' (неактивен)'}</td>
                      <td><button type="button" disabled={assigned || !option.isActive || activeMemberCount >= maximumMembers} aria-label={`Добавить сотрудника: ${option.code} — ${option.name}`} onClick={() => addEmployee(option)}>{assigned ? 'Уже в списке' : 'Добавить участника'}</button></td>
                    </tr>;
                })}</tbody>
                </table>
              </div>)}
            <p role="status">Активных владельцев и участников: {activeMemberCount}</p>
            {activeMemberCount >= maximumMembers && <p role="note">Достигнут предел в 1 000 активных участников.</p>}
            <OptionPager model={employees} label="Страницы списка сотрудников" disabled={!editable}/>
          </fieldset>
          <div className="cash-flow-settings-actions">
            <button type="submit" disabled={saving || loading || needsRefresh || (!dirty && !uncertain)}>
              {saving ? 'Сохранение…' : uncertain ? 'Повторить то же сохранение' : 'Сохранить настройки'}
            </button>
            <button type="button" disabled={saving || loading} onClick={reload}>
              {dirty || uncertain ? 'Заменить ввод текущим состоянием' : 'Загрузить текущее состояние'}
            </button>
          </div>
          {uncertain && <p className="directory-note" role="note">
            Результат записи пока не подтверждён. Повтор использует прежнюю команду и тот же ключ.
            Новая правка и переход к другому ЦФО доступны после проверки результата.
          </p>}
        </form>)}
      {!current && !loading && <button type="button" onClick={reload}>Повторить загрузку настроек</button>}
    </section>);
}
export function BudgetDirectoryPage({ onNavigationLockChange, canEdit = true }: {
    onNavigationLockChange?: (locked: boolean) => void;
    canEdit?: boolean;
}) {
    const costCenters = useOptionPage(listBudgetDirectoryCostCenters);
    const [selectedId, setSelectedId] = useState<string | null>(null);
    const [navigationLocked, setNavigationLocked] = useState(false);
    const purposeLocks = useRef(new Set<string>());
    const cardLock = useRef(false);
    const navigationLock = useRef(false);
    const trigger = useRef<HTMLButtonElement | null>(null);
    const notifyNavigationLock = useRef(onNavigationLockChange);
    notifyNavigationLock.current = onNavigationLockChange;
    useEffect(() => () => { notifyNavigationLock.current?.(false); }, []);
    useEffect(() => {
        // The opener was disabled while the card was dirty. Restore focus only
        // after React has removed that disabled attribute on closing the card.
        if (selectedId === null)
            trigger.current?.focus();
    }, [selectedId]);
    function setNavigationLock(locked: boolean) {
        cardLock.current = locked;
        const combined = locked || purposeLocks.current.size > 0;
        navigationLock.current = combined;
        setNavigationLocked(combined);
        notifyNavigationLock.current?.(combined);
    }
    const setPurposeLock = useCallback((id: string, locked: boolean) => {
        if (locked)
            purposeLocks.current.add(id);
        else
            purposeLocks.current.delete(id);
        const combined = cardLock.current || purposeLocks.current.size > 0;
        navigationLock.current = combined;
        setNavigationLocked(combined);
        notifyNavigationLock.current?.(combined);
    }, []);
    function closeCard() {
        setSelectedId(null);
        setNavigationLock(false);
    }
    return (<div className="directory-page budget-directory-purpose-page">
      <h1>Настройки бюджетов по ЦФО</h1>
      {selectedId && <BudgetDirectoryCard key={selectedId} id={selectedId} onClose={closeCard} onNavigationLock={setNavigationLock} canEdit={canEdit}/>}
      <OptionSearch model={costCenters} label="Поиск ЦФО по префиксу или наименованию" disabled={navigationLocked}/>
      {costCenters.loading && <p role="status">Загрузка ЦФО…</p>}
      {costCenters.error && <p className="directory-error" role="alert">{costCenters.error}{' '}
        <button type="button" disabled={navigationLocked} onClick={costCenters.reload}>Повторить загрузку ЦФО</button>
      </p>}
      {costCenters.page && costCenters.page.items.length === 0 && <p role="status">ЦФО не найдены.</p>}
      {costCenters.page && costCenters.page.items.length > 0 && (<div className="directory-table-wrap">
          <table className="directory-table">
            <caption>ЦФО для настройки бюджетов</caption>
            <thead><tr><th scope="col">Код</th><th scope="col">Наименование</th><th scope="col">Состояние</th>
              <th scope="col">Только подразделение затрат</th></tr></thead>
            <tbody>{costCenters.page.items.map(option => (<tr key={option.id}>
                <td>{option.code}</td>
                <td><button type="button" className="directory-item-link" disabled={navigationLocked} onClick={event => {
                    if (navigationLock.current || selectedId === option.id)
                        return;
                    trigger.current = event.currentTarget;
                    setSelectedId(option.id);
                }}>{option.name}</button></td>
                <td>{option.isActive ? 'Активен' : 'Неактивен'}</td>
                <td className="cost-department-purpose-cell"><CostCenterPurposeCheckbox457 option={option} canEdit={canEdit} disabled={navigationLocked} onSaved={costCenters.reload} onLock={setPurposeLock}/></td>
              </tr>))}</tbody>
          </table>
        </div>)}
      <OptionPager model={costCenters} label="Страницы списка ЦФО" disabled={navigationLocked}/>
      {navigationLocked && <p className="directory-note">
        Сначала завершите работу с открытой карточкой ЦФО.
      </p>}
    </div>);
}
