import { useEffect, useState } from 'react';
import { findBddsDrill, getBddsDrill, type BddsDrillPage as Page, type BddsDrillSortColumn } from '../api/bddsDrill';
import { useWorkspaceRefreshBusy, useWorkspaceRefreshRevision } from '../components/WorkspaceRefreshContext';
import './bddsDrill.css';
import { bddsDrillErrorMessage } from '../utils/uiErrorMessages';
const amount = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 });
const precise = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const day = (v: string) => `${v.slice(8, 10)}.${v.slice(5, 7)}.${v.slice(0, 4)}`;
const columns: {
    key: BddsDrillSortColumn;
    label: string;
}[] = [
    { key: 'entryDateLocal', label: 'Дата' }, { key: 'company', label: 'ЮЛ' },
    { key: 'documentType', label: 'Тип документа' }, { key: 'document', label: 'Документ' },
    { key: 'lineNo', label: 'Строка' },
    { key: 'counterparty', label: 'Контрагент' }, { key: 'notes', label: 'Примечания' },
    { key: 'comment', label: 'Комментарии' }, { key: 'amountKzt', label: 'Сумма' },
];
export function BddsDrillPage({ workspaceKey, onBdds }: {
    workspaceKey: string;
    onBdds: () => void;
}) {
    const context = findBddsDrill(workspaceKey);
    const refresh = useWorkspaceRefreshRevision();
    const [skip, setSkip] = useState(0), [take, setTake] = useState<50 | 100>(50), [revision, setRevision] = useState(0);
    const [sort, setSort] = useState<{
        sortBy: BddsDrillSortColumn;
        sortDirection: 'asc' | 'desc';
    } | null>(null);
    const [page, setPage] = useState<Page | null>(null), [loading, setLoading] = useState(false), [error, setError] = useState('');
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        if (!context)
            return;
        const controller = new AbortController();
        setLoading(true);
        setError('');
        setPage(null);
        void getBddsDrill({ ...context.query, skip, take, ...sort }, controller.signal).then(value => {
            if (!controller.signal.aborted)
                setPage(value);
        }).catch(reason => {
            if (!controller.signal.aborted)
                setError(bddsDrillErrorMessage(reason));
        })
            .finally(() => {
            if (!controller.signal.aborted)
                setLoading(false);
        });
        return () => controller.abort();
    }, [context, skip, take, sort, revision, refresh]);
    const orderBy = (key: BddsDrillSortColumn) => {
        setSkip(0);
        setSort(current => ({ sortBy: key, sortDirection: current?.sortBy === key && current.sortDirection === 'asc' ? 'desc' : 'asc' }));
    };
    return <div className="bdds-drill-page directory-page">
    <h1>Расшифровка</h1>
    {!context ? <p role="status">Контекст расшифровки не сохранён после перезагрузки. <button type="button" onClick={onBdds}>Открыть БДДС</button></p> : <>
      <p className="bdds-drill-scope">{context.title} · {context.query.fromMonth.slice(5, 7)}.{context.query.year} — {context.query.toMonth.slice(5, 7)}.{context.query.year} · факт 2026 · суммы в ₸</p>
      <div className="directory-toolbar"><button type="button" onClick={() => setRevision(n => n + 1)} disabled={loading}>Обновить</button>
        <button type="button" onClick={onBdds}>К БДДС</button></div>
      {loading && <p role="status">Загрузка платежей…</p>}
      {error && <p role="alert">{error}</p>}
      {page && <>
        <div className="directory-table-wrap table-scroll"><table className="directory-table bdds-drill-table" aria-label="Фактические платежи">
          <thead><tr>{columns.map(({ key, label }) => <th key={key} scope="col" aria-sort={sort?.sortBy === key ? sort.sortDirection === 'asc' ? 'ascending' : 'descending' : undefined}>
            <button type="button" className="bdds-drill-sort" title={key === 'lineNo' ? 'Номер проводки в платёжном документе 1С' : undefined} onClick={() => orderBy(key)}>{label}</button>
          </th>)}</tr></thead>
          <tbody>{page.items.map((row, index) => <tr key={`${skip}-${index}`}>
            <td>{day(row.entryDateLocal)}</td><td>{row.company}</td><td>{row.documentType}</td><td>{row.document}</td>
            <td className="bdds-drill-line">{row.lineNo}</td>
            <td>{row.counterparty || '—'}</td><td>{row.notes || '—'}</td><td>{row.comment || '—'}</td>
            <td className={`bdds-drill-money${row.amountKzt < 0 ? ' bdds-negative' : ''}`} title={precise.format(row.amountKzt)}>{amount.format(row.amountKzt)}</td>
          </tr>)}</tbody>
          <tfoot><tr><th colSpan={8} scope="row">Итого по всем строкам ({amount.format(page.total)})</th>
            <td className="bdds-drill-money" title={precise.format(page.totalKzt)}>{amount.format(page.totalKzt)}</td></tr></tfoot>
        </table></div>
        {page.total === 0 && <p>Платежей по выбранным статьям и периоду нет. Сумма сверена с БДДС.</p>}
        <div className="bdds-drill-pagination">
          <button type="button" disabled={loading || skip === 0} onClick={() => setSkip(Math.max(0, skip - take))}>Назад</button>
          <span>{page.total === 0 ? 0 : skip + 1}–{Math.min(skip + take, page.total)} из {amount.format(page.total)}</span>
          <button type="button" disabled={loading || skip + take >= page.total} onClick={() => setSkip(skip + take)}>Вперёд</button>
          <label>Строк на странице <select value={take} onChange={event => { setTake(Number(event.target.value) as 50 | 100); setSkip(0); }}><option value={50}>50</option><option value={100}>100</option></select></label>
        </div>
        <p className="muted">SQL BMS · сумма сверена с БДДС до тиына · детализация загружена {new Intl.DateTimeFormat('ru-RU', { timeZone: 'Asia/Qyzylorda', dateStyle: 'short', timeStyle: 'short' }).format(new Date(page.capturedAtUtc))} (UTC+5).</p>
      </>}
    </>}
  </div>;
}
