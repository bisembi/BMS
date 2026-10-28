import { Fragment, useEffect, useState } from 'react';
import { getOrganizations } from '../api';
import { getOpiu, getOpiuDetails, type OpiuQuery, type OpiuReport, type OpiuDetails } from '../api/opiu';
import type { ReferenceDataItem } from '../types';
import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import '../styles/opiu.css';
const money = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 });
const labelMonth = (v: string) => new Date(`${v}T00:00:00Z`).toLocaleDateString('ru-RU', { month: 'short', year: '2-digit', timeZone: 'UTC' });
const currentYear = () => new Intl.DateTimeFormat('en', { year: 'numeric', timeZone: 'Asia/Almaty' }).format(new Date());
type Cell = {
    department: string;
    article: string;
    title: string;
    month?: string;
};
export function OpiuReportPage({ onOpenRequest }: {
    onOpenRequest(id: string): void;
}) {
    const year = currentYear();
    const [from, setFrom] = useState(`${year}-01`);
    const [through, setThrough] = useState(`${year}-12`);
    const [organization, setOrganization] = useState('');
    const [query, setQuery] = useState<OpiuQuery>({ from: `${year}-01-01`, through: `${year}-12-01` });
    const [catalog, setCatalog] = useState<ReferenceDataItem[]>([]);
    const [catalogFailed, setCatalogFailed] = useState(false);
    const [report, setReport] = useState<OpiuReport>();
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(true);
    const [refresh, setRefresh] = useState(0);
    const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
    const [cell, setCell] = useState<Cell>();
    const [skip, setSkip] = useState(0);
    const [detail, setDetail] = useState<OpiuDetails>();
    const [detailError, setDetailError] = useState('');
    const [detailLoading, setDetailLoading] = useState(false);
    const revision = useWorkspaceRefreshRevision();
    useWorkspaceRefreshBusy(loading || detailLoading);
    useEffect(() => {
        let live = true;
        setCatalogFailed(false);
        void getOrganizations().then(v => {
            if (live)
                setCatalog(v.items);
        }).catch(() => {
            if (live)
                setCatalogFailed(true);
        });
        return () => { live = false; };
    }, [refresh, revision]);
    useEffect(() => {
        const controller = new AbortController();
        setLoading(true);
        setError('');
        setReport(undefined);
        setCell(undefined);
        setDetail(undefined);
        void getOpiu(query, controller.signal).then(v => {
            if (!controller.signal.aborted)
                setReport(v);
        })
            .catch(e => {
            if (!controller.signal.aborted)
                setError(e instanceof Error ? e.message : 'ОПиУ недоступен.');
        })
            .finally(() => {
            if (!controller.signal.aborted)
                setLoading(false);
        });
        return () => controller.abort();
    }, [query, refresh, revision]);
    useEffect(() => {
        if (!cell)
            return;
        const controller = new AbortController();
        setDetailLoading(true);
        setDetail(undefined);
        setDetailError('');
        const request = { ...query, from: cell.month ?? query.from, through: cell.month ?? query.through,
            costCenterId: cell.department, expenseItemId: cell.article, skip, take: 50 };
        void getOpiuDetails(request, controller.signal).then(v => {
            if (!controller.signal.aborted)
                setDetail(v);
        })
            .catch(e => {
            if (!controller.signal.aborted)
                setDetailError(e instanceof Error ? e.message : 'Расшифровка недоступна.');
        })
            .finally(() => {
            if (!controller.signal.aborted)
                setDetailLoading(false);
        });
        return () => { controller.abort(); setDetailLoading(false); };
    }, [query, cell, skip, refresh, revision]);
    const apply = () => {
        if (!from || !through || from > through) {
            setError('Проверьте период начисления.');
            return;
        }
        setQuery({ from: `${from}-01`, through: `${through}-01`, ...(organization ? { organizationId: organization } : {}) });
    };
    const open = (value: Cell) => { setSkip(0); setCell(value); };
    return <div className="opiu-page">
    <h1>ОПиУ</h1>
    <div className="opiu-filters">
      <label>Период с<input type="month" value={from} min="2000-01" max="2099-12" onChange={e => setFrom(e.target.value)}/></label>
      <label>по<input type="month" value={through} min="2000-01" max="2099-12" onChange={e => setThrough(e.target.value)}/></label>
      <label>Организация<select value={organization} disabled={catalogFailed} onChange={e => setOrganization(e.target.value)}>
        <option value="">Все доступные</option>{catalog.map(o => <option key={o.id} value={o.id}>{o.name}</option>)}
      </select></label>
      <button type="button" onClick={apply} disabled={loading}>Сформировать</button>
      <button type="button" onClick={() => setRefresh(v => v + 1)} disabled={loading}>Обновить</button>
      <button type="button" onClick={() => setCollapsed(new Set(report?.departments.map(d => d.id)))}>Свернуть группы</button>
      <button type="button" onClick={() => setCollapsed(new Set())}>Развернуть группы</button>
    </div>
    <p className="opiu-note">Текущие незакрытые остатки начислений · по периоду начисления · ₸ без НДС</p>
    {catalogFailed && <p role="alert">Справочник организаций не загрузился. Отбор недоступен; отчёт сохраняет серверные ограничения доступа.</p>}
    {loading && <p role="status">Загрузка ОПиУ…</p>}
    {error && <p role="alert">{error}</p>}
    {report && <>
      <div className="opiu-scroll"><table className="opiu-table" aria-label="ОПиУ по незакрытым начислениям">
        <thead><tr><th scope="col">Статья / подразделение</th>{report.months.map(m => <th scope="col" key={m}>{labelMonth(m)}</th>)}<th scope="col">Итого</th></tr></thead>
        <tbody>{report.departments.map(d => <Fragment key={d.id}>
          <tr className="opiu-department"><th scope="row"><button type="button" aria-expanded={!collapsed.has(d.id)} onClick={() => setCollapsed(v => {
                    const next = new Set(v);
                    if (next.has(d.id))
                        next.delete(d.id);
                    else
                        next.add(d.id);
                    return next;
                })}>{collapsed.has(d.id) ? '▸' : '▾'} {d.name}</button></th>{d.months.map((v, i) => <td key={report.months[i]}>{money.format(v)}</td>)}<td>{money.format(d.total)}</td></tr>
          {!collapsed.has(d.id) && d.articles.map(a => <tr key={a.id}><th scope="row">{a.name}</th>{a.months.map((v, i) => <td key={report.months[i]}>
            <button type="button" className="opiu-value" aria-label={`${a.name}, ${labelMonth(report.months[i]!)}, расшифровка`} onClick={() => open({ department: d.id, article: a.id, title: `${d.name} · ${a.name}`, month: report.months[i] })}>{v === 0 ? '—' : money.format(v)}</button>
          </td>)}<td><button type="button" className="opiu-value" onClick={() => open({ department: d.id, article: a.id, title: `${d.name} · ${a.name}` })}>{money.format(a.total)}</button></td></tr>)}
        </Fragment>)}
        {report.lineCount === 0 && <tr><td colSpan={report.months.length + 2}>Незакрытых начислений в выбранной доступной области нет.</td></tr>}
        </tbody><tfoot><tr><th scope="row">Итого незакрытые начисления</th>{report.totals.map((v, i) => <td key={report.months[i]}>{money.format(v)}</td>)}<td>{money.format(report.total)}</td></tr></tfoot>
      </table></div>
      <p className="opiu-note">Строк начислений: {report.lineCount}. Итоги рассчитаны по всей выборке, включая свёрнутые группы. При частичном закрытии НДС распределён пропорционально сохранённой сумме строки.</p>
    </>}
    {cell && <section className="opiu-detail" aria-label="Расшифровка ОПиУ">
      <div className="opiu-detail-heading"><h2>{cell.title}{cell.month ? ` · ${labelMonth(cell.month)}` : ''}</h2><button type="button" onClick={() => setCell(undefined)}>Закрыть расшифровку</button></div>
      {detailLoading && <p role="status">Загрузка расшифровки…</p>}{detailError && <p role="alert">{detailError}</p>}
      {detail && <><p className="opiu-note">Текущий снимок на момент раскрытия. После закрытия первичных документов остатки могут измениться.</p>
        <div className="opiu-scroll"><table className="opiu-table opiu-detail-table" aria-label="Строки начислений ОПиУ"><thead><tr><th>Месяц</th><th>Организация</th><th>Контрагент</th><th>Назначение</th><th>Без НДС, ₸</th><th>Заявка</th></tr></thead>
          <tbody>{detail.items.map(r => <tr key={`${r.expenseRequestId}:${r.lineNumber}`}><td>{labelMonth(r.accrualPeriod)}</td><td>{r.organization}</td><td>{r.counterparty ?? '—'}</td><td>{r.purpose ?? '—'}</td><td>{money.format(r.remainingNetAmountKzt)}</td><td><button type="button" onClick={() => onOpenRequest(r.expenseRequestId)}>Открыть · {r.lineNumber}</button></td></tr>)}
            {detail.items.length === 0 && <tr><td colSpan={6}>Начислений нет.</td></tr>}</tbody><tfoot><tr><th colSpan={4}>Итого по всей расшифровке</th><td>{money.format(detail.totalNetAmountKzt)}</td><td /></tr></tfoot>
        </table></div><nav className="opiu-pager" aria-label="Страницы расшифровки ОПиУ"><span>{detail.total === 0 ? 0 : skip + 1}–{skip + detail.items.length} из {detail.total}</span>
          <button type="button" disabled={skip === 0 || detailLoading} onClick={() => setSkip(v => Math.max(0, v - 50))}>Предыдущая</button><button type="button" disabled={skip + 50 >= detail.total || detailLoading} onClick={() => setSkip(v => v + 50)}>Следующая</button></nav>
      </>}
    </section>}
  </div>;
}
