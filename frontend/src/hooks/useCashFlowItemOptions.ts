import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import { getCashFlowItems } from '../api';
import type { ReferenceDataItem } from '../types';
export const cashFlowItemOptionSearchDelayMs = 300;
export type CashFlowItemOptions = {
    items: ReferenceDataItem[];
    selectedItems?: ReferenceDataItem[];
    /** Complete scoped catalogue, independent of the row selector's search. */
    allScopedItems?: ReferenceDataItem[];
    query: string;
    appliedQuery: string;
    loading: boolean;
    error: string | null;
    setQuery: (query: string) => void;
    reload: () => void;
};
/// By default the hook keeps the bounded server-side search window. Forms that
/// need one scrollable selector can opt into the complete active, non-group
/// catalogue with `loadAll`.
export function useCashFlowItemOptions(selectedIds: readonly string[], externalReloadToken = 0, loadAll = false, costCenterId?: string | null): CashFlowItemOptions {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [items, setItems] = useState<ReferenceDataItem[]>([]);
    const [query, setQuery] = useState('');
    const [appliedQuery, setAppliedQuery] = useState('');
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [reloadToken, setReloadToken] = useState(0);
    const requestSequence = useRef(0);
    const currentItems = useRef<ReferenceDataItem[]>([]);
    const allItems = useRef<ReferenceDataItem[] | null>(null);
    /**
     * The base of the window this hook actually holds - set when a request
     * succeeds, never when one is merely started.
     *
     * It used to be set before the request went out, and under StrictMode that
     * lost the first window entirely: the replayed mount effect saw its own base
     * already recorded, decided the window was in hand, and returned without
     * asking, while the cleanup had already invalidated the request still in
     * flight. Nothing then arrived and nothing was reported, so a selector sat on
     * an empty list. A failure clears it, so a window that could not be loaded is
     * never mistaken for one that was.
     */
    const loadedRequestBase = useRef<string | null>(null);
    const selectedKey = [...new Set(selectedIds.map((id) => id.toLocaleLowerCase('en-US')))]
        .sort()
        .join(',');
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        const trimmed = query.trim();
        if (trimmed === appliedQuery)
            return undefined;
        const timer = setTimeout(() => setAppliedQuery(trimmed), cashFlowItemOptionSearchDelayMs);
        return () => clearTimeout(timer);
    }, [query, appliedQuery]);
    useEffect(() => {
        const requestBase = `${loadAll ? '' : appliedQuery}\u0000${externalReloadToken}\u0000${workspaceRefresh}\u0000${reloadToken}\u0000${loadAll}\u0000${costCenterId ?? ''}`;
        const selected = selectedKey === '' ? [] : selectedKey.split(',');
        const filterItems = (catalogItems: readonly ReferenceDataItem[]) => {
            if (costCenterId !== undefined)
                catalogItems = catalogItems.filter(item => costCenterId
                    && item.costCenterId?.toLowerCase() === costCenterId.toLowerCase());
            const needle = appliedQuery.toLocaleLowerCase('ru-RU');
            const matches = needle === ''
                ? catalogItems
                : catalogItems.filter((item) => item.code.toLocaleLowerCase('ru-RU').includes(needle)
                    || item.name.toLocaleLowerCase('ru-RU').includes(needle));
            const visibleItems = [...matches];
            if (loadAll && selected.length > 0) {
                const visibleIds = new Set(visibleItems.map(item => item.id.toLocaleLowerCase('en-US')));
                for (const item of catalogItems) {
                    const id = item.id.toLocaleLowerCase('en-US');
                    if (selected.includes(id) && !visibleIds.has(id)) {
                        visibleItems.push(item);
                        visibleIds.add(id);
                    }
                }
            }
            return visibleItems;
        };
        if (loadAll && allItems.current !== null && loadedRequestBase.current === requestBase) {
            const visibleItems = filterItems(allItems.current);
            currentItems.current = visibleItems;
            setItems(visibleItems);
            setLoading(false);
            setError(null);
            return undefined;
        }
        const loadedIds = new Set(currentItems.current.map((item) => item.id.toLocaleLowerCase('en-US')));
        if (loadedRequestBase.current === requestBase
            && selected.every((id) => loadedIds.has(id))) {
            requestSequence.current += 1;
            setLoading(false);
            return undefined;
        }
        const requestNumber = requestSequence.current + 1;
        requestSequence.current = requestNumber;
        setLoading(true);
        setError(null);
        const request = getCashFlowItems(loadAll
            ? { loadAll: true }
            : { query: appliedQuery, selectedIds: selected });
        void request
            .then((catalog) => {
            if (requestNumber !== requestSequence.current)
                return;
            if (loadAll)
                allItems.current = catalog.items;
            const visibleItems = loadAll || costCenterId !== undefined ? filterItems(catalog.items) : catalog.items;
            loadedRequestBase.current = requestBase;
            currentItems.current = visibleItems;
            setItems(visibleItems);
            setLoading(false);
        })
            .catch(() => {
            if (requestNumber !== requestSequence.current)
                return;
            loadedRequestBase.current = null;
            allItems.current = null;
            currentItems.current = [];
            setItems([]);
            setError('Не удалось загрузить статьи ДДС.');
            setLoading(false);
        });
        return () => {
            requestSequence.current += 1;
        };
    }, [appliedQuery, externalReloadToken, loadAll, reloadToken, selectedKey, costCenterId, workspaceRefresh]);
    const reload = useCallback(() => setReloadToken((current) => current + 1), []);
    const scopedItems = costCenterId === undefined ? items : items.filter(item => costCenterId
        && item.costCenterId?.toLowerCase() === costCenterId.toLowerCase());
    const allScopedItems = allItems.current?.filter(item => costCenterId === undefined || (costCenterId
        && item.costCenterId?.toLowerCase() === costCenterId.toLowerCase()));
    return { items: scopedItems, allScopedItems, selectedItems: allItems.current?.filter(item => selectedIds.some(id => id.toLowerCase() === item.id.toLowerCase())) ?? [], query, appliedQuery, loading, error, setQuery, reload };
}
