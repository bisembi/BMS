import { useEffect, useState, type FormEvent } from 'react';
import { RefreshCw } from 'lucide-react';
import { useWorkspaceRefreshBusy, useWorkspaceRefreshRevision } from '../components/WorkspaceRefreshContext';
import { getExchangeJournal, getExchangeDetail, type ExchangeFilter, type ExchangeKind, type ExchangeStatus, type ExchangePage, type ExchangeItem, type ExchangeDetail } from '../api/exchangeJournal';
import '../styles/exchange-journal.css';
import { exchangeJournalErrorMessage as errorText } from '../utils/uiErrorMessages';
const dates = new Intl.DateTimeFormat('ru-RU', { timeZone: 'Etc/GMT-5', day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' });
const count = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 });
const money = (value: string) => {
    const m = /^(-?)(\d+)(?:\.(\d{1,8}))?$/.exec(value);
    if (!m)
        return value;
    const rounded = BigInt(m[2]) + ((m[3]?.[0] ?? '0') >= '5' ? 1n : 0n);
    return (m[1] && rounded !== 0n ? '−' : '') + count.format(rounded);
};
const statusNames = { success: 'Успешно', failed: 'Ошибка', running: 'Выполняется', warning: 'Требует проверки' };
const stamp = (value: string | null | undefined) => value ? dates.format(new Date(value)) : '—';
const day = (value: string | null | undefined) => value ? value.slice(0, 10).split('-').reverse().join('.') : '—';
const localEntryDate = (value: string) => /^\d{4}-\d{2}-\d{2}T/.test(value) ? day(value) + ' ' + value.slice(11, 19) : value;
export function ExchangeJournalPage() {
    const workspaceRevision = useWorkspaceRefreshRevision();
    const [from, setFrom] = useState(''), [to, setTo] = useState('');
    const [kind, setKind] = useState<ExchangeKind>('all'), [status, setStatus] = useState<ExchangeStatus>('all');
    const [filter, setFilter] = useState<ExchangeFilter>({ kind: 'all', status: 'all' });
    const [skip, setSkip] = useState(0), [take, setTake] = useState(50), [revision, setRevision] = useState(0);
    const [error, setError] = useState<string | null>(null), [loading, setLoading] = useState(true);
    const key = JSON.stringify([filter, skip, take, revision, workspaceRevision]);
    const [capture, setCapture] = useState<{
        key: string;
        page: ExchangePage;
    } | null>(null);
    const page = capture?.key === key ? capture.page : null;
    const [selected, setSelected] = useState<ExchangeItem | null>(null), [detailSkip, setDetailSkip] = useState(0);
    const [includeExcluded, setIncludeExcluded] = useState(false), [detailError, setDetailError] = useState<string | null>(null);
    const detailKey = JSON.stringify([selected?.kind, selected?.id, detailSkip, includeExcluded, revision, workspaceRevision]);
    const [detailCapture, setDetailCapture] = useState<{
        key: string;
        value: ExchangeDetail;
    } | null>(null);
    const detail = detailCapture?.key === detailKey ? detailCapture.value : null;
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        const controller = new AbortController();
        setLoading(true);
        setError(null);
        void getExchangeJournal(filter, skip, take, controller.signal)
            .then(value => {
            if (!controller.signal.aborted)
                setCapture({ key, page: value });
        })
            .catch(e => {
            if (!controller.signal.aborted)
                setError(errorText(e));
        })
            .finally(() => {
            if (!controller.signal.aborted)
                setLoading(false);
        });
        return () => controller.abort();
    }, [key, filter, skip, take]);
    useEffect(() => {
        if (!selected)
            return;
        const controller = new AbortController();
        setDetailError(null);
        void getExchangeDetail(selected, includeExcluded, detailSkip, 50, controller.signal)
            .then(value => {
            if (!controller.signal.aborted)
                setDetailCapture({ key: detailKey, value });
        })
            .catch(e => {
            if (!controller.signal.aborted)
                setDetailError(errorText(e));
        });
        return () => controller.abort();
    }, [selected, detailKey, includeExcluded, detailSkip]);
    function apply(event: FormEvent) {
        event.preventDefault();
        if (from && to && from > to) {
            setError('Начало периода не может быть позже конца.');
            return;
        }
        setSelected(null);
        setSkip(0);
        setFilter({ kind, status, ...(from ? { fromUtc: new Date(`${from}T00:00:00+05:00`).toISOString() } : {}),
            ...(to ? { toUtc: new Date(new Date(`${to}T00:00:00+05:00`).getTime() + 86400000).toISOString() } : {}) });
    }
    const refresh = () => setRevision(n => n + 1);
    return <div className="list-page exchange-journal-page" aria-label="Журнал обмена">
    <h1>Журнал обмена</h1>
    <form className="exchange-filters" onSubmit={apply}>
      <label>С<input type="date" value={from} onChange={e => setFrom(e.target.value)}/></label>
      <label>По<input type="date" value={to} onChange={e => setTo(e.target.value)}/></label>
      <label>Обмен<select value={kind} onChange={e => setKind(e.target.value as ExchangeKind)}>
        <option value="all">Все</option><option value="dds">Факт ДДС</option><option value="currency">Курсы валют</option>
        <option value="references">Изменения справочников</option><option value="payments">Платёжные документы и сверка оплат</option></select></label>
      <label>Результат<select value={status} onChange={e => setStatus(e.target.value as ExchangeStatus)}>
        <option value="all">Все</option><option value="success">Успешно</option><option value="failed">Ошибка</option><option value="running">Выполняется</option><option value="warning">Требует проверки</option></select></label>
      <button type="submit" className="secondary-button compact" disabled={loading}>Применить</button>
      <button type="button" className="secondary-button compact" disabled={loading} onClick={() => {
            setFrom('');
            setTo('');
            setKind('all');
            setStatus('all');
            setSkip(0);
            setSelected(null);
            setFilter({ kind: 'all', status: 'all' });
        }}>Сбросить</button>
      <button type="button" className="secondary-button compact" disabled={loading} onClick={refresh}><RefreshCw size={15} aria-hidden="true"/>Обновить</button>
    </form>
    {page?.fact && <div className="exchange-summary" aria-label="Актуальность факта">
      <span>Срез источника: <strong>{stamp(page.fact.capturedAtUtc)}</strong></span>
      <span>Проверено: {stamp(page.fact.checkedAtUtc)}</span>
      <span>Последняя дата операций: {day(page.fact.latestBusinessDate)}</span>
      <span>Запрос по дату: до {day(page.fact.cutoffExclusiveLocal)} (не включая)</span>
      <span>Записей источника: {page.fact.sourceLegs === null ? '—' : count.format(page.fact.sourceLegs)}</span>
      <small>Ежедневное задание — 07:00 UTC+5. Дата операций и время загрузки показаны отдельно.</small>
    </div>}
    {page?.warnings.map(warning => <p className="notice warning" role="alert" key={warning}>{warning}</p>)}
    {error && <p className="notice error" role="alert">{error}</p>}
    {loading && <p role="status">Загрузка журнала обмена…</p>}
    {page && <>
      <div className="exchange-toolbar">
        <span role="status">Записей: {count.format(page.total)}{!page.coverageComplete ? ' — по доступным источникам' : ''}</span>
        <label>Строк на странице<select value={take} disabled={loading} onChange={e => { setSkip(0); setTake(Number(e.target.value)); }}>
          <option value={50}>50</option><option value={100}>100</option></select></label>
        {(skip > 0 || page.total > take) && <>
          <button type="button" className="secondary-button compact" disabled={skip === 0 || loading} onClick={() => setSkip(Math.max(0, skip - take))}>Предыдущая страница</button>
          <button type="button" className="secondary-button compact" disabled={skip + take >= page.total || skip + take > 10000 || loading} onClick={() => setSkip(skip + take)}>Следующая страница</button></>}
        {skip + take > 10000 && page.total > skip + take && <span>Для более ранних записей сузьте период.</span>}
      </div>
      <div className="exchange-table-wrap"><table className="exchange-table" aria-label="Обмены BMS">
        <caption>Записи SQL BMS · время UTC+5 · изменения справочников не выдаются за автоматическую загрузку</caption>
        <thead><tr>{['Начало', 'Завершение', 'Обмен / изменение', 'Результат', 'Записей в снимке / изменено', 'Описание', 'Подробности'].map(t => <th scope="col" key={t}>{t}</th>)}</tr></thead>
        <tbody>{page.items.map(item => <tr key={`${item.kind}:${item.id}`}>
          <td>{stamp(item.startedAtUtc)}</td><td>{stamp(item.finishedAtUtc)}</td><td>{item.name}</td>
          <td><span className={`exchange-status exchange-status-${item.status}`}>{statusNames[item.status]}</span></td>
          <td className="numeric">{item.recordCount === null ? '—' : count.format(item.recordCount)}</td><td>{item.description}</td>
          <td>{item.hasDetails ? <button type="button" className="secondary-button compact" onClick={() => {
                        setSelected(item);
                        setDetailSkip(0);
                        setIncludeExcluded(false);
                    }}>{item.kind === 'dds' ? 'Реестр' : item.kind === 'currency' ? 'Курсы' : item.kind === 'payments' ? 'Подробности' : 'Изменения'}</button> : '—'}</td>
        </tr>)}{page.items.length === 0 && <tr><td colSpan={7}>Записи за выбранный период не найдены.</td></tr>}</tbody>
      </table></div>
    </>}
    {selected && <section className="exchange-detail" aria-label="Подробности обмена">
      <div className="exchange-toolbar"><h2>{detail?.title ?? 'Подробности обмена'}</h2>
        {selected.kind === 'dds' && <label><input type="checkbox" checked={includeExcluded} onChange={e => {
                    setIncludeExcluded(e.target.checked);
                    setDetailSkip(0);
                }}/>Показывать исключённые записи</label>}
        <button type="button" className="secondary-button compact" onClick={() => setSelected(null)}>Закрыть подробности</button></div>
      {detailError ? <p role="alert" className="notice error">{detailError} <button type="button" className="secondary-button compact" onClick={refresh}>Повторить</button></p>
                : !detail ? <p role="status">Загрузка реестра…</p> : <>
          <p>{detail.information}</p>
          <div className="exchange-toolbar"><span>Строк: {count.format(detail.total)}</span>
            {(detailSkip > 0 || detail.total > 50) && <>
              <button type="button" className="secondary-button compact" disabled={detailSkip === 0} onClick={() => setDetailSkip(Math.max(0, detailSkip - 50))}>Предыдущие строки</button>
              <button type="button" className="secondary-button compact" disabled={detailSkip + 50 >= detail.total} onClick={() => setDetailSkip(detailSkip + 50)}>Следующие строки</button></>}
            {detail.totals.map(total => <span key={total.label} title={`Без округления: ${total.amountKzt} ₸`}>{total.label}: <strong>{money(total.amountKzt)} ₸</strong></span>)}
          </div>
          <div className="exchange-table-wrap"><table className="exchange-table" aria-label={detail.title}>
            <thead><tr>{detail.columns.map(t => <th scope="col" key={t}>{t}</th>)}</tr></thead>
            <tbody>{detail.rows.map((row, index) => <tr key={`${detailSkip}:${index}`}>{row.map((cell, col) => <td key={col} title={cell}>
              {selected.kind === 'dds' && col === 0 ? localEntryDate(cell)
                                : selected.kind === 'dds' && ((detail.columns.length === 7 && col === 5) || (detail.columns.length === 8 && col === 7)) ? money(cell)
                                    : selected.kind === 'payments' && detail.columns.length === 7 && col === 5 ? money(cell)
                                        : selected.kind === 'payments' && detail.columns.length === 7 && col === 4
                                            ? cell.replace(/^(-?\d+\.\d+) (.+)$/, (_, amount: string, currency: string) => `${money(amount)} ${currency}`) : cell}
            </td>)}</tr>)}{detail.rows.length === 0 && <tr><td colSpan={detail.columns.length}>Нет доступных строк реестра.</td></tr>}</tbody>
          </table></div>
        </>}
    </section>}
  </div>;
}
