import { useEffect, useState } from 'react';
import { getUnbudgetedArticles, type UnbudgetedReport } from '../api/unbudgetedArticles';
import { listCompanyBudgetVersions, type CompanyBudgetVersionItem } from '../api/companyBudgetVersions';
import { useWorkspaceRefreshBusy, useWorkspaceRefreshRevision } from '../components/WorkspaceRefreshContext';
import { toBusinessDateInput } from '../utils/businessDate';
import { budgetPageNumbers } from '../utils/budgetPageNumbers';
import { CASH_FLOW_ACTIVITY_TYPE_LABELS } from '../types';
import { unbudgetedReportErrorMessage } from '../utils/uiErrorMessages';
import '../styles/unbudgeted-articles.css';
export function UnbudgetedArticlesPage() {
    const year = toBusinessDateInput().slice(0, 4);
    const [from, setFrom] = useState(year + '-01'), [to, setTo] = useState(year + '-12');
    const [center, setCenter] = useState(''), [group, setGroup] = useState(''), [version, setVersion] = useState('');
    const [applied, setApplied] = useState(() => ({ from: year + '-01', to: year + '-12', center: '', group: '', version: '' }));
    const [page, setPage] = useState(0), [size, setSize] = useState(25), [revision, setRevision] = useState(0);
    const [report, setReport] = useState<UnbudgetedReport | null>(null);
    const [loading, setLoading] = useState(true), [error, setError] = useState<string | null>(null);
    const [versions, setVersions] = useState<CompanyBudgetVersionItem[]>([]), [versionTotal, setVersionTotal] = useState(0);
    const [versionSkip, setVersionSkip] = useState(0), [versionLoading, setVersionLoading] = useState(true);
    const [versionError, setVersionError] = useState(false);
    const refresh = useWorkspaceRefreshRevision();
    const catalogKey = `${versionSkip}:${revision}:${refresh}`;
    const [loadedCatalogKey, setLoadedCatalogKey] = useState<string | null>(null);
    const catalogReady = !versionLoading && !versionError && loadedCatalogKey === catalogKey;
    useWorkspaceRefreshBusy(loading || versionLoading || (!versionError && loadedCatalogKey !== catalogKey));
    useEffect(() => {
        let alive = true;
        const controller = new AbortController();
        setVersionLoading(true);
        setVersionError(false);
        void (async () => {
            const fresh: CompanyBudgetVersionItem[] = [];
            let total = 0;
            for (let offset = 0; offset <= versionSkip; offset += 100) {
                const result = await listCompanyBudgetVersions(offset, 100, controller.signal);
                if (!alive)
                    return;
                if (!Number.isSafeInteger(result.total) || result.total < 0 || result.total > 10000
                    || (offset > 0 && result.total !== total))
                    throw new Error('Version catalog changed.');
                total = result.total;
                if (result.items.some(v => fresh.some(prior => prior.id === v.id)))
                    throw new Error('Duplicate version.');
                fresh.push(...result.items);
                if (offset + 100 >= total)
                    break;
            }
            return { items: fresh, total };
        })().then(result => {
            if (!alive || !result)
                return;
            setVersions(result.items);
            setVersionTotal(result.total);
            setLoadedCatalogKey(catalogKey);
            setVersionLoading(false);
        }).catch(() => {
            if (alive) {
                setVersionError(true);
                setVersionLoading(false);
            }
        });
        return () => { alive = false; controller.abort(); };
    }, [versionSkip, revision, refresh]);
    useEffect(() => {
        if (!catalogReady || versions.some(v => v.id === version))
            return;
        const next = (versions.find(v => v.isActive) ?? versions[0])?.id ?? '';
        setVersion(next);
        setApplied(current => current.version === '' || !versions.some(item => item.id === current.version)
            ? { ...current, version: next } : current);
        setPage(0);
    }, [versions, catalogReady, version]);
    useEffect(() => {
        let alive = true;
        const controller = new AbortController();
        // Never query the implicit current plan: this report belongs to a listed version.
        if (!catalogReady || !applied.version || !versions.some(v => v.id === applied.version)) {
            setLoading(false);
            setError(null);
            setReport(null);
            return () => { alive = false; controller.abort(); };
        }
        setLoading(true);
        setError(null);
        void getUnbudgetedArticles({ fromMonth: applied.from, toMonth: applied.to, costCenterId: applied.center || undefined,
            groupId: applied.group || undefined, versionId: applied.version, skip: page * size, take: size }, controller.signal)
            .then(result => {
            if (!alive)
                return;
            if (page > 0 && page * size >= result.total) {
                setPage(Math.max(0, Math.ceil(result.total / size) - 1));
                return;
            }
            setReport(result);
            setLoading(false);
        }).catch(reason => {
            if (alive) {
                setError(unbudgetedReportErrorMessage(reason));
                setLoading(false);
            }
        });
        return () => { alive = false; controller.abort(); };
    }, [applied, page, size, revision, refresh, versions, catalogReady]);
    const pages = Math.max(1, Math.ceil((report?.total ?? 0) / size));
    const versionReady = catalogReady && versions.some(v => v.id === version);
    return <section className="unbudgeted-articles-page">
    <h1>Незабюджетированные статьи по ЦФО</h1>
    <div className="unbudgeted-articles-filters">
      <label>Период с<input aria-label="Период с" type="month" min="2000-01" max="2100-12" value={from} onChange={e => { setFrom(e.target.value); }}/></label>
      <label>Период по<input aria-label="Период по" type="month" min="2000-01" max="2100-12" value={to} onChange={e => { setTo(e.target.value); }}/></label>
      <label className="unbudgeted-version-filter">Версия бюджета<select value={version} disabled={!versionReady} onChange={e => { setVersion(e.target.value); }}>
        {!versions.length && <option value="" disabled>{versionLoading ? 'Загрузка версий…' : 'Нет доступных версий'}</option>}
        {versions.map(v => <option key={v.id} value={v.id}>{v.name}{v.isActive ? ' (действует)' : ''}</option>)}
      </select></label>
      <label>ЦФО<select value={center} onChange={e => { setCenter(e.target.value); setGroup(''); }}>
        <option value="">Все доступные ЦФО</option>
        {report?.costCenters.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
      </select></label>
      <label>Группа<select value={group} onChange={e => { setGroup(e.target.value); }}>
        <option value="">Все группы</option><option value="none">Без группы</option>
        {report?.groups.map(g => <option key={g.id} value={g.id}>{g.name}</option>)}
      </select></label>
      <button type="button" disabled={!versionReady || from > to} onClick={() => {
            setApplied({ from, to, center, group, version });
            setPage(0);
        }}>Применить</button>
      <button type="button" disabled={loading || !versionReady} onClick={() => {
            const currentVersion = (versions.find(v => v.isActive) ?? versions[0])?.id ?? '';
            setFrom(year + '-01');
            setTo(year + '-12');
            setCenter('');
            setGroup('');
            setVersion(currentVersion);
            setPage(0);
            setApplied({ from: year + '-01', to: year + '-12', center: '', group: '', version: currentVersion });
        }}>Сбросить</button>
      <button type="button" disabled={loading || !versionReady} onClick={() => setRevision(v => v + 1)}>Обновить отчёт</button>
    </div>
    {(applied.from !== from || applied.to !== to || applied.center !== center || applied.group !== group || applied.version !== version)
            && <p className="muted">Отбор изменён. Нажмите «Применить».</p>}
    {versionError && <p role="alert">Не удалось загрузить список версий.
      <button type="button" onClick={() => setRevision(v => v + 1)}>Повторить загрузку версий</button></p>}
    {!versionError && versions.length < versionTotal && <button type="button" disabled={versionLoading} onClick={() => setVersionSkip(v => v + 100)}>Показать ещё версии</button>}
    {versionLoading ? <p role="status">Загрузка версий…</p>
            : !versionError && !versions.length ? <p>Нет доступных версий бюджета. Добавьте версию в список версий.</p>
                : loading ? <p role="status">Загрузка отчёта…</p> : error ? <p role="alert">{error}
      <button type="button" onClick={() => setRevision(v => v + 1)}>Повторить</button></p> : report && <>
      <div className="directory-table-wrap"><table className="directory-table" aria-label="Незабюджетированные статьи">
        <thead><tr>{['ЦФО', 'Вид деятельности', 'Группа', 'Статья ДДС', 'Бюджет за период, ₸']
                        .map(name => <th key={name} scope="col">{name}</th>)}</tr></thead>
        <tbody>{report.items.map(row => <tr key={row.id}>
          <td>{row.costCenterName}</td><td>{CASH_FLOW_ACTIVITY_TYPE_LABELS[row.activityType]}</td>
          <td>{row.groupName}</td><td>{row.name}</td><td className="unbudgeted-articles-money">0</td>
        </tr>)}</tbody>
      </table></div>
      {report.total === 0 && <p>Незабюджетированных статей по выбранным фильтрам нет.</p>}
      <div className="unbudgeted-articles-paging">
        <label>Строк на странице<select value={size} onChange={e => { setSize(Number(e.target.value)); setPage(0); }}>
          {[25, 50, 100].map(n => <option key={n} value={n}>{n}</option>)}</select></label>
        {(pages > 1 || page > 0) && <><button disabled={page === 0} onClick={() => setPage(v => v - 1)}>Назад</button>
        {budgetPageNumbers(page + 1, pages).map((n, i) => typeof n === 'number'
                            ? <button key={n} aria-current={n === page + 1 ? 'page' : undefined} onClick={() => setPage(n - 1)}>{n}</button> : <span key={'gap' + i}>…</span>)}
        <span>Страница {page + 1} из {pages}</span>
        <button disabled={page + 1 >= pages} onClick={() => setPage(v => v + 1)}>Вперёд</button></>}
      </div>
    </>}
  </section>;
}
