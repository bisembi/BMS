import { useEffect, useMemo, useState } from 'react';
import { getCashFlowOrder, type CashFlowOrder } from '../../api/cashFlowOrder';
import { filteredCashFlowStructure } from '../../pages/cashFlowStructure';
import type { BddsReportRow } from '../../api/bddsReport';
import '../../styles/cash-flow-structure.css';
export function CashFlowStructurePreview({ refreshRevision, catalogueRevision = 0, orderVersion, onClose, catalogue, catalogueLoading = false, catalogueError = false, onRetry, query = '', center = '', group = '' }: {
    refreshRevision: number;
    catalogueRevision?: number;
    orderVersion?: string;
    onClose: () => void;
    catalogue?: CashFlowOrder | null;
    catalogueLoading?: boolean;
    catalogueError?: boolean;
    onRetry?: () => void;
    query?: string;
    center?: string;
    group?: string;
}) {
    const [snapshot, setSnapshot] = useState<CashFlowOrder | null>(null);
    const [loading, setLoading] = useState(true), [error, setError] = useState(false), [retry, setRetry] = useState(0);
    const controlled = catalogue !== undefined;
    useEffect(() => {
        if (controlled)
            return;
        const controller = new AbortController();
        let active = true;
        setLoading(true);
        setError(false);
        setSnapshot(null);
        void getCashFlowOrder(controller.signal).then(value => {
            if (!active)
                return;
            setSnapshot(value);
        }).catch(() => {
            if (active)
                setError(true);
        }).finally(() => {
            if (active)
                setLoading(false);
        });
        return () => { active = false; controller.abort(); };
    }, [controlled, refreshRevision, catalogueRevision, orderVersion, retry]);
    const current = controlled ? catalogue : snapshot;
    const projection = useMemo(() => {
        try {
            return { rows: current ? filteredCashFlowStructure(current.items, query, center, group) : [], invalid: false };
        }
        catch {
            return { rows: [], invalid: true };
        }
    }, [current, query, center, group]);
    const rows = projection.rows;
    const isLoading = controlled ? catalogueLoading : loading;
    const hasError = (controlled ? catalogueError : error) || projection.invalid;
    const filtered = Boolean(query.trim() || center || group);
    const byId = new Map(current?.items.map(item => [item.id, item]));
    const byKey = new Map(rows.map(row => [row.key, row]));
    function depth(row: BddsReportRow) {
        let n = 0, parent = row.parentKey;
        while (parent !== null && byKey.has(parent)) {
            n++;
            parent = byKey.get(parent)!.parentKey;
        }
        return n;
    }
    return <section className="cash-flow-structure-preview" aria-labelledby="cash-flow-structure-title">
    <div className="directory-create-header"><h2 id="cash-flow-structure-title">Структура отчета БДДС</h2>
      <button type="button" onClick={onClose}>Закрыть структуру</button></div>
    <p>Сохранённая структура справочника, без сумм. Применены те же поиск, ЦФО и группа, что и в таблице ниже.
      Показаны все подходящие статьи со всех страниц.
      В отчёте состав статей зависит от выбранного ЦФО, периода и версии бюджета.</p>
    {isLoading && <p role="status">Загрузка структуры отчета…</p>}
    {hasError && <div role="alert">Не удалось загрузить структуру отчета.
      <button type="button" onClick={() => controlled ? onRetry?.() : setRetry(v => v + 1)}>Повторить загрузку структуры</button></div>}
    {!isLoading && !hasError && current && (rows.length === 0
            ? <p role="status">{filtered ? 'По выбранным фильтрам статей нет.' : 'В справочнике нет действующих статей.'}</p>
            : <><p role="status">Статей в структуре: {rows.filter(row => row.kind === 'Article').length}</p>
        <div className="cash-flow-structure-scroll"><table aria-label="Структура отчета БДДС">
          <thead><tr><th scope="col">Статья</th><th scope="col">Направление</th><th scope="col">ЦФО</th></tr></thead>
          <tbody>{rows.map(row => {
                    const item = row.cashFlowItemId ? byId.get(row.cashFlowItemId) : undefined;
                    return <tr key={row.key} className={'cash-flow-structure-row cash-flow-structure-' + row.kind.toLowerCase()} data-kind={row.kind} data-article-id={item?.id}>
              <td style={{ paddingInlineStart: 10 + depth(row) * 18 }}>{row.name}</td>
              <td>{item ? item.flowDirection === 'Receipt' ? 'Поступление' : item.flowDirection === 'Payment' ? 'Выплата' : 'Не задано' : ''}</td>
              <td>{item ? item.costCenterName ?? 'Не задано' : ''}</td>
            </tr>;
                })}</tbody></table></div></>)}
  </section>;
}
