import { useEffect, useRef, useState, type FormEvent } from 'react';
import { getUsageLog, type UsagePage } from '../api/usageLog';
import { useWorkspaceRefreshRevision } from './WorkspaceRefreshContext';
const dates = new Intl.DateTimeFormat('ru-RU', { timeZone: 'Etc/GMT-5', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' });
const day = (offset = 0) => new Date(Date.now() + (5 * 3600 + offset * 86400) * 1000).toISOString().slice(0, 10);
export function usageDuration(seconds: number): string {
    return `${Math.floor(seconds / 3600)}:${String(Math.floor(seconds % 3600 / 60)).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`;
}
export function UsageLogPanel() {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [search, setSearch] = useState('');
    const [from, setFrom] = useState(() => day(-6));
    const [through, setThrough] = useState(() => day());
    const [filter, setFilter] = useState(() => ({ search: '', from: day(-6), through: day() }));
    const [skip, setSkip] = useState(0);
    const [revision, setRevision] = useState(0);
    const [page, setPage] = useState<UsagePage | null>(null);
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(true);
    const lastQuery = useRef<{
        filter: typeof filter;
        skip: number;
    } | null>(null);
    useEffect(() => {
        const controller = new AbortController();
        let timer: ReturnType<typeof setTimeout>;
        // Refresh the same mounted table: removing it resets horizontal scroll.
        // Different filters/pages must still clear stale rows immediately.
        if (lastQuery.current?.filter !== filter || lastQuery.current.skip !== skip)
            setPage(null);
        lastQuery.current = { filter, skip };
        setError('');
        setLoading(true);
        const load = async () => {
            try {
                const result = await getUsageLog(filter.search, filter.from, filter.through, skip, controller.signal);
                if (!controller.signal.aborted) {
                    if (skip > 0 && skip >= result.total)
                        setSkip(Math.max(0, Math.floor((result.total - 1) / 50) * 50));
                    else {
                        setPage(result);
                        setError('');
                    }
                }
            }
            catch {
                if (!controller.signal.aborted) {
                    setPage(null);
                    setError('Не удалось загрузить журнал времени BMS.');
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
    }, [filter, skip, revision, workspaceRefresh]);
    const apply = (event: FormEvent) => {
        event.preventDefault();
        if (from > through || (Date.parse(through) - Date.parse(from)) / 86400000 > 366) {
            setError('Укажите период не более 367 дней, дата «с» не позже даты «по».');
            return;
        }
        setSkip(0);
        setFilter({ search: search.trim(), from, through });
        setRevision(x => x + 1);
    };
    return <section aria-label="ЛОГ времени BMS">
    <p>Время открытого рабочего пространства, не время активной работы за клавиатурой. Вкладки одного сотрудника объединяются — время не суммируется.
      Если сигнала нет 3 минуты, выход указан по последнему сигналу и является приблизительным. Явный выход — закрытие последней активной вкладки через «Выйти».
      История сохраняется в БД с момента внедрения.</p>
    <form className="change-journal-filters" onSubmit={apply}>
      <label>Сотрудник<input value={search} maxLength={100} onChange={e => setSearch(e.target.value)}/></label>
      <label>С<input type="date" required value={from} onChange={e => setFrom(e.target.value)}/></label>
      <label>По<input type="date" required value={through} onChange={e => setThrough(e.target.value)}/></label>
      <button className="secondary-button compact" type="submit">Найти</button>
      <button className="secondary-button compact" type="button" onClick={() => { setSearch(''); setFrom(day(-6)); setThrough(day()); setSkip(0); setFilter({ search: '', from: day(-6), through: day() }); }}>Сбросить</button>
      <button className="secondary-button compact" type="button" onClick={() => setRevision(x => x + 1)}>Обновить</button>
    </form>
    {loading && !page && <p role="status">Загрузка…</p>}{error && <p role="alert" className="notice error">{error}</p>}
    {page && <>
      <p role="status">Сеансов: {page.total.toLocaleString('ru-RU')} · Проверено: {dates.format(new Date(page.checkedAtUtc))}</p>
      <div className="change-journal-table-wrap"><table className="change-journal-table">
        <caption>ЛОГ времени BMS · UTC+05:00 · отбор по дате входа</caption>
        <thead><tr>{['Сотрудник', 'Вход', 'Выход', 'Продолжительность', 'Состояние'].map(x => <th key={x} scope="col">{x}</th>)}</tr></thead>
        <tbody>{page.items.map(r => <tr key={r.sessionId}><td>{r.userName}</td><td>{dates.format(new Date(r.enteredAtUtc))}</td>
          <td>{r.exitedAtUtc ? dates.format(new Date(r.exitedAtUtc)) : '—'}</td><td>{usageDuration(r.durationSeconds)}</td>
          <td>{r.state === 'ACTIVE' ? 'В BMS' : r.state === 'EXIT' ? 'Выход' : 'Связь потеряна (приблизительно)'}</td></tr>)}
          {!page.items.length && <tr><td colSpan={5}>Записи не найдены.</td></tr>}</tbody>
      </table></div>
      {(page.total > 50 || skip > 0) && <div className="change-journal-toolbar">
        <button className="secondary-button compact" disabled={skip === 0} onClick={() => setSkip(x => Math.max(0, x - 50))}>Предыдущая страница</button>
        <span>Страница {Math.floor(skip / 50) + 1} из {Math.max(1, Math.ceil(page.total / 50))}</span>
        <button className="secondary-button compact" disabled={skip + 50 >= page.total} onClick={() => setSkip(x => x + 50)}>Следующая страница</button>
      </div>}
    </>}
  </section>;
}
