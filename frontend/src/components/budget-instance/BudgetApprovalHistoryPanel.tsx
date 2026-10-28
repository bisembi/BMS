import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../WorkspaceRefreshContext';
import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react';
import type { BudgetInstance } from '../../api/budgetInstances';
import { getBudgetHistory, type BudgetHistoryPage } from '../../api/budgetInstanceHistory';
import { budgetHistoryErrorMessage } from '../../utils/uiErrorMessages';
import './BudgetApprovalHistoryPanel.css';
import { groupHistoryEvents, loadHistoryDisplay } from './budgetHistoryDisplay';
const take = 20;
const time = new Intl.DateTimeFormat('ru-RU', { timeZone: 'Asia/Qyzylorda', day: '2-digit', month: '2-digit',
    year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
/** Both panels stay mounted: opening read-only history never resets the budget form or command attempts. */
export function BudgetApprovalHistoryPanel({ budget, children, toolbar }: {
    budget?: BudgetInstance;
    children?: ReactNode;
    toolbar?: (historyButton: ReactNode) => ReactNode;
}) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const id = useId();
    const tabs = useRef<[
        HTMLButtonElement | null,
        HTMLButtonElement | null
    ]>([null, null]);
    const historyToggle = useRef<HTMLButtonElement | null>(null);
    const [history, setHistory] = useState(false);
    const [skip, setSkip] = useState(0);
    const [revision, setRevision] = useState(0);
    const [page, setPage] = useState<BudgetHistoryPage | null>(null);
    const [rawMode, setRawMode] = useState(false);
    const requestSkip = rawMode ? skip : 0;
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState('');
    useWorkspaceRefreshBusy(loading);
    useEffect(() => { setSkip(0); setRawMode(false); }, [budget?.id]);
    useEffect(() => {
        const abort = new AbortController();
        setPage(null);
        setError('');
        setLoading(false);
        if (!history || !budget)
            return () => abort.abort();
        setLoading(true);
        const request = rawMode ? getBudgetHistory(budget.id, requestSkip, take, abort.signal)
            .then(result => ({ mode: 'raw' as const, page: result })) : loadHistoryDisplay(budget.id, abort.signal);
        void request.then(result => {
            if (!abort.signal.aborted) {
                if (result.mode === 'compact') {
                    const count = groupHistoryEvents(result.page.items).length;
                    setSkip(previous => Math.min(previous, Math.floor(Math.max(0, count - 1) / take) * take));
                }
                setPage(result.page);
                setRawMode(result.mode === 'raw');
                setLoading(false);
            }
        }).catch(cause => {
            if (!abort.signal.aborted) {
                setError(budgetHistoryErrorMessage(cause));
                setLoading(false);
            }
        });
        return () => abort.abort();
    }, [budget, history, requestSkip, rawMode, revision, workspaceRefresh]);
    const current = page?.budgetInstanceId === budget?.id && (!rawMode || page?.skip === skip) ? page : null;
    const groups = useMemo(() => current ? rawMode ? current.items.map(event => ({ firstId: event.id, event, count: 1 }))
        : groupHistoryEvents(current.items) : [], [current, rawMode]);
    const total = rawMode ? current?.total ?? 0 : groups.length;
    const visible = rawMode ? groups : groups.slice(skip, skip + take);
    const historyButton = <button ref={historyToggle} type="button" aria-pressed={history} aria-controls={id + '-panel-1'} disabled={!budget} title={!budget ? 'Сначала сохраните черновик бюджета' : undefined} onClick={() => setHistory(value => !value)}>История</button>;
    return <section className="budget-history-section" aria-label="Согласование и история бюджета">
    {toolbar ? toolbar(historyButton) : <div className="budget-history-tabs" role="tablist" aria-label="Сведения о согласовании бюджета">
      {[false, true].map((value, index) => <button key={index} ref={node => { tabs.current[index] = node; }} type="button" role="tab" id={id + '-tab-' + index} aria-controls={id + '-panel-' + index} aria-selected={history === value} tabIndex={history === value ? 0 : -1} disabled={value && !budget} title={value && !budget ? 'Сначала сохраните черновик бюджета' : undefined} onClick={() => setHistory(value)} onKeyDown={event => {
                    if (!budget || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key))
                        return;
                    event.preventDefault();
                    const next = event.key === 'Home' ? 0 : event.key === 'End' ? 1 : 1 - index;
                    setHistory(next === 1);
                    tabs.current[next]?.focus();
                }}>{value ? 'История согласований' : 'Согласование бюджета'}</button>)}
    </div>}
    <div role={toolbar ? undefined : 'tabpanel'} id={id + '-panel-0'} aria-labelledby={toolbar ? undefined : id + '-tab-0'} hidden={history}>
      {children}{!toolbar && !budget && <p>История появится после сохранения нового черновика.</p>}
    </div>
    <div role={toolbar ? 'region' : 'tabpanel'} id={id + '-panel-1'} aria-label={toolbar ? 'История бюджета' : undefined} aria-labelledby={toolbar ? undefined : id + '-tab-1'} hidden={!history}>
      {history && <>
        {rawMode && <p>Большая история показана без объединения повторов, с обычной постраничной загрузкой.</p>}
        <div className="budget-history-actions">
          <button type="button" disabled={loading} onClick={() => { setSkip(0); setRawMode(false); setRevision(v => v + 1); }}>Обновить историю</button>
          <button type="button" onClick={() => {
                setHistory(false);
                (toolbar ? historyToggle.current : tabs.current[0])?.focus();
            }}>Скрыть историю</button>
        </div>
        {loading && <p role="status">Загрузка истории согласований…</p>}
        {error && <p role="alert">{error}</p>}
        {current && <>
          {current.total === 0 ? <p>Записанных событий пока нет. Даты и авторы отсутствующих событий не подставляются.</p>
                    : <div className="budget-history-scroll" role="region" aria-label="Список событий бюджета" tabIndex={0}>
              <div className="budget-history-grid budget-history-heading" aria-hidden="true">
                <span>№</span><span>Действие</span><span>Автор</span><span>Дата и время</span><span>Комментарий</span>
              </div>
              <ol className="budget-history-events" role="list" start={skip + 1}>{visible.map(({ firstId, event: item, count }, index) => <li key={firstId} value={skip + index + 1} className="budget-history-grid budget-history-row" title={count > 1 ? `Последнее из ${count} одинаковых событий подряд` : undefined}>
                <span className="budget-history-number" aria-hidden="true">{skip + index + 1}.</span>
                <span className="budget-history-action"><strong>{item.title}</strong>{item.roleName && <> — <span>{item.roleName}</span></>}</span>
                <span className="budget-history-actor">{item.actorName}</span>
                <time className="budget-history-time" dateTime={item.occurredAtUtc}>{time.format(new Date(item.occurredAtUtc))}</time>
                <div className="budget-history-details">
                  {item.comment && <p className="budget-history-comment">{item.comment}</p>}
                  {!item.detailsAvailable && <p>Дополнительные сведения об этом событии не сохранились или недоступны.</p>}
                </div>
              </li>)}</ol>
            </div>}
          <p role="status">{total ? `События ${skip + 1}–${skip + visible.length} из ${total}` : 'Событий: 0'}</p>
          <p>Исходных записей в истории: {current.total}. {rawMode ? 'Показаны без объединения.' : `Строк после объединения: ${total}.`}</p>
          <div className="directory-actions">
            <button type="button" disabled={loading || skip === 0} onClick={() => setSkip(Math.max(0, skip - take))}>Предыдущие события</button>
            <button type="button" disabled={loading || skip + visible.length >= total} onClick={() => setSkip(skip + take)}>Следующие события</button>
          </div>
        </>}
      </>}
    </div>
  </section>;
}
