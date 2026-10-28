import { useEffect, useState, type FormEvent } from 'react';
import { budgetRegisterPageSizes350, getBudgetRegister343, type BudgetRegisterFilters343, type BudgetRegisterPage343, type BudgetRegisterPageSize350 } from '../api/budgetRegister343';
import { getCostCenters, getCashFlowItems } from '../api/referenceData';
import type { ReferenceDataItem } from '../types';
import { toBusinessDateInput } from '../utils/businessDate';
import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import '../styles/change-journal.css';
import '../styles/budget-register350.css';
const money = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 });
const dates = new Intl.DateTimeFormat('ru-RU', { timeZone: 'Etc/GMT-5', dateStyle: 'short', timeStyle: 'medium' });
export function BudgetExecutionRegisterPage343({ onOpenRequest }: {
    onOpenRequest: (id: string) => void;
}) {
    const refresh = useWorkspaceRefreshRevision();
    const [month, setMonth] = useState(() => toBusinessDateInput().slice(0, 7));
    const [cfo, setCfo] = useState('');
    const [article, setArticle] = useState('');
    const [filters, setFilters] = useState<BudgetRegisterFilters343>({ month });
    const [centers, setCenters] = useState<ReferenceDataItem[]>([]);
    const [articles, setArticles] = useState<ReferenceDataItem[]>([]);
    const [skip, setSkip] = useState(0);
    const [revision, setRevision] = useState(0);
    const [take, setTake] = useState<BudgetRegisterPageSize350>(100);
    const [page, setPage] = useState<BudgetRegisterPage343 | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    const [referenceError, setReferenceError] = useState(false);
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        let cancelled = false;
        void Promise.all([getCostCenters(), getCashFlowItems({ loadAll: true })]).then(([c, a]) => {
            if (!cancelled) {
                setCenters(c.items);
                setArticles(a.items);
                setReferenceError(false);
            }
        }).catch(() => {
            if (!cancelled)
                setReferenceError(true);
        });
        return () => { cancelled = true; };
    }, [revision, refresh]);
    useEffect(() => {
        const abort = new AbortController();
        setLoading(true);
        setError('');
        setPage(null);
        void getBudgetRegister343(filters, skip, abort.signal, take).then(p => {
            if (abort.signal.aborted)
                return;
            if (skip > 0 && skip >= p.total) {
                setSkip(p.total === 0 ? 0 : Math.floor((p.total - 1) / take) * take);
                return;
            }
            setPage(p);
        })
            .catch(() => {
            if (!abort.signal.aborted)
                setError('Не удалось загрузить регистр исполнения бюджета.');
        })
            .finally(() => {
            if (!abort.signal.aborted)
                setLoading(false);
        });
        return () => abort.abort();
    }, [filters, skip, take, revision, refresh]);
    const apply = (e: FormEvent) => {
        e.preventDefault();
        if (!month)
            return;
        setSkip(0);
        setFilters({ month, costCenterId: cfo || undefined, articleId: article || undefined });
    };
    return <div className="list-page change-journal-page budget-execution-register350" aria-label="Регистр исполнения бюджета">
    <h1>Исполнение бюджета</h1>
    <form className="change-journal-filters" onSubmit={apply}>
      <label>Месяц<input type="month" required value={month} onChange={e => setMonth(e.target.value)}/></label>
      <label>ЦФО<select value={cfo} onChange={e => setCfo(e.target.value)}><option value="">Все ЦФО</option>
        {centers.map(c => <option key={c.id} value={c.id}>{c.code} {c.name}</option>)}</select></label>
      <label>Статья ДДС<select value={article} onChange={e => setArticle(e.target.value)}><option value="">Все статьи</option>
        {articles.map(a => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}</select></label>
      <button className="secondary-button compact" disabled={loading}>Применить</button>
      <button type="button" className="secondary-button compact" disabled={loading} onClick={() => {
            const current = toBusinessDateInput().slice(0, 7);
            setMonth(current);
            setCfo('');
            setArticle('');
            setSkip(0);
            setFilters({ month: current });
        }}>Сбросить</button>
      <button type="button" className="secondary-button compact" disabled={loading} onClick={() => setRevision(n => n + 1)}>Обновить</button>
    </form>
    <p>Плюс — увеличение лимита или освобождение резерва; минус — занятие лимита или оплата. Оплата замещает резерв, а не списывается дважды. Суммы в ₸; время UTC+5.</p>
    {referenceError && <p role="alert">Не удалось загрузить справочники фильтров. Данные регистра доступны без этих фильтров.</p>}
    {loading && <p role="status">Загрузка регистра…</p>}{error && <p className="notice error" role="alert">{error}</p>}
    {page && <>
      <p role="status">Версия {page.revision}. Лимит: {money.format(page.totals.limitKzt)}; резерв: {money.format(page.totals.reserveKzt)};
        факт: {money.format(page.totals.paidKzt)}; остаток: {money.format(page.totals.balanceKzt)}.
        {page.cashCapturedAtUtc && ` Факт принят: ${dates.format(new Date(page.cashCapturedAtUtc))}.`}</p>
      <div className="change-journal-toolbar"><span>{page.total === 0 ? 'Движений нет' : `${skip + 1}–${skip + page.items.length} из ${page.total}`}</span>
        <label>Строк на странице<select value={take} disabled={loading} onChange={e => {
                const value = Number(e.target.value) as BudgetRegisterPageSize350;
                if (budgetRegisterPageSizes350.includes(value)) {
                    setSkip(0);
                    setTake(value);
                }
            }}>{budgetRegisterPageSizes350.map(size => <option key={size} value={size}>{size}</option>)}</select></label>
        <button className="secondary-button compact" disabled={loading || skip === 0} onClick={() => setSkip(Math.max(0, skip - take))}>Назад</button>
        <button className="secondary-button compact" disabled={loading || skip + take >= page.total} onClick={() => setSkip(skip + take)}>Вперёд</button></div>
      <div className="change-journal-table-wrap" tabIndex={0}><table className="change-journal-table" style={{ minWidth: 1930 }}><caption>Движения регистра исполнения бюджета</caption>
        <colgroup>{[140, 200, 320, 110, 280, 140, 140, 140, 140, 140, 180].map((width, i) => <col key={i} style={{ width }}/>)}</colgroup>
        <thead><tr>{['№ / дата', 'ЦФО', 'Статья ДДС', 'Заявка', 'Событие', 'Изменение лимита', 'Изменение резерва', 'Изменение факта', '+ / −', 'Остаток по статье', 'Источник'].map(t => <th key={t} scope="col">{t}</th>)}</tr></thead>
        <tbody>{page.items.map(row => <tr key={row.sequence}><td>{row.sequence}<br />{dates.format(new Date(row.postedAtUtc))}</td><td>{row.costCenter}</td><td>{row.article}</td>
          <td>{row.requestId && row.requestNumber?.trim() ? <button type="button" className="register-request-link350" onClick={() => onOpenRequest(row.requestId!)}>{row.requestNumber}</button> : row.requestNumber?.trim() || '—'}</td>
          <td>{row.event}</td>{[row.limitDeltaKzt, row.reservedDeltaKzt, row.paidDeltaKzt, row.amountKzt, row.balanceAfterKzt].map((n, i) => <td key={i} style={{ whiteSpace: 'nowrap', textAlign: 'right' }}>{money.format(n)}</td>)}
          <td><details><summary>Основание</summary><p>{row.sourceVersion}</p><pre style={{ whiteSpace: 'pre-wrap' }}>{row.evidence}</pre></details></td></tr>)}
          {page.items.length === 0 && <tr><td colSpan={11}>Движений за выбранный период нет.</td></tr>}</tbody>
      </table></div>
    </>}
  </div>;
}
