import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useState, type FormEvent } from 'react';
import { ChevronLeft, ChevronRight, RefreshCw } from 'lucide-react';
import { getChangeJournal, type ChangeJournalFilters, type ChangeJournalPageData } from '../api/changeJournal';
import '../styles/change-journal.css';
const dates = new Intl.DateTimeFormat('ru-RU', { timeZone: 'Etc/GMT-5', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' });
const count = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 });
export function ChangeJournalPage() {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [from, setFrom] = useState('');
    const [to, setTo] = useState('');
    const [category, setCategory] = useState('');
    const [filters, setFilters] = useState<ChangeJournalFilters>({});
    const [skip, setSkip] = useState(0);
    const [take, setTake] = useState(50);
    const [page, setPage] = useState<ChangeJournalPageData | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [revision, setRevision] = useState(0);
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        const controller = new AbortController();
        setLoading(true);
        setError(null);
        setPage(null);
        void getChangeJournal(filters, skip, take, controller.signal)
            .then(value => {
            if (!controller.signal.aborted)
                setPage(value);
        })
            .catch(() => {
            if (!controller.signal.aborted)
                setError('Не удалось загрузить журнал изменений.');
        })
            .finally(() => {
            if (!controller.signal.aborted)
                setLoading(false);
        });
        return () => controller.abort();
    }, [filters, skip, take, revision, workspaceRefresh]);
    const apply = (event: FormEvent) => {
        event.preventDefault();
        if (from && to && from > to) {
            setError('Начало периода не может быть позже конца.');
            return;
        }
        const next: ChangeJournalFilters = {};
        if (from)
            next.fromUtc = new Date(`${from}T00:00:00+05:00`).toISOString();
        if (to)
            next.toUtc = new Date(new Date(`${to}T00:00:00+05:00`).getTime() + 86400000).toISOString();
        if (category)
            next.category = category;
        setSkip(0);
        setFilters(next);
    };
    return <div className="list-page change-journal-page" aria-label="Журнал изменений">
    <div className="list-heading"><div><h1>Журнал изменений</h1></div></div>
    <form className="change-journal-filters" onSubmit={apply}>
      <label>Период с<input type="date" value={from} onChange={e => setFrom(e.target.value)}/></label>
      <label>по<input type="date" value={to} onChange={e => setTo(e.target.value)}/></label>
      <label>Раздел<select value={category} onChange={e => setCategory(e.target.value)}>
        <option value="">Все изменения</option><option value="references">Справочники</option>
        <option value="employees">Сотрудники и роли</option><option value="budgets">Бюджеты</option>
        <option value="expenses">Расходные заявки</option><option value="accruals">Начисления</option>
        <option value="other">Прочее</option>
      </select></label>
      <button type="submit" className="secondary-button compact" disabled={loading}>Применить</button>
      <button type="button" className="secondary-button compact" disabled={loading} onClick={() => {
            setFrom('');
            setTo('');
            setCategory('');
            setSkip(0);
            setFilters({});
        }}>Сбросить</button>
    </form>
    <div className="change-journal-toolbar">
      <button type="button" className="secondary-button compact" disabled={loading} onClick={() => setRevision(n => n + 1)}>
        <RefreshCw size={15} aria-hidden="true"/>Обновить
      </button>
      <label>Строк на странице<select value={take} disabled={loading} onChange={e => { setSkip(0); setTake(Number(e.target.value)); }}>
        <option value={50}>50</option><option value={100}>100</option><option value={500}>500</option>
      </select></label>
      {page && <span role="status">{page.total > 0
                ? `Строки ${count.format(skip + 1)}–${count.format(skip + page.items.length)} из ${count.format(page.total)}`
                : 'Записей: 0'}</span>}
      {(skip > 0 || page && page.total > take) && <><button type="button" className="secondary-button compact" disabled={loading || skip === 0} onClick={() => setSkip(Math.max(0, skip - take))}><ChevronLeft size={15} aria-hidden="true"/>Предыдущая страница</button>
      <button type="button" className="secondary-button compact" disabled={loading || !page || skip + take >= page.total} onClick={() => setSkip(skip + take)}>Следующая страница<ChevronRight size={15} aria-hidden="true"/></button></>}
    </div>
    {loading && <p role="status">Загрузка журнала…</p>}
    {error && <p className="notice error" role="alert">{error}</p>}
    {page && <div className="change-journal-table-wrap" tabIndex={0} aria-label="Таблица журнала изменений">
      <table className="change-journal-table"><caption>Изменения BMS · время UTC+05:00</caption>
        <thead><tr>{['Дата и время', 'Автор', 'Событие', 'Объект', 'Описание'].map(label => <th key={label} scope="col">{label}</th>)}</tr></thead>
        <tbody>{page.items.map((row, index) => <tr key={`${skip + index}:${row.occurredAtUtc}`}>
          <td>{dates.format(new Date(row.occurredAtUtc))}</td><td>{row.author}</td><td>{row.event}</td>
          <td>{row.object}</td><td>{row.description || '—'}</td>
        </tr>)}{page.items.length === 0 && <tr><td colSpan={5}>Изменения за выбранный период не найдены.</td></tr>}</tbody>
      </table>
    </div>}
  </div>;
}
