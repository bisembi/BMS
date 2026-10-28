import { useEffect, useRef, useState } from 'react';
import { Plus, RefreshCw } from 'lucide-react';
import { getStaffDoc471, getStaffOptions471, listStaff471, staffActions471, staffCommand471, StaffError471, staffStatus471, type StaffDoc471, type StaffInput471, type StaffOptions471, type StaffPage471, type StaffCommand471, type StaffAction471 } from '../api/staffSubstitution471';
import { useCommandLock } from '../components/WorkspaceCommandLockContext';
import { useWorkspaceRefreshRevision } from '../components/WorkspaceRefreshContext';
import { formatBusinessTimestamp } from '../utils/businessTime';
import { staffPeriod471 } from '../utils/staffSubstitution471';
import { staffSubstitutionError471 } from '../utils/uiErrorMessages';
import '../styles/staff-substitution471.css';
function today471() { const d = new Date(Date.now() + 5 * 3600000); return d.toISOString().slice(0, 10); }
const inputOf = (d: StaffDoc471): StaffInput471 => ({ deputyEmployeeId: d.deputyEmployeeId, roleCode: d.roleCode, from: d.from, through: d.through ?? null });
const actionNames: Readonly<Record<string, string>> = { create: 'Сохранить черновик', update: 'Сохранить изменения', submit: 'Отправить на согласование', confirm: 'Подтвердить', reject: 'Отказать', revoke: 'Отозвать замещение' };
export function StaffSubstitutionPage471({ documentId, onOpen, onChanged, onDirty, acting = false, refreshKey = '' }: {
    documentId?: string;
    onOpen: (id?: string) => void;
    onChanged: () => void;
    onDirty: (value: boolean) => void;
    acting?: boolean;
    refreshKey?: string;
}) {
    const [skip, setSkip] = useState(0), [revision, setRevision] = useState(0), refresh = useWorkspaceRefreshRevision();
    const [page, setPage] = useState<StaffPage471 | null>(null), [error, setError] = useState('');
    useEffect(() => {
        if (documentId)
            return;
        const abort = new AbortController();
        setPage(null);
        setError('');
        void listStaff471(skip, 50, abort.signal).then(p => {
            if (abort.signal.aborted)
                return;
            const last = Math.max(0, Math.ceil(p.total / 50) - 1) * 50;
            if (skip > last) {
                setSkip(last);
                return;
            }
            setPage(p);
        })
            .catch(e => {
            if (!abort.signal.aborted)
                setError(staffSubstitutionError471(e));
        });
        return () => abort.abort();
    }, [documentId, skip, revision, refresh, refreshKey]);
    if (documentId)
        return <StaffDocument471 key={documentId} id={documentId} acting={acting} onChanged={onChanged} onDirty={onDirty} onBack={() => onOpen()}/>;
    return <div className="list-page staff471"><div className="list-heading"><h1>Замещения сотрудников</h1></div>
  <div className="list-toolbar"><button type="button" className="primary-button compact" disabled={acting} onClick={() => onOpen('new')}><Plus size={15}/>Создать</button>
   <button type="button" className="secondary-button compact" onClick={() => setRevision(v => v + 1)}><RefreshCw size={15}/>Обновить</button>
   <span className="list-toolbar-spacer"/><span>{page ? `Всего: ${page.total}` : 'Загрузка…'}</span></div>
  {acting && <p className="notice">Для создания кадрового документа выберите свою роль, не роль по замещению.</p>}
  {error && <p role="alert" className="notice error">{error}</p>}
  <div className="request-grid-wrap"><table className="request-grid" aria-label="Документы замещения"><thead><tr>
   {['Документ', 'Сотрудник', 'Заместитель', 'Роль', 'Период', 'Статус'].map(x => <th scope="col" key={x}>{x}</th>)}</tr></thead>
   <tbody>{page?.items.map(d => <tr key={d.id}><td><button type="button" className="request-link" onClick={() => onOpen(d.id)}>{d.number}</button></td>
    <td>{d.principalName}</td><td>{d.deputyName}</td><td>{d.roleName}</td><td>{staffPeriod471(d.from, d.through)}</td><td>{staffStatus471[d.status]}</td></tr>)}
    {page?.items.length === 0 && <tr><td colSpan={6}>Документов замещения пока нет.</td></tr>}</tbody></table></div>
  {page && (skip > 0 || page.total > 50) && <nav className="list-toolbar" aria-label="Страницы замещений"><button type="button" disabled={!skip} onClick={() => setSkip(v => Math.max(0, v - 50))}>Назад</button>
   <span>{skip + 1}–{skip + page.items.length} из {page.total}</span><button type="button" disabled={skip + page.items.length >= page.total} onClick={() => setSkip(v => v + 50)}>Вперёд</button></nav>}
 </div>;
}
function StaffDocument471({ id, onChanged, onDirty, onBack, acting }: {
    id: string;
    onChanged: () => void;
    onDirty: (v: boolean) => void;
    onBack: () => void;
    acting: boolean;
}) {
    const [newId] = useState(() => crypto.randomUUID()), [doc, setDoc] = useState<StaffDoc471 | null>(null), [options, setOptions] = useState<StaffOptions471 | null>(null);
    const [input, setInput] = useState<StaffInput471>(() => ({ deputyEmployeeId: '', roleCode: '', from: today471(), through: today471() }));
    const [baseline, setBaseline] = useState<StaffInput471 | null>(null), [actions, setActions] = useState<StaffAction471[]>([]), [error, setError] = useState(''), [busy, setBusy] = useState(false);
    const [retry, setRetry] = useState(0), [attempt, setAttempt] = useState<{
        id: string;
        action: string;
        body: StaffCommand471;
    } | null>(null);
    const documentId = doc?.id ?? (id === 'new' ? newId : id), lock = useCommandLock(documentId), pending = useRef(false), mounted = useRef(true);
    const dirtyCallback = useRef(onDirty);
    dirtyCallback.current = onDirty;
    const dirty = baseline === null ? !!(input.deputyEmployeeId || input.roleCode) : JSON.stringify(input) !== JSON.stringify(baseline);
    const canEdit = !acting && (!doc || doc.commands.includes('update'));
    useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
    useEffect(() => { dirtyCallback.current(dirty || attempt !== null); }, [dirty, attempt]);
    useEffect(() => () => dirtyCallback.current(false), []);
    useEffect(() => {
        const abort = new AbortController();
        setError('');
        const loadId = doc?.id ?? (id === 'new' ? null : id);
        void Promise.all([getStaffOptions471(abort.signal), loadId === null ? Promise.resolve(null) : getStaffDoc471(loadId, abort.signal)]).then(([o, d]) => {
            if (abort.signal.aborted)
                return;
            setOptions(o);
            setDoc(d);
            const initial = d ? inputOf(d) : { deputyEmployeeId: '', roleCode: o.roles.length === 1 ? o.roles[0].code : '', from: today471(), through: today471() };
            setInput(initial);
            setBaseline(initial);
        }).catch(e => {
            if (!abort.signal.aborted)
                setError(staffSubstitutionError471(e));
        });
        return () => abort.abort();
    }, [id, retry]);
    useEffect(() => {
        if (!doc)
            return;
        const abort = new AbortController();
        void staffActions471(doc.id, abort.signal).then(v => {
            if (!abort.signal.aborted)
                setActions(v);
        }).catch(() => { });
        return () => abort.abort();
    }, [doc?.id, doc?.version]);
    function change(patch: Partial<StaffInput471>) {
        if (canEdit && !busy && !attempt)
            setInput(v => ({ ...v, ...patch }));
    }
    const valid = !!input.deputyEmployeeId && !!input.roleCode && !!input.from && (input.through === null || !!input.through && input.through >= input.from);
    async function send(action: string) {
        if (pending.current || acting)
            return;
        const currentAttempt = attempt ?? { id: documentId, action, body: { commandId: crypto.randomUUID(), expectedVersion: doc?.version ?? 0,
                ...((action === 'create' || action === 'update') ? { input: { ...input } } : {}) } };
        const ticket = lock.acquire();
        if (!ticket)
            return;
        pending.current = true;
        setBusy(true);
        setError('');
        setAttempt(currentAttempt);
        try {
            const saved = await staffCommand471(currentAttempt.id, currentAttempt.action, currentAttempt.body);
            if (!lock.isCurrent(ticket))
                return;
            setDoc(saved);
            const next = inputOf(saved);
            setInput(next);
            setBaseline(next);
            setAttempt(null);
            lock.release(ticket);
            onChanged();
        }
        catch (e) {
            if (!lock.isCurrent(ticket))
                return;
            setError(staffSubstitutionError471(e));
            if (e instanceof StaffError471 && [400, 403, 404, 409, 422].includes(e.status)) {
                setAttempt(null);
                lock.release(ticket);
            }
            else
                lock.retain(ticket);
        }
        finally {
            pending.current = false;
            if (mounted.current)
                setBusy(false);
        }
    }
    return <div className="list-page staff471"><div className="list-heading"><h1>Замещение сотрудника{doc ? ' № ' + doc.number : ''}</h1><span>{doc ? staffStatus471[doc.status] : 'Новый документ'}</span></div>
  <div className="list-toolbar"><button type="button" className="secondary-button compact" disabled={busy || attempt !== null} onClick={onBack}>К списку</button>
   {doc && <button type="button" className="secondary-button compact" disabled={busy || attempt !== null} onClick={() => {
                if (!dirty || window.confirm('Отменить несохранённые изменения и обновить документ?'))
                    setRetry(v => v + 1);
            }}>Обновить документ</button>}
   {canEdit && <button type="button" className="primary-button compact" disabled={busy || attempt !== null || !valid || !options || !dirty && doc !== null} onClick={() => void send(doc ? 'update' : 'create')}>{doc ? 'Сохранить изменения' : 'Сохранить черновик'}</button>}
   {!acting && doc?.commands.filter(c => c !== 'update').map(action => <button type="button" key={action} className={action === 'confirm' ? 'approve-button compact' : action === 'reject' ? 'reject-button compact' : 'secondary-button compact'} disabled={busy || attempt !== null || dirty} onClick={() => {
                if (action !== 'revoke' || window.confirm('Отозвать замещение? Дальнейшие действия в переданной роли будут недоступны.'))
                    void send(action);
            }}>{actionNames[action]}</button>)}
  </div>
  {error && <p className="notice error" role="alert">{error}</p>}
  {attempt && !busy && <div className="notice" role="status"><span>Повтор использует ту же команду и не создаст второй документ.</span><button type="button" onClick={() => void send(attempt.action)}>Повторить ту же отправку</button></div>}
  {(!options || error && !doc && id !== 'new') && <button type="button" disabled={busy || attempt !== null} onClick={() => setRetry(v => v + 1)}>Повторить загрузку документа</button>}
  {acting && <p className="notice">Для действий с кадровым документом переключитесь на свою роль.</p>}
  <section className="form-card"><div className="staff471-fields">
   <label>Сотрудник<input value={doc?.principalName ?? options?.employeeName ?? ''} readOnly/></label>
   <label>Роль<select aria-label="Передаваемая роль" value={input.roleCode} disabled={!canEdit || busy || attempt !== null} onChange={e => change({ roleCode: e.target.value })}>
    <option value="">Выберите роль</option>{doc && !options?.roles.some(r => r.code === doc.roleCode) && <option value={doc.roleCode}>{doc.roleName}</option>}
    {options?.roles.map(r => <option key={r.code} value={r.code}>{r.name}</option>)}</select></label>
   <label>Заместитель<select aria-label="Заместитель" value={input.deputyEmployeeId} disabled={!canEdit || busy || attempt !== null} onChange={e => change({ deputyEmployeeId: e.target.value })}>
    <option value="">Выберите сотрудника</option>{doc && !options?.employees.some(e => e.id === doc.deputyEmployeeId) && <option value={doc.deputyEmployeeId}>{doc.deputyName}</option>}
    {options?.employees.map(e => <option key={e.id} value={e.id}>{e.name}</option>)}</select></label>
   <div className="staff471-dates"><label>С даты<input aria-label="Дата начала замещения" type="date" value={input.from} max="9998-12-31" disabled={!canEdit || busy || attempt !== null} onChange={e => change({ from: e.target.value })}/></label>
    <label>По дату включительно<input aria-label="Дата окончания замещения" type="date" value={input.through ?? ''} min={input.from} max="9998-12-31" disabled={!canEdit || busy || attempt !== null || input.through === null} onChange={e => change({ through: e.target.value })}/></label></div>
   <label className="staff471-indefinite"><input type="checkbox" checked={input.through === null} disabled={!canEdit || busy || attempt !== null} onChange={e => change({ through: e.target.checked ? null : input.from })}/>На неопределённый срок</label>
  </div><p className="muted">Собственная роль сохраняется. Заместитель получит права только после подтверждения и с указанной даты; передать их дальше нельзя.</p>
   {doc?.status === 'CONFIRMED' && <p>После начала срока полученная роль доступна в переключателе «Роль» с пометкой, кого вы замещаете.</p>}
  </section>
  {actions.length > 0 && <section className="form-card"><h2>Действия заместителя</h2><table className="request-grid" aria-label="Действия по замещению"><thead><tr><th scope="col">Дата</th><th scope="col">Исполнитель</th><th scope="col">Кого замещает</th><th scope="col">Роль</th><th scope="col">Действие</th></tr></thead>
   <tbody>{actions.map(a => <tr key={a.id}><td>{formatBusinessTimestamp(a.atUtc)}</td><td>{a.actorName}</td><td>{a.principalName}</td><td>{a.roleName}</td><td>{a.action}</td></tr>)}</tbody></table></section>}
 </div>;
}
