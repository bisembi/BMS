import { useEffect, useMemo, useState } from 'react';
import { getOnlyCostDepartments473 } from '../../api/referenceData';
import type { ReferenceDataItem } from '../../types';
import { useWorkspaceRefreshBusy, useWorkspaceRefreshRevision } from '../WorkspaceRefreshContext';
import { CostCenterEditor } from './CostCenterEditor';
import { ReferenceDirectoryGrid } from './ReferenceDirectoryGrid';
import './departments475.css';
export function CostDepartments473({ canManage, onLockChange }: {
    canManage: boolean;
    onLockChange: (locked: boolean) => void;
}) {
    const refresh = useWorkspaceRefreshRevision();
    const [items, setItems] = useState<ReferenceDataItem[]>([]), [query, setQuery] = useState('');
    const [loading, setLoading] = useState(true), [error, setError] = useState(false);
    const [revision, setRevision] = useState(0), [creating, setCreating] = useState(false);
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        const abort = new AbortController();
        setLoading(true);
        setError(false);
        void getOnlyCostDepartments473(abort.signal).then(catalog => {
            if (!abort.signal.aborted) {
                setItems(catalog.items);
                setLoading(false);
            }
        }).catch(() => {
            if (!abort.signal.aborted) {
                setItems([]);
                setLoading(false);
                setError(true);
            }
        });
        return () => abort.abort();
    }, [refresh, revision]);
    const visible = useMemo(() => items.filter(item => `${item.code} ${item.name}`.toLocaleLowerCase('ru-RU')
        .includes(query.trim().toLocaleLowerCase('ru-RU'))), [items, query]);
    return <section className="cost-departments480" aria-label="Только подразделения затрат">
    {canManage && !creating && <div className="department-actions475">
      <button type="button" className="primary-button" onClick={() => setCreating(true)}>+ Добавить подразделение</button>
    </div>}
    {creating && <CostCenterEditor canManage={canManage} onlyCostDepartment onLockChange={onLockChange} onClose={() => { setCreating(false); onLockChange(false); }} onSaved={async () => { setRevision(v => v + 1); }}/>}
    <label className="department-search475"><span>Поиск подразделений</span>
      <input value={query} maxLength={200} onChange={e => setQuery(e.target.value)}/></label>
    {loading ? <p role="status">Загрузка подразделений затрат…</p>
            : error ? <p role="alert">Не удалось загрузить подразделения затрат. <button type="button" onClick={() => setRevision(v => v + 1)}>Повторить загрузку</button></p>
                : visible.length === 0 ? <p role="status">{query.trim() ? 'По условию поиска ничего не найдено.' : 'Подразделений затрат нет.'}</p>
                    : <ReferenceDirectoryGrid items={visible} caption="Только подразделения затрат" showsHierarchy={false} byId={new Map(items.map(item => [item.id, item]))}/>}
  </section>;
}
