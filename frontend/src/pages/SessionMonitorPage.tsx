import { useEffect, useRef, useState, type FormEvent } from 'react';
import { getActiveSessions, type ActiveSessionsPage } from '../api/activeSessions';
import { roleLabels } from '../utils/activeRole';
import { useWorkspaceRefreshRevision } from '../components/WorkspaceRefreshContext';
import '../styles/change-journal.css';
import { UsageLogPanel } from '../components/UsageLogPanel';
import { UsageDailyPanel } from '../components/UsageDailyPanel';
const dates = new Intl.DateTimeFormat('ru-RU', { timeZone: 'Etc/GMT-5', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' });
export function SessionMonitorPage() {
    const [mode, setMode] = useState<'active' | 'log' | 'daily'>('active');
    return <div className="list-page" aria-label="Монитор состояний">
    <h1>Монитор состояний</h1>
    <div className="change-journal-toolbar" role="group" aria-label="Режим монитора">
      <button className="secondary-button compact" aria-pressed={mode === 'active'} onClick={() => setMode('active')}>Активные сессии</button>
      <button className="secondary-button compact" aria-pressed={mode === 'daily'} onClick={() => setMode('daily')}>По сотрудникам и дням</button>
      <button className="secondary-button compact" aria-pressed={mode === 'log'} onClick={() => setMode('log')}>Журнал сеансов</button>
    </div>
    {mode === 'daily' ? <UsageDailyPanel /> : mode === 'log' ? <UsageLogPanel /> : <ActiveSessionPanel />}
  </div>;
}
function ActiveSessionPanel() {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [input, setInput] = useState('');
    const [search, setSearch] = useState('');
    const [skip, setSkip] = useState(0);
    const [revision, setRevision] = useState(0);
    const [page, setPage] = useState<ActiveSessionsPage | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [loading, setLoading] = useState(true);
    const lastQuery = useRef<{
        search: string;
        skip: number;
    } | null>(null);
    useEffect(() => {
        const controller = new AbortController();
        let timer: ReturnType<typeof setTimeout>;
        // Keep the mounted table and its scroll for a same-query refresh. A new
        // filter/page must not display stale rows while its request is pending.
        if (lastQuery.current?.search !== search || lastQuery.current.skip !== skip)
            setPage(null);
        lastQuery.current = { search, skip };
        setLoading(true);
        setError(null);
        const load = async () => {
            try {
                const result = await getActiveSessions(search, skip, 50, controller.signal);
                if (!controller.signal.aborted) {
                    if (skip > 0 && skip >= result.total) {
                        setSkip(result.total === 0 ? 0 : Math.floor((result.total - 1) / 50) * 50);
                    }
                    else {
                        setPage(result);
                        setError(null);
                    }
                }
            }
            catch {
                if (!controller.signal.aborted) {
                    setPage(null);
                    setError('Не удалось загрузить активные сессии.');
                }
            }
            finally {
                if (!controller.signal.aborted) {
                    setLoading(false);
                    timer = setTimeout(() => { void load(); }, 15000);
                }
            }
        };
        void load();
        return () => { controller.abort(); clearTimeout(timer); };
    }, [search, skip, revision, workspaceRefresh]);
    const apply = (event: FormEvent) => { event.preventDefault(); setSkip(0); setSearch(input.trim()); setRevision(x => x + 1); };
    return <section aria-label="Активные сессии пользователей">
    <p>Активные сессии пользователей — открытые вкладки BMS с сигналом связи за последние 3 минуты.
      Обновление каждые 15 секунд. После перезапуска сервера список заполняется заново.
      Старые вкладки начнут учитываться после обновления страницы.</p>
    <form className="change-journal-filters" onSubmit={apply}>
      <label>Пользователь<input value={input} maxLength={100} onChange={e => setInput(e.target.value)}/></label>
      <button className="secondary-button compact" type="submit">Найти</button>
      <button className="secondary-button compact" type="button" onClick={() => { setInput(''); setSearch(''); setSkip(0); setRevision(x => x + 1); }}>Сбросить</button>
      <button className="secondary-button compact" type="button" onClick={() => setRevision(x => x + 1)}>Обновить</button>
    </form>
    {loading && !page && <p role="status">Загрузка…</p>}{error && <p role="alert" className="notice error">{error}</p>}
    {page && <>
      <p role="status">Активных сессий: {page.total.toLocaleString('ru-RU')} · Проверено: {dates.format(new Date(page.checkedAtUtc))}</p>
      <div className="change-journal-table-wrap"><table className="change-journal-table">
        <caption>Активные сессии BMS · время UTC+05:00</caption>
        <thead><tr>{['Пользователь', 'Роль', 'Подключение', 'Последний сигнал'].map(x => <th key={x} scope="col">{x}</th>)}</tr></thead>
        <tbody>{page.items.map((r, i) => <tr key={`${r.sessionId}:${i}`}><td>{r.userName}</td>
          <td>{r.roleCode ? roleLabels[r.roleCode] : 'Без выбранной роли'}</td>
          <td>{dates.format(new Date(r.connectedAtUtc))}</td><td>{dates.format(new Date(r.lastSeenAtUtc))}</td></tr>)}
          {!page.items.length && <tr><td colSpan={4}>Активные сессии не найдены.</td></tr>}</tbody>
      </table></div>
      {(page.total > 50 || skip > 0) && <div className="change-journal-toolbar">
        <button className="secondary-button compact" disabled={skip === 0} onClick={() => setSkip(x => Math.max(0, x - 50))}>Предыдущая страница</button>
        <span>Страница {Math.floor(skip / 50) + 1} из {Math.max(1, Math.ceil(page.total / 50))}</span>
        <button className="secondary-button compact" disabled={skip + 50 >= page.total} onClick={() => setSkip(x => x + 50)}>Следующая страница</button>
      </div>}
    </>}
  </section>;
}
