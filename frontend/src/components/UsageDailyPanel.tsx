import { useEffect, useRef, useState, type FormEvent } from 'react';
import { getUsageDaily, type UsageDailyPage } from '../api/usageDaily';
import { businessDayInput, formatBusinessDay, formatBusinessTimestamp } from '../utils/businessTime';
import { useWorkspaceRefreshRevision } from './WorkspaceRefreshContext';
import { usageDuration } from './UsageLogPanel';
const today = () => businessDayInput();
const weekAgo = () => businessDayInput(new Date(Date.now() - 6 * 86400000));
export function UsageDailyPanel() {
    const refresh = useWorkspaceRefreshRevision();
    const [search, setSearch] = useState(''), [from, setFrom] = useState(weekAgo), [through, setThrough] = useState(today);
    const [filters, setFilters] = useState(() => ({ search: '', from: weekAgo(), through: today() }));
    const [skip, setSkip] = useState(0), [revision, setRevision] = useState(0);
    const [page, setPage] = useState<UsageDailyPage | null>(null), [loading, setLoading] = useState(true), [error, setError] = useState('');
    const previousQuery = useRef('');
    useEffect(() => {
        const abort = new AbortController();
        let timer: ReturnType<typeof setTimeout> | undefined;
        const key = JSON.stringify([filters, skip]);
        if (previousQuery.current !== key)
            setPage(null);
        previousQuery.current = key;
        setLoading(true);
        setError('');
        const load = async () => {
            try {
                const result = await getUsageDaily(filters.search, filters.from, filters.through, skip, abort.signal);
                if (!abort.signal.aborted) {
                    if (skip > 0 && skip >= result.total)
                        setSkip(result.total === 0 ? 0 : Math.floor((result.total - 1) / 50) * 50);
                    else
                        setPage(result);
                }
            }
            catch {
                if (!abort.signal.aborted) {
                    setPage(null);
                    setError('Не удалось загрузить сводку времени BMS.');
                }
            }
            finally {
                if (!abort.signal.aborted) {
                    setLoading(false);
                    timer = setTimeout(() => void load(), 15000);
                }
            }
        };
        void load();
        return () => { abort.abort(); clearTimeout(timer); };
    }, [filters, skip, revision, refresh]);
    const apply = (event: FormEvent) => {
        event.preventDefault();
        if (!from || !through || from > through || !Number.isFinite(Date.parse(from)) || !Number.isFinite(Date.parse(through))
            || (Date.parse(through) - Date.parse(from)) / 86400000 > 366) {
            setError('Укажите период не более 367 дней, дата «с» не позже даты «по».');
            return;
        }
        setSkip(0);
        setFilters({ search: search.trim(), from, through });
        setRevision(value => value + 1);
    };
    return <section aria-label="Время в BMS по сотрудникам и дням">
    <p>Время открытого BMS, а не фактической работы за клавиатурой. Пересекающиеся сеансы одного сотрудника объединяются,
      время через полночь разделяется по дням UTC+5. При потере связи окончание учитывается по последнему сигналу.</p>
    <form className="change-journal-filters" onSubmit={apply}>
      <label>Сотрудник<input value={search} maxLength={100} onChange={event => setSearch(event.target.value)}/></label>
      <label>С<input type="date" required value={from} onChange={event => setFrom(event.target.value)}/></label>
      <label>По<input type="date" required value={through} onChange={event => setThrough(event.target.value)}/></label>
      <button type="submit" className="secondary-button compact">Применить</button>
      <button type="button" className="secondary-button compact" onClick={() => {
            setSearch('');
            setFrom(weekAgo());
            setThrough(today());
            setSkip(0);
            setFilters({ search: '', from: weekAgo(), through: today() });
        }}>Сбросить</button>
      <button type="button" className="secondary-button compact" onClick={() => setRevision(value => value + 1)}>Обновить сводку</button>
    </form>
    {error && <p role="alert">{error}</p>}{loading && !page && <p role="status">Загрузка…</p>}
    {page && <>
      <p role="status">Записей: {page.total.toLocaleString('ru-RU')} · Проверено: {formatBusinessTimestamp(page.checkedAtUtc)}</p>
      <div className="change-journal-table-wrap"><table className="change-journal-table">
        <caption>Присутствие в BMS по сотрудникам и дням · UTC+5</caption>
        <thead><tr>{['День', 'Сотрудник', 'Первое присутствие', 'Последнее присутствие', 'Время в BMS', 'Сеансов', 'Состояние'].map(label => <th key={label} scope="col">{label}</th>)}</tr></thead>
        <tbody>{page.items.map(row => <tr key={row.summaryId + row.day}>
          <td>{formatBusinessDay(row.day)}</td><td>{row.userName}</td>
          <td><time dateTime={row.firstEntryAtUtc} title={row.firstEntryAtUtc}>{formatBusinessTimestamp(row.firstEntryAtUtc)}</time></td>
          <td><time dateTime={row.lastExitAtUtc} title={row.lastExitAtUtc}>{formatBusinessTimestamp(row.lastExitAtUtc)}</time></td>
          <td>{usageDuration(row.durationSeconds)}</td><td>{row.sessionCount.toLocaleString('ru-RU')}</td>
          <td>{row.isActive ? 'В BMS' : row.hasApproximateExit ? 'Окончание приблизительное' : 'Завершено'}</td>
        </tr>)}{page.items.length === 0 && <tr><td colSpan={7}>Записи не найдены.</td></tr>}</tbody>
      </table></div>
      {(page.total > 50 || skip > 0) && <div className="change-journal-toolbar">
        <button type="button" disabled={loading || skip === 0} onClick={() => setSkip(value => Math.max(0, value - 50))}>Предыдущая страница</button>
        <span>Страница {Math.floor(skip / 50) + 1} из {Math.max(1, Math.ceil(page.total / 50))}</span>
        <button type="button" disabled={loading || skip + 50 >= page.total} onClick={() => setSkip(value => value + 50)}>Следующая страница</button>
      </div>}
    </>}
  </section>;
}
