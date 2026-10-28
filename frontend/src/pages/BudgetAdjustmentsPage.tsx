import { useEffect, useRef, useState } from 'react';
import { Check, CircleX, RotateCcw, Trash2 } from 'lucide-react';
import { listBudgetInstances, type BudgetInstanceListItem } from '../api/budgetInstances';
import { AdjustmentError, adjustmentStates, getAdjustment, getAdjustmentBasis, listAdjustments, writeAdjustment, type AdjustmentArticle, type AdjustmentBasis, type AdjustmentDoc, type AdjustmentMove, type AdjustmentPage } from '../api/budgetAdjustments';
import { useCommandLock } from '../components/WorkspaceCommandLockContext';
import { useWorkspaceRefreshGuard, useWorkspaceRefreshRevision } from '../components/WorkspaceRefreshContext';
import { MoneyInput } from '../components/expense-request-form/MoneyInput';
import { exactMoneyNumber } from '../utils/moneyInput';
import { formatMoney } from '../utils/expenseRequestDraft';
import { openableTableRow } from '../utils/openableTableRow';
import { budgetAdjustmentNumber } from '../utils/budgetAdjustmentNumber';
import { adjustmentErrorMessage } from '../utils/uiErrorMessages';
import './BudgetAdjustmentsPage.css';
type MoveDraft = {
    source: string;
    kind: 'month' | 'article';
    targetArticle: string;
    targetMonth: string;
    amount: string;
};
type Attempt = {
    path: string;
    method: 'POST' | 'PUT';
    body: unknown;
    key: string;
};
const blank = (): MoveDraft => ({ source: '', kind: 'month', targetArticle: '', targetMonth: '', amount: '' });
const sourceKey = (id: string, month: string, direction: number) => [id, month, direction].join('|');
const reason = adjustmentErrorMessage;
export function BudgetAdjustmentsPage({ baseBudgetId, adjustmentId, onChanged, onNavigationLockChange }: {
    baseBudgetId?: string;
    adjustmentId?: string;
    onChanged?: () => void;
    onNavigationLockChange?: (locked: boolean) => void;
}) {
    const refresh = useWorkspaceRefreshRevision();
    const [page, setPage] = useState<AdjustmentPage | null>(null), [skip, setSkip] = useState(0), [revision, setRevision] = useState(0);
    const [options, setOptions] = useState<BudgetInstanceListItem[]>([]), [optionSkip, setOptionSkip] = useState(0), [optionTotal, setOptionTotal] = useState(0);
    const [selectedBase, setSelectedBase] = useState(baseBudgetId ?? ''), [basis, setBasis] = useState<AdjustmentBasis | null>(null);
    const [doc, setDoc] = useState<AdjustmentDoc | null>(null), [editing, setEditing] = useState(false);
    const [articles, setArticles] = useState<AdjustmentArticle[]>([]), [loadError, setLoadError] = useState<string | null>(null);
    const [moves, setMoves] = useState<MoveDraft[]>([blank()]), [justification, setJustification] = useState(''), [comment, setComment] = useState('');
    const [error, setError] = useState<string | null>(null), [notice, setNotice] = useState<string | null>(null), [busy, setBusy] = useState(false), [dirty, setDirty] = useState(false);
    const [uncertain, setUncertain] = useState(false), [reading, setReading] = useState(false);
    const [pendingRemoval, setPendingRemoval] = useState<number | null>(null);
    const attempt = useRef<Attempt | null>(null), readController = useRef<AbortController | null>(null);
    const openedAdjustment = useRef<string | null>(null);
    const lock = useCommandLock('budget-adjustment:' + (doc?.document.id ?? selectedBase ?? 'new'));
    const notify = useRef(onNavigationLockChange);
    notify.current = onNavigationLockChange;
    useWorkspaceRefreshGuard(dirty || uncertain || busy ? 'Сначала сохраните корректировку или подтвердите результат отправки.' : null);
    useEffect(() => { notify.current?.(dirty || uncertain || busy); return () => notify.current?.(false); }, [dirty, uncertain, busy]);
    useEffect(() => {
        const f = (e: BeforeUnloadEvent) => {
            if (dirty || uncertain || busy)
                e.preventDefault();
        };
        window.addEventListener('beforeunload', f);
        return () => window.removeEventListener('beforeunload', f);
    }, [dirty, uncertain, busy]);
    useEffect(() => {
        const c = new AbortController();
        setLoadError(null);
        void listAdjustments(baseBudgetId, skip, c.signal).then(p => {
            if (!c.signal.aborted)
                setPage(p);
        }).catch(e => {
            if (!c.signal.aborted)
                setLoadError(reason(e));
        });
        return () => c.abort();
    }, [baseBudgetId, skip, revision, refresh]);
    useEffect(() => {
        const c = new AbortController();
        void listBudgetInstances({ skip: optionSkip, take: 100 }, c.signal).then(p => {
            if (!c.signal.aborted) {
                setOptions(p.items.filter(b => b.status === 'Approved'));
                setOptionTotal(p.total);
            }
        })
            .catch(e => {
            if (!c.signal.aborted)
                setLoadError(reason(e));
        });
        return () => c.abort();
    }, [optionSkip, refresh]);
    function apply(value: AdjustmentDoc) {
        setPendingRemoval(null);
        setDoc(value);
        setSelectedBase(value.document.baseBudgetId);
        setJustification(value.reason);
        setMoves(value.transfers.map(m => ({ source: sourceKey(m.sourceArticleId, m.sourceMonth, m.flowDirection), kind: m.sourceArticleId === m.targetArticleId ? 'month' : 'article',
            targetArticle: m.targetArticleId, targetMonth: m.targetMonth.slice(0, 7), amount: String(m.amountKzt) })));
        setDirty(false);
        setError(null);
        setComment('');
    }
    async function loadBasis(id: string, signal: AbortSignal) {
        const b = await getAdjustmentBasis(id, signal);
        if (signal.aborted)
            return;
        setBasis(b);
        setArticles(b.articles);
        return b;
    }
    async function open(id?: string) {
        if (dirty || busy || uncertain || reading)
            return;
        readController.current?.abort();
        const c = new AbortController();
        readController.current = c;
        setReading(true);
        setError(null);
        try {
            if (id) {
                const d = await getAdjustment(id, c.signal);
                await loadBasis(d.document.baseBudgetId, c.signal);
                if (!c.signal.aborted) {
                    apply(d);
                    setEditing(true);
                }
            }
            else if (selectedBase) {
                const b = await loadBasis(selectedBase, c.signal);
                if (!c.signal.aborted && b) {
                    if (!b.canCreate)
                        throw new Error('Нет права создания корректировки этого ЦФО.');
                    setDoc(null);
                    setMoves([blank()]);
                    setJustification('');
                    setEditing(true);
                    setDirty(true);
                }
            }
        }
        catch (e) {
            if (!c.signal.aborted)
                setError(reason(e));
        }
        finally {
            if (!c.signal.aborted)
                setReading(false);
        }
    }
    useEffect(() => {
        if (!adjustmentId || openedAdjustment.current === adjustmentId)
            return;
        openedAdjustment.current = adjustmentId;
        void open(adjustmentId);
    }, [adjustmentId]);
    useEffect(() => () => readController.current?.abort(), []);
    const editable = editing && !busy && !reading && !uncertain && (doc ? doc.canEdit : basis?.canCreate === true);
    function update(index: number, patch: Partial<MoveDraft>) { setPendingRemoval(null); setMoves(p => p.map((m, i) => i === index ? { ...m, ...patch } : m)); setDirty(true); setNotice(null); }
    function commandMoves(): AdjustmentMove[] {
        if (!basis || !justification.trim())
            throw new Error('Укажите основание корректировки.');
        return moves.map(m => {
            const cell = basis.cells.find(c => sourceKey(c.articleId, c.month, c.direction) === m.source);
            const amount = exactMoneyNumber(m.amount, false);
            if (!cell || amount === null)
                throw new Error('Выберите источник и введите положительную сумму (не более двух знаков после запятой).');
            const targetMonth = m.kind === 'article' ? cell.month : m.targetMonth + '-01';
            const targetArticle = m.kind === 'month' ? cell.articleId : m.targetArticle;
            if (!targetArticle || targetMonth.length !== 10)
                throw new Error('Выберите получателя переноса.');
            return { sourceArticleId: cell.articleId, sourceMonth: cell.month, targetArticleId: targetArticle, targetMonth, amountKzt: amount, flowDirection: cell.direction };
        });
    }
    async function execute(action: 'save' | 'submit' | 'approve' | 'return' | 'reject') {
        if (busy)
            return;
        setPendingRemoval(null);
        let frozen = attempt.current;
        if (!frozen) {
            try {
                if (action !== 'save' && (!doc || dirty))
                    throw new Error('Сначала сохраните корректировку.');
                if (action === 'save')
                    frozen = { path: doc ? '/' + doc.document.id : '', method: doc ? 'PUT' : 'POST', key: crypto.randomUUID(),
                        body: { baseBudgetId: selectedBase, expectedStamp: doc?.document.stamp ?? null, reason: justification, transfers: commandMoves() } };
                else if (doc)
                    frozen = { path: '/' + doc.document.id + '/' + action, method: 'POST', key: crypto.randomUUID(), body: { expectedStamp: doc.document.stamp, comment } };
                else
                    throw new Error('Сначала сохраните корректировку.');
            }
            catch (e) {
                setError(reason(e));
                return;
            }
        }
        const ticket = lock.acquire();
        if (!ticket)
            return;
        attempt.current = frozen;
        setBusy(true);
        setError(null);
        setNotice(null);
        const wasUncertain = uncertain;
        let unknown = false;
        try {
            const d = await writeAdjustment(frozen.path, frozen.method, frozen.body, frozen.key);
            if (!lock.isCurrent(ticket))
                return;
            // Response is a receipt; fresh read resolves idempotent replay after later actions.
            const confirmed = await getAdjustment(d.document.id);
            if (!lock.isCurrent(ticket))
                return;
            apply(confirmed);
            setBasis(b => b ? { ...b, cells: confirmed.effectiveCells, consumption: confirmed.consumption } : b);
            attempt.current = null;
            setUncertain(false);
            setRevision(v => v + 1);
            setNotice('Операция подтверждена сервером.');
            onChanged?.();
        }
        catch (e) {
            if (!lock.isCurrent(ticket))
                return;
            unknown = wasUncertain || !(e instanceof AdjustmentError) || e.status >= 500;
            setUncertain(unknown);
            if (!unknown)
                attempt.current = null;
            setError(reason(e) + (unknown ? ' Повторите ту же попытку: введённые данные и ключ запроса сохранены.' : ''));
        }
        finally {
            if (lock.isCurrent(ticket)) {
                setBusy(false);
                if (unknown)
                    lock.retain(ticket);
                else
                    lock.release(ticket);
            }
        }
    }
    function close() {
        if (busy || uncertain || dirty)
            return;
        setEditing(false);
        setDoc(null);
        setBasis(null);
        setError(null);
        setNotice(null);
    }
    const cellLabel = (id: string) => articles.find(a => a.id === id)?.name ?? 'Статья недоступна';
    // The source identity still includes direction; only its presentation is split.
    const sources = [...new Map(basis?.cells.map(c => [c.articleId + '|' + c.direction, c]) ?? []).values()];
    const hasBothDirections = (id: string) => sources.filter(c => c.articleId === id).length > 1;
    const periodLabel = (month: string) => new Intl.DateTimeFormat('ru-RU', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(month + 'T00:00:00Z'));
    return <div className={'budget-adjustments-page' + (!editing ? ' adjustment-list-page' : '')}>
  <h1>{editing ? 'Корректировка экземпляра бюджета' + (doc ? ' № ' + budgetAdjustmentNumber(doc.document) : '') : 'Корректировки экземпляров бюджетов'}</h1>
  {loadError && <div className="notice error" role="alert">{loadError}<button onClick={() => setRevision(v => v + 1)}>Повторить загрузку</button></div>}
  {error && <div className="notice error" role="alert">{error}</div>}{notice && <p role="status">{notice}</p>}
  {!editing ? <><div className="directory-actions adjustment-list-actions">
   <label>Утверждённый экземпляр <select aria-label="Утверждённый экземпляр" value={selectedBase} onChange={e => setSelectedBase(e.target.value)}>
    <option value="">Выберите экземпляр</option>{selectedBase && !options.some(o => o.id === selectedBase) && <option value={selectedBase}>Выбранный экземпляр</option>}
    {options.map(o => <option value={o.id} key={o.id}>{o.number} — {o.costCenterName}</option>)}</select></label>
   <button disabled={!selectedBase || reading} onClick={() => void open()}>Создать корректировку</button>
   <button disabled={optionSkip === 0} onClick={() => setOptionSkip(v => Math.max(0, v - 100))}>Предыдущие экземпляры</button>
   <button disabled={optionSkip + 100 >= optionTotal} onClick={() => setOptionSkip(v => v + 100)}>Следующие экземпляры</button>
   <button onClick={() => setRevision(v => v + 1)}>Обновить список</button>
  </div>
  <div className="expense-table-wrap adjustment-list-wrap"><table className="directory-table adjustment-list-grid" aria-label="Корректировки экземпляров бюджетов"><thead><tr><th scope="col">Номер</th><th scope="col">Исходный экземпляр</th><th scope="col">ЦФО</th><th scope="col">Автор</th><th scope="col">Сумма переносов, ₸</th><th scope="col">Статус</th></tr></thead>
   <tbody>{page?.items.map(a => <tr key={a.id} {...openableTableRow('Корректировка ' + budgetAdjustmentNumber(a), () => void open(a.id), reading || busy || dirty || uncertain)}><td>{budgetAdjustmentNumber(a)}</td><td>{a.baseBudgetNumber}</td><td>{a.costCenterName}</td><td>{a.authorName}</td><td className="amount-cell">{formatMoney(a.transferAmountKzt)}</td><td>{adjustmentStates[a.state]}</td></tr>)}</tbody></table></div>
  <p>Показано: {page?.items.length ?? 0}. Всего: {page?.total ?? 0}.</p>
  <button disabled={skip === 0} onClick={() => setSkip(v => Math.max(0, v - 25))}>Назад</button><button disabled={!page || skip + 25 >= page.total} onClick={() => setSkip(v => v + 25)}>Вперёд</button>
  </> : <>
   <p>Основание: <strong>{basis?.number}</strong>. {doc ? adjustmentStates[doc.document.state] : 'Черновик'}</p>
   <div className="directory-actions document-command-bar" role="toolbar" aria-label="Действия корректировки">
    {(doc ? doc.canEdit : basis?.canCreate) && <button disabled={busy || reading || uncertain} onClick={() => void execute('save')}>Сохранить черновик</button>}
    {(doc ? doc.canSubmit : basis?.canCreate) && <button disabled={!doc || busy || reading || dirty || uncertain} title={!doc || dirty ? 'Сначала сохраните черновик корректировки' : 'Отправить сохранённую корректировку на согласование'} onClick={() => void execute('submit')}>Отправить на согласование</button>}
    {uncertain && <button disabled={busy} onClick={() => void execute('save')}>Повторить ту же попытку</button>}
    <button disabled={dirty || busy || uncertain} onClick={close}>К списку</button>
    {dirty && <button disabled={busy || uncertain} onClick={() => {
                    if (window.confirm('Отменить несохранённый ввод корректировки?')) {
                        setDirty(false);
                        setEditing(false);
                        setDoc(null);
                        setBasis(null);
                        setError(null);
                    }
                }}>Отменить ввод</button>}
    <span role="status">{busy ? 'Отправка…' : uncertain ? 'Результат неизвестен' : dirty ? 'Есть несохранённые изменения' : 'Сохранено'}</span>
   </div>
   {doc && (doc.canApprove || doc.canStop) && <section className="workflow-command-panel" aria-label="Согласование корректировки бюджета">
    <div className="workflow-command-heading"><div>
     <div className="section-title">Доступные действия</div>
     <div className="section-hint">Права и текущий этап проверяются сервером повторно.</div>
    </div></div>
    <div className="workflow-command-list">
     {doc.canApprove && <div className="workflow-command"><button className="secondary-button" data-workflow-command="APPROVE" type="button" disabled={busy || reading || uncertain} onClick={() => void execute('approve')}><Check size={16} aria-hidden="true"/>{doc.document.state === 'PendingGd' ? 'Утвердить' : 'Согласовать'}</button></div>}
     {doc.canStop && <><div className="workflow-command"><button className="secondary-button" data-workflow-command="RETURN" type="button" disabled={busy || reading || uncertain} onClick={() => void execute('return')}><RotateCcw size={16} aria-hidden="true"/>Вернуть</button></div>
      <div className="workflow-command"><button className="secondary-button" data-workflow-command="REJECT" type="button" disabled={busy || reading || uncertain} onClick={() => void execute('reject')}><CircleX size={16} aria-hidden="true"/>Отклонить</button></div></>}
     <label className="workflow-approval-comment360"><span>Комментарий к согласованию (необязательно)</span>
      <input type="text" disabled={busy || reading || uncertain} value={comment} maxLength={2000} placeholder="Например: в оплату на следующую неделю" onChange={e => setComment(e.target.value)}/>
     </label>
    </div>
   </section>}
   <p className="notice">Корректировка действует только после утверждения ГД. Переносится только свободный остаток; факт 1С и резервы заявок не изменяются. Проверка повторяется при утверждении.</p>
   <label className="adjustment-reason">Основание корректировки<textarea aria-label="Основание корректировки" disabled={!editable} maxLength={2000} value={justification} onChange={e => { setJustification(e.target.value); setDirty(true); }}/></label>
   <div className="expense-table-wrap"><table className="directory-table adjustment-moves" aria-label="Переносы корректировки бюджета">
   <colgroup><col className="adjustment-col-article"/><col className="adjustment-col-period"/><col className="adjustment-col-free"/><col className="adjustment-col-kind"/><col className="adjustment-col-target"/><col className="adjustment-col-amount"/><col className="adjustment-col-actions"/></colgroup>
   <thead><tr><th scope="col">Источник: статья</th><th scope="col">Источник: период</th><th scope="col">Свободный остаток сейчас, ₸</th><th scope="col">Вид переноса</th><th scope="col">Получатель</th><th scope="col">Сумма, ₸</th><th scope="col">Действия</th></tr></thead>
   <tbody>{moves.map((m, i) => {
                const cell = basis?.cells.find(c => sourceKey(c.articleId, c.month, c.direction) === m.source);
                const used = cell ? basis?.consumption.find(u => u.articleId === cell.articleId && u.month === cell.month && (u.direction ?? 0) === cell.direction) : undefined;
                const free = cell && used ? Math.max(0, cell.amountKzt - used.paidKzt - used.reservedKzt) : null;
                const [articleId, sourceMonth, direction] = m.source.split('|');
                const periods = basis?.cells.filter(c => c.articleId === articleId && String(c.direction) === direction) ?? [];
                return <tr key={i}><td><select aria-label={'Статья источника, строка ' + (i + 1)} disabled={!editable} value={articleId ? articleId + '|' + direction : ''} onChange={e => {
                        const [id, flow] = e.target.value.split('|');
                        const available = basis?.cells.filter(c => c.articleId === id && String(c.direction) === flow) ?? [];
                        const month = available.find(c => c.month === sourceMonth)?.month ?? (available.length === 1 ? available[0].month : '');
                        update(i, { source: id ? sourceKey(id, month, Number(flow)) : '', targetArticle: '', targetMonth: '' });
                    }}><option value="">Выберите статью</option>
     {sources.map(c => <option key={c.articleId + '|' + c.direction} value={c.articleId + '|' + c.direction}>
      {cellLabel(c.articleId)}{hasBothDirections(c.articleId) ? ' — ' + (c.direction === 0 ? 'Выплата' : 'Поступление') : ''}</option>)}</select></td>
     <td><select aria-label={'Период источника, строка ' + (i + 1)} disabled={!editable || !articleId} value={sourceMonth ?? ''} onChange={e => update(i, { source: sourceKey(articleId, e.target.value, Number(direction)), targetArticle: '', targetMonth: '' })}>
      <option value="">Выберите период</option>{periods.map(c => <option key={c.month} value={c.month}>{periodLabel(c.month)}</option>)}</select></td>
     <td className="amount-cell">{free === null ? '—' : formatMoney(free)}</td>
     <td><select aria-label={'Вид переноса, строка ' + (i + 1)} disabled={!editable} value={m.kind} onChange={e => update(i, { kind: e.target.value as MoveDraft['kind'], targetArticle: '', targetMonth: '' })}><option value="month">Другой месяц</option><option value="article">Другая статья</option></select></td>
     <td>{m.kind === 'month' ? <input aria-label={'Месяц получателя, строка ' + (i + 1)} type="month" min={basis?.validFrom.slice(0, 7)} max={basis?.validTo.slice(0, 7)} disabled={!editable} value={m.targetMonth} onChange={e => update(i, { targetMonth: e.target.value })}/> :
                        <select aria-label={'Статья получателя, строка ' + (i + 1)} disabled={!editable} value={m.targetArticle} onChange={e => update(i, { targetArticle: e.target.value })}><option value="">Выберите статью</option>
       {articles.filter(a => a.selectable && a.id !== cell?.articleId && (!a.flowDirection || a.flowDirection === (cell?.direction === 1 ? 'Receipt' : 'Payment'))).map(a => <option key={a.id} value={a.id}>{a.name}</option>)}</select>}</td>
     <td><MoneyInput aria-label={'Сумма переноса, строка ' + (i + 1)} disabled={!editable} value={m.amount} onValueChange={amount => update(i, { amount })}/></td>
     <td><button type="button" className={'line-delete-button' + (pendingRemoval === i ? ' is-pending' : '')} disabled={!editable || moves.length === 1} aria-label={(pendingRemoval === i ? 'Подтвердить удаление строки ' : 'Удалить строку ') + (i + 1)} title={pendingRemoval === i ? 'Нажмите ещё раз для удаления строки' : 'Удалить строку: два нажатия'} onBlur={() => setPendingRemoval(null)} onKeyDown={e => {
                        if (e.key === 'Escape')
                            setPendingRemoval(null);
                    }} onClick={() => {
                        if (!editable)
                            return;
                        if (pendingRemoval !== i) {
                            setPendingRemoval(i);
                            return;
                        }
                        setMoves(p => p.filter((_, j) => j !== i));
                        setPendingRemoval(null);
                        setDirty(true);
                        setNotice(null);
                    }}><Trash2 size={17} aria-hidden="true"/></button></td></tr>;
            })}</tbody><tfoot><tr><td colSpan={5}>Итого переносов</td><td className="amount-cell">{formatMoney(moves.reduce((sum, m) => sum + (exactMoneyNumber(m.amount, false) ?? 0), 0))}</td><td /></tr></tfoot></table></div>
   <button className="adjustment-add-move" disabled={!editable || moves.length >= 200} onClick={() => { setPendingRemoval(null); setMoves(p => [...p, blank()]); setDirty(true); }}>Добавить перенос</button>
   {doc && <section className="form-card"><h2>Маршрут и история согласования</h2><p>{doc.approverNames.join(' → ') || 'Финансовый директор → Генеральный директор'}</p>
    <ul>{doc.decisions.map((d, i) => <li key={i}>{new Date(d.atUtc).toLocaleString('ru-RU')} — {d.name}: {d.decision === 'approve' ? (d.roleCode === 'GENERAL_DIRECTOR' ? 'Утвердил(а)' : 'Согласовал(а)') : d.decision === 'return' ? 'Вернул(а) на доработку' : 'Отклонил(а)'}{d.comment ? ' — ' + d.comment : ''}</li>)}</ul>
    <p>Неотъемлемая часть экземпляра {doc.document.baseBudgetNumber}. Версии: {doc.includedVersions.join('; ') || 'Экземпляр пока не включён в версию'}.</p>
   </section>}
  </>}
 </div>;
}
