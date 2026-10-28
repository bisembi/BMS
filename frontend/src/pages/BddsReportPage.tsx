import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { bddsReportErrorMessage, getBddsReport, type BddsCostCenter, type BddsReport, type BddsReportRow } from '../api/bddsReport';
import { listCompanyBudgetVersions, type CompanyBudgetVersionItem } from '../api/companyBudgetVersions';
import { projectBddsReport } from './bddsDisplayProjection';
import { bddsDrillArticleIndex } from './bddsDrillArticleIndex';
import { type BddsDrillContext } from '../api/bddsDrill';
import { businessDayInput } from '../utils/businessTime';
import './bddsDrill.css';
const months = ['Янв', 'Фев', 'Мар', 'Апр', 'Май', 'Июн', 'Июл', 'Авг', 'Сен', 'Окт', 'Ноя', 'Дек'];
const amount = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 });
const percent = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
const factValue = (value: number | null | undefined) => value == null ? '—' : amount.format(value);
const percentValue = (value: number | null | undefined) => value == null ? '—' : `${percent.format(value)}%`;
export function roundedBddsNetForDisplay(receipts: number, payments: number): number {
    return Math.round(receipts) - Math.round(payments);
}
function valueClass(value: number): string {
    return value < 0 ? 'bdds-negative' : value > 0 ? 'bdds-positive' : '';
}
function columnClass(metric: 'plan' | 'fact' | 'delta' | 'pct', comparison: boolean, total = false, value = 0): string {
    return [valueClass(value), metric === 'fact' ? 'bdds-fact' : '', total ? 'bdds-total' : '',
        metric === 'plan' && (comparison || total) ? 'bdds-block-start' : '',
        metric === 'pct' || (!comparison && total) ? 'bdds-block-end' : ''].filter(Boolean).join(' ');
}
function ancestorsCollapsed(row: BddsReportRow, byKey: Map<string, BddsReportRow>, collapsed: Set<string>): boolean {
    let parent = row.parentKey;
    while (parent !== null) {
        if (collapsed.has(parent))
            return true;
        parent = byKey.get(parent)?.parentKey ?? null;
    }
    return false;
}
function atGroupLevel(groupKeys: readonly string[], collapsed: ReadonlySet<string>, directSectionKeys: readonly string[], directCollapsed: ReadonlySet<string>): boolean {
    return groupKeys.length + directSectionKeys.length > 0 && collapsed.size === groupKeys.length
        && groupKeys.every(key => collapsed.has(key)) && directCollapsed.size === directSectionKeys.length
        && directSectionKeys.every(key => directCollapsed.has(key));
}
export function BddsReportPage({ onOpenDrill }: {
    onOpenDrill?: (context: BddsDrillContext) => void;
} = {}) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [selected, setSelected] = useState('');
    const [initialMonth] = useState(() => businessDayInput().slice(0, 7));
    const [fromMonth, setFromMonth] = useState(`${initialMonth.slice(0, 4)}-01`);
    const [toMonth, setToMonth] = useState(`${initialMonth.slice(0, 4)}-12`);
    const [includeActuals, setIncludeActuals] = useState(false);
    const [actualsYear, setActualsYear] = useState<number>(() => Number(initialMonth.slice(0, 4)));
    const [quickMonth, setQuickMonth] = useState(false);
    const previousActuals = useRef({ includeActuals: false, actualsYear: Number(initialMonth.slice(0, 4)) });
    const [selectedVersion, setSelectedVersion] = useState('');
    const [compareVersion, setCompareVersion] = useState('');
    const [versionChoices, setVersionChoices] = useState<CompanyBudgetVersionItem[]>([]);
    const [versionSkip, setVersionSkip] = useState(0);
    const [versionTotal, setVersionTotal] = useState(0);
    const [versionLoading, setVersionLoading] = useState(false);
    const [versionError, setVersionError] = useState(false);
    const [versionRevision, setVersionRevision] = useState(0);
    const [centers, setCenters] = useState<BddsCostCenter[]>([]);
    const [canIncludeActuals, setCanIncludeActuals] = useState(false);
    const [report, setReport] = useState<BddsReport | null>(null);
    const reportScope = useRef<string | null>(null);
    const topScroll = useRef<HTMLDivElement>(null);
    const scrollSpacer = useRef<HTMLDivElement>(null);
    const tableScroll = useRef<HTMLDivElement>(null);
    const tableRef = useRef<HTMLTableElement>(null);
    const measureScroll = useRef<(() => void) | null>(null);
    const synchronizedPositions = useRef(new WeakMap<HTMLDivElement, number>());
    const [hasHorizontalOverflow, setHasHorizontalOverflow] = useState(false);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [revision, setRevision] = useState(0);
    const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
    const [directCollapsed, setDirectCollapsed] = useState<Set<string>>(new Set());
    const year = Number(fromMonth.slice(0, 4));
    useLayoutEffect(() => {
        const top = topScroll.current, spacer = scrollSpacer.current, bottom = tableScroll.current, table = tableRef.current;
        if (!top || !spacer || !bottom || !table)
            return;
        let disposed = false, frame = 0;
        const measure = () => {
            if (disposed)
                return;
            // Cached hidden tabs must retain their horizontal position.
            if (bottom.clientWidth === 0) {
                setHasHorizontalOverflow(false);
                return;
            }
            const width = bottom.scrollWidth;
            if (spacer.style.width !== `${width}px`)
                spacer.style.width = `${width}px`;
            const maximum = Math.max(0, width - bottom.clientWidth);
            setHasHorizontalOverflow(maximum > 0);
            const left = Math.min(maximum, Math.max(0, bottom.scrollLeft));
            if (bottom.scrollLeft !== left) {
                bottom.scrollLeft = left;
                synchronizedPositions.current.set(bottom, bottom.scrollLeft);
            }
            if (top.scrollLeft !== bottom.scrollLeft) {
                top.scrollLeft = bottom.scrollLeft;
                synchronizedPositions.current.set(top, top.scrollLeft);
            }
        };
        const schedule = () => {
            // The resize adapter emits before applying column widths to the DOM.
            if (!disposed && !frame)
                frame = requestAnimationFrame(() => { frame = 0; measure(); });
        };
        const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure);
        observer?.observe(bottom);
        observer?.observe(table);
        table.addEventListener('bms:column-width', schedule);
        window.addEventListener('resize', measure);
        measureScroll.current = measure;
        measure();
        return () => {
            disposed = true;
            measureScroll.current = null;
            if (frame)
                cancelAnimationFrame(frame);
            observer?.disconnect();
            table.removeEventListener('bms:column-width', schedule);
            window.removeEventListener('resize', measure);
        };
    }, [report]); // Refreshes update the existing table; filter changes mount a new one.
    useLayoutEffect(() => { measureScroll.current?.(); });
    const synchronize = (from: HTMLDivElement, to: HTMLDivElement | null) => {
        const expected = synchronizedPositions.current.get(from);
        if (expected !== undefined) {
            synchronizedPositions.current.delete(from);
            // Programmatic scroll events arrive asynchronously. Do not let an echo
            // overwrite a newer native movement of the opposite scrollbar.
            if (Math.abs(expected - from.scrollLeft) < 0.5)
                return;
        }
        if (to && from.clientWidth > 0 && to.clientWidth > 0 && to.scrollLeft !== from.scrollLeft) {
            synchronizedPositions.current.set(to, from.scrollLeft);
            to.scrollLeft = from.scrollLeft;
        }
    };
    useWorkspaceRefreshBusy(loading || versionLoading);
    useEffect(() => { setCollapsed(new Set()); setDirectCollapsed(new Set()); }, [selected, fromMonth, toMonth, includeActuals, actualsYear, selectedVersion, compareVersion, year]);
    useEffect(() => {
        const controller = new AbortController();
        setVersionLoading(true);
        setVersionError(false);
        void listCompanyBudgetVersions(versionSkip, 25, controller.signal).then(page => {
            if (controller.signal.aborted)
                return;
            setVersionTotal(page.total);
            setVersionChoices(previous => {
                const items = new Map(previous.map(item => [item.id.toLowerCase(), item]));
                page.items.forEach(item => items.set(item.id.toLowerCase(), item));
                return [...items.values()];
            });
        }).catch(() => {
            if (!controller.signal.aborted)
                setVersionError(true);
        })
            .finally(() => {
            if (!controller.signal.aborted)
                setVersionLoading(false);
        });
        return () => controller.abort();
    }, [versionSkip, versionRevision, workspaceRefresh]);
    useEffect(() => {
        const controller = new AbortController();
        const scope = JSON.stringify([selected, fromMonth, toMonth, includeActuals, actualsYear, selectedVersion, compareVersion]);
        // Keep the same-query DOM (including both scrollbars and its page height)
        // during a refresh. Never retain a report from a different filter scope.
        if (reportScope.current !== scope)
            setReport(null);
        setError(null);
        if (!/^\d{4}-(0[1-9]|1[0-2])$/u.test(fromMonth) || !/^\d{4}-(0[1-9]|1[0-2])$/u.test(toMonth)
            || year < 2000 || year > 2100 || fromMonth.slice(0, 4) !== toMonth.slice(0, 4) || fromMonth > toMonth) {
            setLoading(false);
            setError('Выберите период в пределах одного года: начало не позднее конца.');
            return () => controller.abort();
        }
        setLoading(true);
        const options = fromMonth === `${year}-01` && toMonth === `${year}-12` && !includeActuals && !selectedVersion && !compareVersion
            ? undefined : { fromMonth, toMonth, includeActuals, ...(includeActuals && actualsYear !== year ? { actualsYear } : {}),
            ...(selectedVersion ? { versionId: selectedVersion } : {}), ...(compareVersion ? { compareVersionId: compareVersion } : {}) };
        const request = options === undefined
            ? getBddsReport(year, selected || undefined, controller.signal)
            : getBddsReport(year, selected || undefined, controller.signal, options);
        void request
            .then(value => {
            if (!controller.signal.aborted) {
                reportScope.current = scope;
                setReport(projectBddsReport(value));
                setCenters(value.costCenters);
                setCanIncludeActuals(value.canIncludeActuals === true);
            }
        })
            .catch(cause => {
            if (!controller.signal.aborted) {
                reportScope.current = null;
                setReport(null);
                setError(bddsReportErrorMessage(cause, year));
            }
        })
            .finally(() => {
            if (!controller.signal.aborted)
                setLoading(false);
        });
        return () => controller.abort();
    }, [revision, selected, fromMonth, toMonth, includeActuals, actualsYear, selectedVersion, compareVersion, year, workspaceRefresh]);
    const byKey = useMemo(() => new Map(report?.rows.map(row => [row.key, row]) ?? []), [report]);
    const groupKeys = useMemo(() => {
        const parents = new Set(report?.rows.filter(row => row.kind === 'Group').map(row => row.parentKey) ?? []);
        return report?.rows.filter(row => row.kind === 'Group' && !parents.has(row.key)).map(row => row.key) ?? [];
    }, [report]);
    const directSectionKeys = useMemo(() => {
        const directArticleParents = new Set(report?.rows.filter(row => row.kind === 'Article').map(row => row.parentKey) ?? []);
        return report?.rows.filter(row => (row.kind === 'Section' || (row.kind === 'Group' && !groupKeys.includes(row.key))) && directArticleParents.has(row.key)).map(row => row.key) ?? [];
    }, [report, groupKeys]);
    const grouped = atGroupLevel(groupKeys, collapsed, directSectionKeys, directCollapsed);
    const visible = report?.rows.filter(row => !ancestorsCollapsed(row, byKey, collapsed)
        && !(row.kind === 'Article' && row.parentKey !== null && directCollapsed.has(row.parentKey))) ?? [];
    const displayedNet = report === null
        ? 0 : roundedBddsNetForDisplay(report.receiptsAmountKzt, report.paymentsAmountKzt);
    const reportMonths = report?.months ?? months.map((_, i) => `${year}-${String(i + 1).padStart(2, '0')}-01`);
    const showActuals = report?.includesActuals === true;
    const drillArticleIds = useMemo(() => bddsDrillArticleIndex(report?.rows ?? []), [report]);
    const factCell = (row: BddsReportRow, index?: number) => {
        const value = index === undefined ? row.actualTotal : row.actualAmounts?.[index];
        if (value == null || report?.actualsYear !== report?.budgetYear || !report?.actualsSnapshotId || !onOpenDrill)
            return factValue(value);
        const ids = drillArticleIds.get(row.key) ?? [];
        if (!ids.length)
            return factValue(value);
        const from = index === undefined ? reportMonths[0] : reportMonths[index];
        const to = index === undefined ? reportMonths[reportMonths.length - 1] : from;
        return <button type="button" className="bdds-fact-link" aria-label={`Расшифровка: ${row.name}, ${from.slice(0, 7)}${from === to ? '' : ` — ${to.slice(0, 7)}`}`} onClick={() => onOpenDrill({ title: row.name, query: { year: report.budgetYear, fromMonth: from, toMonth: to,
                    snapshotId: report.actualsSnapshotId!, costCenterId: report.costCenterId, versionId: report.planVersion?.id ?? null,
                    articleIds: [...ids], skip: 0, take: 50 } })}>{factValue(value)}</button>;
    };
    const showVersionComparison = report?.compareVersion != null;
    const valueColumns = (reportMonths.length + 1) * (1 + (showVersionComparison ? 3 : 0) + (showActuals ? 3 : 0));
    const tableWidth = 350 + 70 + valueColumns * (1550 / 13);
    const pickerChoices: (NonNullable<BddsReport['planVersion']> & {
        isPartial?: boolean;
    })[] = [...versionChoices];
    for (const value of [selectedVersion ? report?.planVersion : null, compareVersion ? report?.compareVersion : null]) {
        if (value && !pickerChoices.some(item => item.id.toLowerCase() === value.id.toLowerCase())) {
            // A successful scoped report authoritatively names a selected version beyond the first catalogue page.
            pickerChoices.push(value);
        }
    }
    function toggle(key: string) {
        if (directCollapsed.has(key)) {
            setDirectCollapsed(current => { const next = new Set(current); next.delete(key); return next; });
            setCollapsed(current => { const next = new Set(current); next.delete(key); return next; });
            return;
        }
        setCollapsed(current => {
            const next = new Set(current);
            if (next.has(key))
                next.delete(key);
            else
                next.add(key);
            return next;
        });
    }
    function toggleCurrentPeriod() {
        const month = businessDayInput().slice(0, 7);
        if (quickMonth) {
            setFromMonth(`${month.slice(0, 4)}-01`);
            setToMonth(`${month.slice(0, 4)}-12`);
            setIncludeActuals(previousActuals.current.includeActuals && canIncludeActuals);
            setActualsYear(previousActuals.current.actualsYear);
        }
        else {
            previousActuals.current = { includeActuals, actualsYear };
            setFromMonth(month);
            setToMonth(month);
            // Use the current supported fact year; never grant fact access in the UI.
            setActualsYear(Number(businessDayInput().slice(0, 4)));
            setIncludeActuals(canIncludeActuals);
        }
        setQuickMonth(value => !value);
        setRevision(value => value + 1);
    }
    return <div className="bdds-page">
    <h1>БДДС</h1>
    <div className="bdds-toolbar">
      <div className="bdds-toolbar-primary">
      <label>ЦФО
        <select value={selected} disabled={loading || centers.length === 0} onChange={event => setSelected(event.target.value)}>
          <option value="">Все доступные</option>
          {centers.map(center => <option key={center.id} value={center.id}>{center.name}</option>)}
        </select>
      </label>
      <label>Базовая версия A
        <select aria-label="Базовая версия A" value={selectedVersion} disabled={loading || versionLoading} onChange={event => {
            setSelectedVersion(event.target.value);
            if (event.target.value === compareVersion)
                setCompareVersion('');
        }}>
          <option value="">Действующая версия</option>
          {pickerChoices.map(version => <option key={version.id} value={version.id}>
            {version.name}{version.isActive ? ' (действует)' : ''}{version.isPartial ? ' (доступные ЦФО)' : ''}
          </option>)}
        </select>
      </label>
      <label>Сравниваемая версия B
        <select aria-label="Сравниваемая версия B" value={compareVersion} disabled={loading || versionLoading} onChange={event => setCompareVersion(event.target.value)}>
          <option value="">Без сравнения версий</option>
          {pickerChoices.map(version => <option key={version.id} value={version.id} disabled={version.id.toLowerCase() === (selectedVersion || report?.planVersion?.id || '').toLowerCase()}>
            {version.name}{version.isActive ? ' (действует)' : ''}{version.isPartial ? ' (доступные ЦФО)' : ''}
          </option>)}
        </select>
      </label>
      <div className="bdds-toolbar-actions">
        <button type="button" disabled={loading || versionLoading} onClick={() => {
            setVersionSkip(0);
            setVersionRevision(value => value + 1);
            setRevision(value => value + 1);
        }}>Обновить отчёт</button>
        <button type="button" disabled={loading || report === null || groupKeys.length + directSectionKeys.length === 0} onClick={() => { setCollapsed(grouped ? new Set() : new Set(groupKeys)); setDirectCollapsed(grouped ? new Set() : new Set(directSectionKeys)); }}>
          {grouped ? 'Развернуть до статей' : 'Свернуть до групп'}
        </button>
        <button type="button" disabled={loading} onClick={toggleCurrentPeriod}>
          {quickMonth ? 'Текущий год' : 'Текущий месяц'}
        </button>
      </div>
      </div>
      {versionTotal > 25 && <div className="bdds-version-pages">
        <button type="button" disabled={versionLoading || versionSkip === 0} onClick={() => setVersionSkip(value => Math.max(0, value - 25))}>Предыдущие версии</button>
        <button type="button" disabled={versionLoading || versionSkip + 25 >= versionTotal} onClick={() => setVersionSkip(value => value + 25)}>Следующие версии</button>
        <span>Загружено {versionChoices.length} из {versionTotal} доступных версий</span>
      </div>}
      {versionError && <span role="status">Список версий не загружен. <button type="button" onClick={() => { setVersionChoices([]); setVersionRevision(value => value + 1); }}>Повторить список версий</button></span>}
      <div className="bdds-toolbar-secondary">
      <fieldset className="bdds-period-range">
        <legend>Период</legend>
        <label className="bdds-period">С
        <input aria-label="Период с" type="month" min="2000-01" max="2100-12" value={fromMonth} onChange={event => { setFromMonth(event.target.value); setQuickMonth(false); }}/>
        </label>
        <label className="bdds-period">По
        <input aria-label="Период по" type="month" min="2000-01" max="2100-12" value={toMonth} onChange={event => { setToMonth(event.target.value); setQuickMonth(false); }}/>
        </label>
      </fieldset>
      <label className="bdds-actuals-choice" title={selected
            ? 'Факт по статьям, назначенным выбранному ЦФО.'
            : 'Общий факт группы без ВГО. Отклонение = Факт − Бюджет; % = отклонение / |Бюджет|.'}>
        <input type="checkbox" checked={includeActuals} disabled={loading || !canIncludeActuals} onChange={event => setIncludeActuals(event.target.checked)}/>Добавить факт
      </label>
      {includeActuals && <div role="radiogroup" aria-label="Год факта" className="bdds-fact-year">
        {Array.from({ length: Math.max(2026, Number(initialMonth.slice(0, 4)), Number(fromMonth.slice(0, 4))) - 2025 + 1 }, (_, i) => 2025 + i).map(value => <label key={value}>
          <input type="radio" name="bdds-actuals-year" value={value} checked={actualsYear === value} disabled={loading} onChange={() => setActualsYear(value)}/>{value}
        </label>)}
      </div>}
      {report?.includesActuals === true && report.actualsSnapshotId === null
            && <span role="status">Факт за {report.actualsYear ?? actualsYear} год в BMS пока не загружен.</span>}
      </div>
    </div>
    {loading && report === null && <p role="status">Загрузка БДДС…</p>}
    {error && <p role="alert" className="directory-error">{error}{' '}
      <button type="button" onClick={() => setRevision(value => value + 1)}>Повторить</button></p>}
    {report && <>
      <div className="bdds-top-scroll" ref={topScroll} hidden={!hasHorizontalOverflow} role="region" tabIndex={0} aria-label="Горизонтальная прокрутка БДДС сверху" onScroll={event => synchronize(event.currentTarget, tableScroll.current)}>
        <div className="bdds-scroll-spacer" ref={scrollSpacer} aria-hidden="true"/>
      </div>
      <div className="bdds-table-wrap" ref={tableScroll} tabIndex={0} aria-label="Отчет БДДС" aria-busy={loading} onScroll={event => synchronize(event.currentTarget, topScroll.current)}>
        <table className="bdds-table" ref={tableRef} aria-label={`БДДС за ${report.budgetYear} год`} style={valueColumns === 13 ? undefined : { width: tableWidth, minWidth: tableWidth }}>
          <colgroup><col className="bdds-name-column"/><col className="bdds-cfo-column"/><col span={valueColumns}/></colgroup>
          <thead><tr><th scope="col">Статья</th><th scope="col" className="bdds-cfo-cell">ЦФО</th>{[...reportMonths, 'total'].flatMap(month => {
                const label = month === 'total' ? 'Итого' : `${months[Number(month.slice(5, 7)) - 1]} ${report.budgetYear}`;
                const total = month === 'total';
                return [<th key={`${month}-plan`} className={columnClass('plan', showActuals || showVersionComparison, total)} scope="col" title={report.planVersion?.name}>{label}{!total || showActuals || showVersionComparison ? <small className="bdds-plan-label">
                {showVersionComparison ? `A: ${report.planVersion?.name ?? 'ПЛАН'}` : 'ПЛАН'}</small> : null}</th>,
                    ...(showVersionComparison ? [<th key={`${month}-version`} className={columnClass('fact', true, total)} scope="col" title={report.compareVersion!.name}>{label}<small>B: {report.compareVersion!.name}</small></th>,
                        <th key={`${month}-version-delta`} className={columnClass('delta', true, total)} scope="col" title="Версия B − базовая версия A">{label}<small>B − A, ₸</small></th>,
                        <th key={`${month}-version-pct`} className={columnClass('pct', true, total)} scope="col" title="(B − A) / |A| × 100; при нулевой базе — прочерк">{label}<small>% B − A</small></th>] : []),
                    ...(showActuals ? [<th key={`${month}-fact`} className={columnClass('fact', true, total)} scope="col" title={total ? 'Сумма факта по доступным месяцам выбранного периода; незагруженные месяцы не включены' : undefined}>{total ? label : `${months[Number(month.slice(5, 7)) - 1]} ${report.actualsYear ?? actualsYear}`}<small>ФАКТ</small></th>,
                        <th key={`${month}-delta`} className={columnClass('delta', true, total)} scope="col" title={total ? 'Итог доступного факта − полный план выбранного периода' : 'Факт − План'}>{label}<small>ОТКЛ., ₸</small></th>,
                        <th key={`${month}-pct`} className={columnClass('pct', true, total)} scope="col" title={total ? '(Итог доступного факта − полный план выбранного периода) / |План| × 100; при нулевом плане — прочерк' : '(Факт − План) / |План| × 100; при нулевом плане — прочерк'}>{label}<small>%</small></th>] : [])];
            })}</tr></thead>
          <tbody>{visible.map(row => <tr key={row.key} className={`bdds-${row.kind.toLowerCase()}${row.kind === 'Group' && byKey.get(row.parentKey ?? '')?.kind === 'Group' ? ' bdds-channel' : ''}${row.kind === 'Article' && row.parentKey?.includes('|channel|') ? ' bdds-channel-article' : ''}`}>
            <th scope="row">
              {row.kind !== 'Article' && row.kind !== 'Net'
                    ? <button type="button" className="bdds-toggle" title={row.name} aria-expanded={!collapsed.has(row.key) && !directCollapsed.has(row.key)} onClick={() => toggle(row.key)}>
                    <span aria-hidden="true">{collapsed.has(row.key) || directCollapsed.has(row.key) ? '▸' : '▾'}</span>
                    <span className="bdds-label">{row.name}</span>
                  </button>
                    : <span className="bdds-label" title={row.name}>{row.name}</span>}
            </th>
            <td className="bdds-cfo-cell" title={row.kind === 'Article' ? row.costCenterCode ?? undefined : undefined}>
              {row.kind === 'Article' ? row.costCenterCode ?? '' : ''}
            </td>
            {row.amounts.flatMap((cell, index) => [<td key={`${index}-plan`} className={columnClass('plan', showActuals || showVersionComparison, false, cell)}>{cell === 0 ? '' : amount.format(cell)}</td>,
                    ...(showVersionComparison ? [<td key={`${index}-version`} className={columnClass('fact', true, false, row.compareAmounts?.[index] ?? 0)}>{factValue(row.compareAmounts?.[index])}</td>,
                        <td key={`${index}-version-delta`} className={columnClass('delta', true, false, row.versionVarianceAmounts?.[index] ?? 0)}>{factValue(row.versionVarianceAmounts?.[index])}</td>,
                        <td key={`${index}-version-pct`} className={columnClass('pct', true, false, row.versionVariancePercentages?.[index] ?? 0)}>{percentValue(row.versionVariancePercentages?.[index])}</td>] : []),
                    ...(showActuals ? [<td key={`${index}-fact`} className={columnClass('fact', true, false, row.actualAmounts?.[index] ?? 0)}>{factCell(row, index)}</td>,
                        <td key={`${index}-delta`} className={columnClass('delta', true, false, row.varianceAmounts?.[index] ?? 0)}>{factValue(row.varianceAmounts?.[index])}</td>,
                        <td key={`${index}-pct`} className={columnClass('pct', true, false, row.variancePercentages?.[index] ?? 0)}>{percentValue(row.variancePercentages?.[index])}</td>] : [])])}
            <td className={columnClass('plan', showActuals || showVersionComparison, true, row.kind === 'Net' ? displayedNet : row.total)}>
              {amount.format(row.kind === 'Net' ? displayedNet : row.total)}
            </td>
            {showVersionComparison && <><td className={columnClass('fact', true, true, row.compareTotal ?? 0)}>{factValue(row.compareTotal)}</td>
              <td className={columnClass('delta', true, true, row.versionVarianceTotal ?? 0)}>{factValue(row.versionVarianceTotal)}</td>
              <td className={columnClass('pct', true, true, row.versionVariancePercentageTotal ?? 0)}>{percentValue(row.versionVariancePercentageTotal)}</td></>}
            {showActuals && <><td className={columnClass('fact', true, true, row.actualTotal ?? 0)}>{factCell(row)}</td>
              <td className={columnClass('delta', true, true, row.varianceTotal ?? 0)}>{factValue(row.varianceTotal)}</td>
              <td className={columnClass('pct', true, true, row.variancePercentageTotal ?? 0)}>{percentValue(row.variancePercentageTotal)}</td></>}
          </tr>)}</tbody>
        </table>
      </div>
      {showVersionComparison && <div className="bdds-controls" aria-label="Контрольные итоги сравниваемой версии B">
        <span>B — {report.compareVersion!.name}: поступления {factValue(report.compareReceiptsAmountKzt)} ₸</span>
        <span>Выплаты {factValue(report.comparePaymentsAmountKzt)} ₸</span>
        <strong>Чистый денежный поток {factValue(report.compareNetCashFlowAmountKzt)} ₸</strong>
      </div>}
      {showActuals && <div className="bdds-controls" aria-label="Контрольные итоги факта">
        <span>Факт поступлений: {factValue(report.actualReceiptsAmountKzt)} ₸</span>
        <span>Факт выплат: {factValue(report.actualPaymentsAmountKzt)} ₸</span>
        <strong>Факт чистого потока: {factValue(report.actualNetCashFlowAmountKzt)} ₸</strong>
      </div>}
      <div className="bdds-controls" aria-label="Контрольные итоги БДДС">
        <span>Поступления: {amount.format(report.receiptsAmountKzt)} ₸</span>
        <span>Выплаты: {amount.format(report.paymentsAmountKzt)} ₸</span>
        <strong>Чистый денежный поток: {amount.format(displayedNet)} ₸</strong>
      </div>
    </>}
  </div>;
}
