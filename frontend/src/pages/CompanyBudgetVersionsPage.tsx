import { useWorkspaceRefreshRevision, useWorkspaceRefreshGuard, useWorkspaceRefreshBusy, useWorkspaceRefreshAction } from '../components/WorkspaceRefreshContext';
import { useEffect, useRef, useState } from 'react';
import { openableTableRow } from '../utils/openableTableRow';
import { useCommandLock } from '../components/WorkspaceCommandLockContext';
import { CompanyBudgetActivationDialog } from '../components/CompanyBudgetActivationDialog';
import { CompanyBudgetDeleteDialog } from '../components/CompanyBudgetDeleteDialog';
import { CompanyBudgetVersionHistoryPanel } from '../components/CompanyBudgetVersionHistoryPanel';
import { createOperationKey } from '../utils/operationKey';
import { formatBudgetPeriodDisplay } from '../utils/budgetPeriodDisplay';
import { CompanyBudgetVersionApiError, companyBudgetVersionErrorMessage, deleteCompanyBudgetVersion, getCompanyBudgetVersion, getCompanyBudgetVersionOptions, isCompanyBudgetResultUncertain, listCompanyBudgetVersions, writeCompanyBudgetVersion, publicCompanyBudgetDeleteReceipt, type CompanyBudgetActivationInput, type CompanyBudgetActiveCompositionInput, type CompanyBudgetInstance, type CompanyBudgetVersion, type CompanyBudgetDeleteInput, type CompanyBudgetVersionItem, type CompanyBudgetVersionInput, type CompanyBudgetVersionOptions, type CompanyBudgetVersionPage } from '../api/companyBudgetVersions';
import '../styles/company-budget-versions.css';
type Attempt = {
    kind: 'create' | 'update' | 'activate' | 'revision' | 'active-composition';
    id: string | null;
    key: string;
    input: CompanyBudgetVersionInput | CompanyBudgetActivationInput | CompanyBudgetActiveCompositionInput;
} | {
    kind: 'delete';
    id: string;
    key: string;
    input: CompanyBudgetDeleteInput;
    name: string;
    instanceCount: number;
};
type ActivationPrompt = Pick<CompanyBudgetVersion, 'id' | 'name' | 'concurrencyStamp' | 'activeVersionId' | 'activeConcurrencyStamp'>;
type DeletePrompt = Pick<CompanyBudgetVersionItem, 'id' | 'name' | 'concurrencyStamp' | 'instanceCount' | 'isActive' | 'isPublished'> & {
    activeConcurrencyStamp: string;
};
const take = 25;
const same = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();
function InlineComposition({ id, disabled, onOpenInstance, onOpenAdjustments }: {
    id: string;
    disabled: boolean;
    onOpenInstance: (id: string, number: string) => void;
    onOpenAdjustments?: (id: string) => void;
}) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [detail, setDetail] = useState<CompanyBudgetVersion | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [revision, setRevision] = useState(0);
    useWorkspaceRefreshBusy(detail === null && error === null);
    useEffect(() => {
        const controller = new AbortController();
        setDetail(null);
        setError(null);
        void getCompanyBudgetVersion(id, controller.signal).then(value => {
            if (!controller.signal.aborted)
                setDetail(value);
        })
            .catch(cause => {
            if (!controller.signal.aborted)
                setError(companyBudgetVersionErrorMessage(cause));
        });
        return () => controller.abort();
    }, [id, revision, workspaceRefresh]);
    if (error)
        return <div role="alert">{error} <button type="button" onClick={() => setRevision(value => value + 1)}>Повторить состав</button></div>;
    if (!detail)
        return <p role="status">Загрузка состава…</p>;
    return <div className="company-version-inline-composition">
    {detail.scopeLimited && <p>Показан состав только доступных ЦФО.</p>}
    <ul aria-label={`Состав версии ${detail.name}`}>{detail.instances.map(row => <li key={row.id} className={row.status === 'Approved' ? 'company-version-composition-approved' : undefined}>
      <button type="button" className="directory-item-link" disabled={disabled} onClick={() => onOpenInstance(row.id, row.number)}>{row.number}</button>
      {' — '}{row.costCenterName}; {formatBudgetPeriodDisplay(row.validFrom, row.validTo)};
      {' '}{row.status === 'Approved' ? 'Утверждён' : 'Черновик'}
      {row.status === 'Approved' && onOpenAdjustments && <>; <button type="button" className="directory-item-link" disabled={disabled} onClick={() => onOpenAdjustments(row.id)}>Корректировки</button></>}
    </li>)}</ul>
    {detail.overlaps.length > 0 && <p>Пересечения: {detail.overlaps.length}. Предупреждения доступны при открытии версии.</p>}
  </div>;
}
export function CompanyBudgetVersionsPage({ onOpenInstance, onNavigationLockChange, onOpenAdjustments }: {
    onOpenAdjustments?: (id: string) => void;
    onOpenInstance: (id: string, number: string) => void;
    onNavigationLockChange?: (locked: boolean) => void;
}) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [mode, setMode] = useState<'list' | 'form'>('list');
    const [versions, setVersions] = useState<CompanyBudgetVersionPage | null>(null);
    const [skip, setSkip] = useState(0);
    const [expandedId, setExpandedId] = useState<string | null>(null);
    const [revision, setRevision] = useState(0);
    const [current, setCurrent] = useState<CompanyBudgetVersion | null>(null);
    const [revisionSource, setRevisionSource] = useState<CompanyBudgetVersion | null>(null);
    const [historyVisible, setHistoryVisible] = useState(false);
    const [name, setName] = useState('');
    const [selected, setSelected] = useState<Map<string, CompanyBudgetInstance>>(new Map());
    const [options, setOptions] = useState<CompanyBudgetVersionOptions | null>(null);
    const [optionSkip, setOptionSkip] = useState(0);
    const [query, setQuery] = useState('');
    const [centerId, setCenterId] = useState('');
    const [loading, setLoading] = useState(true);
    const [optionsLoading, setOptionsLoading] = useState(false);
    const [activationAllowed, setActivationAllowed] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [optionsError, setOptionsError] = useState<string | null>(null);
    const [saving, setSaving] = useState(false);
    const [uncertain, setUncertain] = useState(false);
    const [refreshRequired, setRefreshRequired] = useState(false);
    const [dirty, setDirty] = useState(false);
    const [notice, setNotice] = useState<string | null>(null);
    const [warningConfirmed, setWarningConfirmed] = useState(false);
    const [activationPrompt, setActivationPrompt] = useState<ActivationPrompt | null>(null);
    const activationPromptRef = useRef<ActivationPrompt | null>(null);
    const [deletePrompt, setDeletePrompt] = useState<DeletePrompt | null>(null);
    const deletePromptRef = useRef<DeletePrompt | null>(null);
    const attempt = useRef<Attempt | null>(null);
    const receiptId = useRef<string | null>(null);
    const inflight = useRef(false);
    const mounted = useRef(true);
    const detailRead = useRef<AbortController | null>(null);
    const lock = useCommandLock('company-budget-version');
    const notify = useRef(onNavigationLockChange);
    notify.current = onNavigationLockChange;
    useWorkspaceRefreshGuard(dirty || saving || uncertain || activationPrompt !== null || deletePrompt !== null
        ? 'Сначала сохраните или отмените изменения версии и закройте диалог; неподтверждённую команду нужно повторить.' : null);
    useWorkspaceRefreshBusy(loading || optionsLoading);
    useWorkspaceRefreshAction(() => {
        if (mode === 'form' && current)
            return open(current.id);
    });
    useEffect(() => { setExpandedId(null); }, [mode, skip, revision]);
    useEffect(() => {
        mounted.current = true;
        return () => {
            mounted.current = false;
            detailRead.current?.abort();
            notify.current?.(false);
        };
    }, []);
    useEffect(() => { notify.current?.(dirty || saving || uncertain || activationPrompt !== null || deletePrompt !== null); }, [dirty, saving, uncertain, activationPrompt, deletePrompt]);
    useEffect(() => {
        if (mode !== 'list')
            return;
        const controller = new AbortController();
        setLoading(true);
        setVersions(null);
        setError(null);
        void listCompanyBudgetVersions(skip, take, controller.signal).then(value => {
            if (controller.signal.aborted)
                return;
            if (skip > 0 && skip >= value.total) {
                setSkip(value.total === 0 ? 0 : Math.floor((value.total - 1) / take) * take);
                return;
            }
            setVersions(value);
        }).catch(cause => {
            if (!controller.signal.aborted)
                setError(companyBudgetVersionErrorMessage(cause));
        })
            .finally(() => {
            if (!controller.signal.aborted)
                setLoading(false);
        });
        return () => controller.abort();
    }, [mode, skip, revision, workspaceRefresh]);
    useEffect(() => {
        if (mode !== 'list')
            return;
        setActivationAllowed(false);
        if (!versions?.canCreate || versions.scopeLimited)
            return;
        const controller = new AbortController();
        // List canActivate also includes composition eligibility. Options exposes
        // the server-authorized role capability, without granting it from a role name.
        void getCompanyBudgetVersionOptions({ skip: 0, take: 1 }, controller.signal)
            .then(value => {
            if (!controller.signal.aborted)
                setActivationAllowed(value.canActivate);
        })
            .catch(() => {
            if (!controller.signal.aborted)
                setActivationAllowed(false);
        });
        return () => controller.abort();
    }, [mode, versions, revision, workspaceRefresh]);
    useEffect(() => {
        if (mode !== 'form' || (current && !current.canEdit && !current.canEditActiveComposition && !revisionSource))
            return;
        const controller = new AbortController();
        setOptionsLoading(true);
        setOptions(null);
        setOptionsError(null);
        void getCompanyBudgetVersionOptions({ skip: optionSkip, take, query: query.trim(), costCenterId: centerId || undefined }, controller.signal)
            .then(value => {
            if (!controller.signal.aborted) {
                if (optionSkip > 0 && optionSkip >= value.total) {
                    setOptionSkip(0);
                    return;
                }
                setOptions(value);
            }
        }).catch(cause => {
            if (!controller.signal.aborted)
                setOptionsError(companyBudgetVersionErrorMessage(cause));
        })
            .finally(() => {
            if (!controller.signal.aborted)
                setOptionsLoading(false);
        });
        return () => controller.abort();
    }, [mode, current, revisionSource, optionSkip, query, centerId, revision, workspaceRefresh]);
    function apply(value: CompanyBudgetVersion) {
        setRevisionSource(null);
        setHistoryVisible(false);
        setCurrent(value);
        setName(value.name);
        setSelected(new Map(value.instances.map(row => [row.id.toLowerCase(), row])));
        setMode('form');
        setDirty(false);
        setWarningConfirmed(false);
        setRefreshRequired(false);
    }
    async function open(id: string, revise = false) {
        if (inflight.current || uncertain || activationPromptRef.current !== null || deletePromptRef.current !== null)
            return;
        detailRead.current?.abort();
        const controller = new AbortController();
        detailRead.current = controller;
        setLoading(true);
        setError(null);
        setNotice(null);
        try {
            const value = await getCompanyBudgetVersion(id, controller.signal);
            if (!controller.signal.aborted && mounted.current) {
                if (revise && (!versions?.canCreate || versions.scopeLimited || !value.isPublished || value.isPartial || value.scopeLimited)) {
                    setError('Создать ревизию этой версии нельзя. Перечитайте список.');
                    return;
                }
                apply(value);
                if (revise)
                    beginRevision(value);
            }
        }
        catch (cause) {
            if (!controller.signal.aborted && mounted.current)
                setError(companyBudgetVersionErrorMessage(cause));
        }
        finally {
            if (!controller.signal.aborted && mounted.current)
                setLoading(false);
        }
    }
    function start() {
        if (!versions?.canCreate || inflight.current || uncertain || activationPromptRef.current !== null || deletePromptRef.current !== null)
            return;
        setMode('form');
        setCurrent(null);
        setRevisionSource(null);
        setHistoryVisible(false);
        setName('');
        setSelected(new Map());
        setDirty(false);
        setQuery('');
        setCenterId('');
        setOptionSkip(0);
        setOptions(null);
        setError(null);
        setNotice(null);
        setRefreshRequired(false);
        setWarningConfirmed(false);
        attempt.current = null;
        receiptId.current = null;
    }
    const blocked = saving || uncertain || loading || refreshRequired || activationPrompt !== null || deletePrompt !== null;
    const activeEditing = !revisionSource && current?.canEditActiveComposition === true;
    const editable = !blocked && (revisionSource ? options?.canCreate === true : current ? current.canEdit || activeEditing : options?.canCreate === true);
    function beginRevision(value: CompanyBudgetVersion) {
        if (!versions?.canCreate || versions.scopeLimited || !value.isPublished || value.isPartial || value.scopeLimited)
            return;
        setRevisionSource(value);
        setName(`${value.name.slice(0, 190)} — ревизия`);
        setDirty(true);
        setQuery('');
        setCenterId('');
        setOptionSkip(0);
        setOptions(null);
        setWarningConfirmed(false);
        setHistoryVisible(false);
        setNotice(null);
        setError(null);
        attempt.current = null;
        receiptId.current = null;
    }
    function toggle(row: CompanyBudgetInstance) {
        if (!editable || (activeEditing && row.status !== 'Approved'))
            return;
        setSelected(previous => {
            const next = new Map(previous);
            const key = row.id.toLowerCase();
            if (next.has(key))
                next.delete(key);
            else
                next.set(key, row);
            return next;
        });
        setDirty(true);
        setWarningConfirmed(false);
        setNotice(null);
    }
    function back() {
        if (inflight.current || uncertain || activationPromptRef.current !== null || deletePromptRef.current !== null
            || (dirty && !window.confirm('Закрыть редактор без сохранения состава версии?')))
            return;
        detailRead.current?.abort();
        setMode('list');
        setCurrent(null);
        setRevisionSource(null);
        setHistoryVisible(false);
        setDirty(false);
        setError(null);
        setNotice(null);
        setRefreshRequired(false);
        attempt.current = null;
        receiptId.current = null;
        setRevision(value => value + 1);
    }
    async function perform(frozen: Attempt) {
        if (inflight.current || activationPromptRef.current !== null || deletePromptRef.current !== null)
            return;
        const ticket = lock.acquire();
        if (!ticket)
            return;
        const hadUncertainOutcome = uncertain;
        inflight.current = true;
        attempt.current = frozen;
        setSaving(true);
        setError(null);
        setNotice(null);
        let unknown = false;
        try {
            if (frozen.kind === 'delete') {
                const expected = { name: frozen.name, retainedInstanceCount: frozen.instanceCount };
                const deleted = publicCompanyBudgetDeleteReceipt(await deleteCompanyBudgetVersion(frozen.id, frozen.input, frozen.key, expected), frozen.id, expected);
                if (!lock.isCurrent(ticket))
                    return;
                // A validated DELETE receipt confirms the command. A separate list GET may fail without making deletion unknown.
                setMode('list');
                setCurrent(null);
                setRevisionSource(null);
                setHistoryVisible(false);
                setSelected(new Map());
                setVersions(null);
                setExpandedId(null);
                setDirty(false);
                setUncertain(false);
                setRefreshRequired(false);
                attempt.current = null;
                receiptId.current = null;
                setNotice(`Версия «${deleted.name}» удалена. Экземпляры бюджетов сохранены: ${deleted.retainedInstanceCount}.`);
                setRevision(value => value + 1);
                return;
            }
            if (receiptId.current === null) {
                const saved = await writeCompanyBudgetVersion(frozen.kind, frozen.id, frozen.input, frozen.key);
                receiptId.current = saved.id;
            }
            // Fresh GET confirms committed state; a lost readback retains the same command/key.
            const confirmed = await getCompanyBudgetVersion(receiptId.current);
            if (!lock.isCurrent(ticket))
                return;
            apply(confirmed);
            setUncertain(false);
            attempt.current = null;
            receiptId.current = null;
            setRevision(value => value + 1);
            setNotice(frozen.kind === 'activate' ? (confirmed.isActive ? 'Действующая версия подтверждена сервером.'
                : 'Операция подтверждена; действующая версия уже изменена другим действием.') : frozen.kind === 'revision'
                ? 'Создана новая неутверждённая ревизия. Исходная версия не изменена; связь сохранена в истории.' : frozen.kind === 'active-composition'
                ? 'Действующий состав сохранён, лимиты пересчитаны. История сохранена; факт и резервы не изменены.' : 'Состав версии сохранён.');
        }
        catch (cause) {
            if (!lock.isCurrent(ticket))
                return;
            // A refusal on retry cannot prove whether the ORIGINAL uncertain command committed.
            unknown = hadUncertainOutcome || receiptId.current !== null || isCompanyBudgetResultUncertain(cause);
            setUncertain(unknown);
            if (!unknown) {
                attempt.current = null;
                if (cause instanceof CompanyBudgetVersionApiError && [401, 403, 404, 409].includes(cause.status))
                    setRefreshRequired(true);
            }
            let message = companyBudgetVersionErrorMessage(cause);
            if (!unknown && frozen.kind === 'activate' && cause instanceof CompanyBudgetVersionApiError && cause.status === 400) {
                // A budget can change after the preflight GET. The atomic server refusal
                // remains authoritative; read its current composition for an actionable message.
                try {
                    const fresh = await getCompanyBudgetVersion(frozen.id!);
                    if (!lock.isCurrent(ticket))
                        return;
                    apply(fresh);
                    message = unapprovedMessage(fresh) ?? message;
                }
                catch { /* Preserve the confirmed refusal when its explanatory GET fails. */ }
            }
            if (!lock.isCurrent(ticket))
                return;
            setError(receiptId.current !== null ? 'Команда подтверждена сервером, но текущее состояние не прочитано. Повторите подтверждение.' : message);
        }
        finally {
            inflight.current = false;
            if (lock.isCurrent(ticket)) {
                setSaving(false);
                if (unknown)
                    lock.retain(ticket);
                else
                    lock.release(ticket);
            }
        }
    }
    function save() {
        if (!editable || optionsLoading || !name.trim() || selected.size === 0 || selected.size > 1000)
            return;
        if (activeEditing && current) {
            if (!dirty || [...selected.values()].some(row => row.status !== 'Approved'))
                return;
            try {
                void perform({ kind: 'active-composition', id: current.id, key: createOperationKey(), input: {
                        budgetInstanceIds: [...selected.values()].map(row => row.id), expectedConcurrencyStamp: current.concurrencyStamp,
                        expectedActiveConcurrencyStamp: current.activeConcurrencyStamp
                    } });
            }
            catch {
                setError('Не удалось подготовить безопасный ключ операции.');
            }
            return;
        }
        try {
            void perform({ kind: revisionSource ? 'revision' : current ? 'update' : 'create', id: revisionSource?.id ?? current?.id ?? null, key: createOperationKey(),
                input: { name: name.trim(), budgetInstanceIds: [...selected.values()].map(row => row.id),
                    ...(revisionSource || current ? { expectedConcurrencyStamp: (revisionSource ?? current)!.concurrencyStamp } : {}) } });
        }
        catch {
            setError('Не удалось подготовить безопасный ключ операции.');
        }
    }
    function canActivateCurrent() {
        return !saving && !uncertain && !loading && !refreshRequired && !dirty && !inflight.current
            && current !== null && (current.canActivate || activationAllowed) && !current.isActive && !current.scopeLimited && !current.isPartial;
    }
    function unapprovedMessage(value: CompanyBudgetVersion) {
        const unapproved = value.instances.filter(row => row.status !== 'Approved');
        return unapproved.length > 0 ? `Версия не утверждена. Не утверждены экземпляры бюджета: ${unapproved.map(row => row.number).join(', ')}.` : null;
    }
    function prepareActivation(value: CompanyBudgetVersion) {
        const message = unapprovedMessage(value);
        if (message) {
            setError(message);
            return;
        }
        if (value.instances.length === 0) {
            setError('Нельзя утвердить версию без экземпляров бюджета.');
            return;
        }
        if (value.overlaps.some(row => row.winnerBudgetInstanceId === null) || (value.overlaps.length > 0 && !warningConfirmed)) {
            setError('Проверьте пересечения состава и подтвердите выбранные экземпляры перед утверждением.');
            return;
        }
        const prompt = { id: value.id, name: value.name, concurrencyStamp: value.concurrencyStamp,
            activeVersionId: value.activeVersionId, activeConcurrencyStamp: value.activeConcurrencyStamp };
        activationPromptRef.current = prompt;
        setActivationPrompt(prompt);
        setError(null);
    }
    async function approveFromList(value: CompanyBudgetVersionItem) {
        if (blocked || inflight.current || !activationAllowed || value.isActive || value.isPartial || versions?.scopeLimited)
            return;
        detailRead.current?.abort();
        const controller = new AbortController();
        detailRead.current = controller;
        setLoading(true);
        setError(null);
        setNotice(null);
        setWarningConfirmed(false);
        try {
            const fresh = await getCompanyBudgetVersion(value.id, controller.signal);
            if (controller.signal.aborted || !mounted.current)
                return;
            apply(fresh);
            if (fresh.isActive || fresh.scopeLimited || fresh.isPartial) {
                setError('Состояние версии изменилось. Обновите список.');
                return;
            }
            // Intersections must be acknowledged on the freshly opened card, not
            // carried over from another version.
            if (fresh.overlaps.length > 0) {
                setError(unapprovedMessage(fresh) ?? 'Проверьте пересечения состава перед утверждением.');
                return;
            }
            prepareActivation(fresh);
        }
        catch (cause) {
            if (!controller.signal.aborted && mounted.current)
                setError(companyBudgetVersionErrorMessage(cause));
        }
        finally {
            if (!controller.signal.aborted && mounted.current)
                setLoading(false);
        }
    }
    function activate() {
        if (activationPromptRef.current !== null || deletePromptRef.current !== null || !canActivateCurrent() || !current)
            return;
        prepareActivation(current);
    }
    function cancelActivation() { activationPromptRef.current = null; setActivationPrompt(null); }
    function confirmActivation() {
        const prompt = activationPromptRef.current;
        if (!prompt)
            return;
        cancelActivation();
        // Recheck current capabilities/definition at the actual confirmation; the server rechecks atomically too.
        if (!canActivateCurrent() || !current || unapprovedMessage(current) || current.instances.length === 0
            || current.overlaps.some(row => row.winnerBudgetInstanceId === null) || (current.overlaps.length > 0 && !warningConfirmed)
            || !same(current.id, prompt.id) || current.name !== prompt.name
            || !same(current.concurrencyStamp, prompt.concurrencyStamp) || !same(current.activeVersionId, prompt.activeVersionId)
            || !same(current.activeConcurrencyStamp, prompt.activeConcurrencyStamp)) {
            setRefreshRequired(true);
            setWarningConfirmed(false);
            setError('Состояние версии изменилось. Перечитайте его перед назначением.');
            return;
        }
        try {
            void perform({ kind: 'activate', id: current.id, key: createOperationKey(), input: {
                    expectedConcurrencyStamp: current.concurrencyStamp, expectedActiveConcurrencyStamp: current.activeConcurrencyStamp
                } });
        }
        catch {
            setError('Не удалось подготовить безопасный ключ операции.');
        }
    }
    const managerCanDelete = versions?.canCreate === true && !versions.scopeLimited;
    function canDeleteVersion(value: CompanyBudgetVersionItem) {
        // Re-read the source capabilities at confirmation; the render-time boolean only controls chrome visibility.
        return versions?.canCreate === true && !versions.scopeLimited
            && value.canDelete === true && !value.isPartial
            && !saving && !uncertain && !loading && !refreshRequired && !dirty && !inflight.current;
    }
    function requestDeletion(value: CompanyBudgetVersionItem) {
        if (activationPromptRef.current !== null || deletePromptRef.current !== null || !canDeleteVersion(value))
            return;
        const activeStamp = mode === 'form' ? current?.activeConcurrencyStamp : versions?.activeConcurrencyStamp;
        if (!activeStamp)
            return;
        const prompt = Object.freeze({ id: value.id, name: value.name, concurrencyStamp: value.concurrencyStamp,
            instanceCount: value.instanceCount, isActive: value.isActive, isPublished: value.isPublished, activeConcurrencyStamp: activeStamp });
        deletePromptRef.current = prompt;
        setDeletePrompt(prompt);
        setError(null);
    }
    function cancelDeletion() { deletePromptRef.current = null; setDeletePrompt(null); }
    function confirmDeletion(replacement?: CompanyBudgetDeleteInput) {
        const prompt = deletePromptRef.current;
        if (!prompt)
            return;
        cancelDeletion();
        const value = mode === 'list' ? versions?.items.find(row => same(row.id, prompt.id)) : current;
        if (!value || !canDeleteVersion(value) || !same(value.id, prompt.id) || value.name !== prompt.name
            || !same(value.concurrencyStamp, prompt.concurrencyStamp) || value.instanceCount !== prompt.instanceCount
            || value.isActive !== prompt.isActive || value.isPublished !== prompt.isPublished
            || (prompt.isActive && (!replacement?.replacementVersionId
                || replacement.expectedActiveConcurrencyStamp !== prompt.activeConcurrencyStamp))
            || (!prompt.isActive && replacement !== undefined)
            || (mode === 'form' && current?.scopeLimited)) {
            setRefreshRequired(true);
            setError('Состояние версии изменилось. Перечитайте его перед удалением.');
            return;
        }
        try {
            void perform({ kind: 'delete', id: prompt.id, name: prompt.name, instanceCount: prompt.instanceCount,
                key: createOperationKey(), input: Object.freeze({ ...replacement, expectedConcurrencyStamp: prompt.concurrencyStamp }) });
        }
        catch {
            setError('Не удалось подготовить безопасный ключ операции.');
        }
    }
    function trash(value: CompanyBudgetVersionItem) {
        if (!managerCanDelete)
            return null;
        return <button type="button" className="company-version-delete-button" aria-label={`Удалить версию ${value.name}`} title={value.canDelete !== true ? (value.isActive ? 'Нужна другая утверждённая версия и право назначения действующего бюджета.'
                : 'Удаление этой версии недоступно.') : value.isActive ? 'Удалить с выбором утверждённой замены.' : `Удалить версию ${value.name}`} disabled={blocked || !canDeleteVersion(value)} onClick={() => requestDeletion(value)}>
      <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
        <path d="M3 6h18M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7M14 10v7"/>
      </svg>
    </button>;
    }
    const rows = [...selected.values()];
    const draftCount = rows.filter(row => row.status === 'Draft').length;
    function documentLink(row: CompanyBudgetInstance) {
        return <button type="button" className="directory-item-link" disabled={saving || uncertain || activationPrompt !== null || deletePrompt !== null} onClick={() => onOpenInstance(row.id, row.number)}>{row.number}</button>;
    }
    return <div className="directory-page company-budget-versions">
    <h1>Версии бюджетов</h1>
    {error && <p role="alert" className="directory-error">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    {loading && <p role="status">Загрузка версий бюджетов…</p>}
    {saving && <p role="status">Подтверждение операции…</p>}
    {uncertain && <div role="alert"><p>Результат команды неизвестен. Состав и остальные действия заблокированы до подтверждения.</p>
      <button type="button" disabled={saving} onClick={() => {
                if (attempt.current)
                    void perform(attempt.current);
            }}>Повторить ту же попытку</button></div>}
    {refreshRequired && <div role="alert"><p>Перед следующим действием перечитайте текущее состояние. Несохранённый состав будет заменён.</p>
      <button type="button" disabled={saving} onClick={() => {
                if (current)
                    void open(current.id);
                else
                    back();
            }}>Перечитать состояние</button></div>}
    {mode === 'list' ? <>
      {versions?.canCreate === true && <button type="button" disabled={blocked} onClick={start}>Создать версию бюджета</button>}
      {versions?.scopeLimited && <p role="status">Показан состав только доступных ЦФО.</p>}
      {error && <button type="button" disabled={saving || uncertain || refreshRequired || deletePrompt !== null || activationPrompt !== null} onClick={() => setRevision(value => value + 1)}>Повторить загрузку</button>}
      {versions && <><div className="directory-table-wrap"><table className="directory-table">
        <caption>Версии бюджетов</caption><thead><tr><th scope="col">Название</th><th scope="col">Статус</th>
          <th scope="col">Состав</th><th scope="col">Действие</th></tr></thead>
        <tbody>{versions.items.map(row => <tr key={row.id} {...openableTableRow(`Версия бюджета ${row.name}`, () => void open(row.id), blocked)}><td>{row.name}</td>
          <td>{row.isActive ? 'Утверждённая; действующая' : row.isPublished ? 'Ранее утверждённая; не действует' : 'Не утверждена'}
            {row.isPartial && <small> Частичный состав — только доступные ЦФО</small>}</td><td>
              <span>Документов в доступном составе: {row.instanceCount}</span>{' '}
              <button type="button" disabled={blocked} aria-expanded={expandedId === row.id} aria-label={`${expandedId === row.id ? 'Скрыть' : 'Показать'} состав ${row.name}`} onClick={() => setExpandedId(previous => previous === row.id ? null : row.id)}>
                {expandedId === row.id ? 'Скрыть состав' : 'Показать состав'}</button>
              {expandedId === row.id && <InlineComposition id={row.id} disabled={blocked} onOpenInstance={onOpenInstance} onOpenAdjustments={onOpenAdjustments}/>}
            </td>
          <td><div className="company-version-row-actions">{versions.canCreate && !versions.scopeLimited && (row.canEdit || row.canEditActiveComposition) && <button type="button" disabled={blocked} onClick={() => void open(row.id)}>Редактировать</button>}
            {versions.canCreate && !versions.scopeLimited && row.isPublished && !row.isPartial && <button type="button" disabled={blocked} onClick={() => void open(row.id, true)}>Создать ревизию</button>}
            {activationAllowed && !versions.scopeLimited && <button type="button" disabled={blocked || row.isActive || row.isPartial} onClick={() => void approveFromList(row)}>Утвердить</button>}{trash(row)}</div></td></tr>)}</tbody>
      </table></div><p role="status">Показано {versions.items.length} из {versions.total}</p>
        {(versions.total > take || skip > 0) && <><button type="button" disabled={skip === 0 || blocked} onClick={() => setSkip(value => Math.max(0, value - take))}>Назад</button>
        <button type="button" disabled={skip + take >= versions.total || blocked} onClick={() => setSkip(value => value + take)}>Вперёд</button></>}
      </>}
    </> : <>
      <div className="company-version-actions"><button type="button" disabled={saving || uncertain || activationPrompt !== null || deletePrompt !== null} onClick={back}>К списку версий</button>
        {(revisionSource || current?.canEdit || activeEditing || (!current && options?.canCreate)) && <button type="button" disabled={!editable || optionsLoading || !name.trim() || selected.size === 0 || (activeEditing && !dirty)} onClick={save}>{activeEditing ? 'Сохранить и пересчитать' : revisionSource ? 'Сохранить новую ревизию' : 'Сохранить версию'}</button>}
        {current && !revisionSource && versions?.canCreate && !versions.scopeLimited && current.isPublished && !current.isPartial && !current.scopeLimited && <button type="button" disabled={blocked || dirty} onClick={() => beginRevision(current)}>Создать ревизию</button>}
        {current && !revisionSource && versions?.canCreate && !versions.scopeLimited && !current.isPartial && !current.scopeLimited && <button type="button" disabled={blocked || dirty} aria-expanded={historyVisible} onClick={() => setHistoryVisible(value => !value)}>История версии</button>}
        {current && !revisionSource && (current.canActivate || activationAllowed) && !current.scopeLimited && !current.isPartial && <button type="button" disabled={blocked || !canActivateCurrent()} onClick={activate}>Утвердить</button>}
        {current && !revisionSource && trash(current)}
      </div>
      <label className="company-version-name">Название версии<input aria-label="Название версии" maxLength={200} value={name} disabled={!editable || activeEditing} onChange={event => { setName(event.target.value); setDirty(true); setNotice(null); }}/></label>
      {activeEditing && <p role="status">Изменение действующего состава сразу пересчитывает лимиты без повторного утверждения. Можно включать только утверждённые экземпляры. История, факт и резервы сохраняются.</p>}
      {revisionSource && <p role="status">Новая ревизия версии «{revisionSource.name}». Исходный утверждённый состав и действующая версия не изменятся. После сохранения требуется повторное утверждение.</p>}
      {current && !revisionSource && <p role="status">{current.isActive ? 'Действующая версия: источник бюджетных лимитов.'
                    : current.isPublished ? 'Опубликованный состав сохранён и закрыт для редактирования.' : 'Версия не действует.'}</p>}
      {current?.scopeLimited && <p role="status">Показан состав только доступных ЦФО. Количество документов и отчёт не являются итогом всей компании.</p>}
      <p>Один документ можно включать в несколько версий. Исходные документы не копируются и не изменяются.</p>
      <h2>Состав версии — {selected.size}</h2>
      <div className="directory-table-wrap"><table className="directory-table"><caption>Выбранные документы бюджета</caption>
        <thead><tr><th scope="col">Номер</th><th scope="col">ЦФО</th><th scope="col">Период</th><th scope="col">Статус</th><th scope="col">Действие</th></tr></thead>
        <tbody>{rows.map(row => <tr key={row.id}><td>{documentLink(row)}</td><td>{row.costCenterName}</td>
          <td>{formatBudgetPeriodDisplay(row.validFrom, row.validTo)}</td><td>{row.status === 'Approved' ? 'Утверждён' : 'Черновик'}</td>
          <td>{editable && <button type="button" onClick={() => toggle(row)} aria-label={`Исключить ${row.number}`}>Исключить</button>}</td></tr>)}</tbody>
      </table></div>
      {draftCount > 0 && <p role="status">В составе есть черновики: {draftCount}. Их можно сравнивать в отчёте; для активации все документы должны быть утверждены.</p>}
      <p>Если ЦФО и месяц пересекаются, используется один экземпляр, а не сумма бюджетов. Непокрытый месяц даёт нулевой лимит.</p>
      {dirty && <p role="status">Состав изменён. Сохраните версию для серверной проверки пересечений.</p>}
      {current && !dirty && current.overlaps.length > 0 && <section className="company-version-warning" aria-label="Пересечения состава">
        <h2>Предупреждения о пересечениях</h2><ul>{current.overlaps.map((warning, index) => <li key={`${warning.costCenterId}:${warning.budgetMonth}:${index}`}>
          {warning.costCenterName}, {warning.budgetMonth.slice(0, 7)}: {warning.warning}
          {warning.winnerBudgetInstanceId
                        ? <> Выбран сервером: {current.instances.find(row => same(row.id, warning.winnerBudgetInstanceId!))?.number ?? 'недоступный документ'}.</>
                        : <> Экземпляр для этого пересечения не определён сервером.</>}
        </li>)}</ul>
        {(current.canActivate || activationAllowed) && !current.isActive && <label><input type="checkbox" checked={warningConfirmed} disabled={blocked} onChange={event => setWarningConfirmed(event.target.checked)}/>Пересечения и выбранные экземпляры проверены</label>}
      </section>}
      {(revisionSource || activeEditing || (current ? current.canEdit : versions?.canCreate)) && <section aria-label="Выбор документов для версии">
        <h2>Добавить документы</h2><div className="directory-filters">
          <label>Номер или ЦФО<input aria-label="Поиск документов бюджета" value={query} maxLength={200} disabled={blocked} onChange={event => { setQuery(event.target.value); setOptionSkip(0); }}/></label>
          <label>ЦФО<select aria-label="ЦФО документов версии" value={centerId} disabled={blocked || optionsLoading} onChange={event => { setCenterId(event.target.value); setOptionSkip(0); }}><option value="">Все доступные</option>
            {options?.costCenters.map(center => <option key={center.id} value={center.id}>{center.name}</option>)}
          </select></label>
        </div>
        {optionsLoading && <p role="status">Загрузка доступных документов…</p>}
        {optionsError && <p role="alert">{optionsError} <button type="button" onClick={() => setRevision(value => value + 1)}>Повторить выбор документов</button></p>}
        {options && <><div className="directory-table-wrap"><table className="directory-table"><caption>Доступные экземпляры бюджета</caption>
          <thead><tr><th scope="col">В состав</th><th scope="col">Номер</th><th scope="col">ЦФО</th><th scope="col">Период</th><th scope="col">Статус</th></tr></thead>
          <tbody>{options.items.map(row => <tr key={row.id}><td><input type="checkbox" aria-label={`Включить ${row.number}`} checked={selected.has(row.id.toLowerCase())} disabled={!editable || optionsLoading || (activeEditing && row.status !== 'Approved')} onChange={() => toggle(row)}/></td>
            <td>{documentLink(row)}</td><td>{row.costCenterName}</td><td>{formatBudgetPeriodDisplay(row.validFrom, row.validTo)}</td>
            <td>{row.status === 'Approved' ? 'Утверждён' : 'Черновик'}</td></tr>)}</tbody>
        </table></div><p role="status">Доступно {options.total}; показано {options.items.length}. Выбрано {selected.size} независимо от страницы и фильтра.</p>
          {(options.total > take || optionSkip > 0) && <><button type="button" aria-label="Предыдущая страница документов" disabled={blocked || optionsLoading || optionSkip === 0} onClick={() => setOptionSkip(value => Math.max(0, value - take))}>Назад</button>
          <button type="button" aria-label="Следующая страница документов" disabled={blocked || optionsLoading || optionSkip + take >= options.total} onClick={() => setOptionSkip(value => value + take)}>Вперёд</button></>}</>}
      </section>}
    </>}
    {historyVisible && current && <CompanyBudgetVersionHistoryPanel key={current.id} id={current.id} disabled={blocked || dirty} onOpenSource={id => void open(id)}/>}
    {activationPrompt && <CompanyBudgetActivationDialog name={activationPrompt.name} onCancel={cancelActivation} onConfirm={confirmActivation}/>}
    {deletePrompt && <CompanyBudgetDeleteDialog name={deletePrompt.name} instanceCount={deletePrompt.instanceCount} activeVersion={deletePrompt.isActive ? { id: deletePrompt.id, concurrencyStamp: deletePrompt.concurrencyStamp,
                activeConcurrencyStamp: deletePrompt.activeConcurrencyStamp } : undefined} onCancel={cancelDeletion} onConfirm={confirmDeletion}/>}
  </div>;
}
