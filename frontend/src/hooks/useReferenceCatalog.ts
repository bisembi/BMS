import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useState } from 'react';
import type { ReferenceCatalogErrorReporter } from './useReferenceCatalogError';
type Catalog<TItem> = {
    items: TItem[];
};
export type ReferenceCatalogState<TItem> = {
    items: TItem[];
    loading: boolean;
};
/// A catalogue that has no scope: it is read once on mount and cancelled on
/// unmount. Each call owns its own state and its own cancellation flag, so one
/// failing catalogue never blocks or overwrites another.
export function useReferenceCatalog<TItem>(load: () => Promise<Catalog<TItem>>, fallbackError: string, reportError: ReferenceCatalogErrorReporter): ReferenceCatalogState<TItem> {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [state, setState] = useState<ReferenceCatalogState<TItem>>({
        items: [],
        loading: true,
    });
    useWorkspaceRefreshBusy(state.loading);
    useEffect(() => {
        let cancelled = false;
        setState((current) => ({ ...current, loading: true }));
        void load()
            .then((catalog) => {
            if (!cancelled)
                setState({ items: catalog.items, loading: false });
        })
            .catch((catalogError) => {
            if (!cancelled) {
                setState((current) => ({ ...current, loading: false }));
                reportError(catalogError, fallbackError);
            }
        });
        return () => {
            cancelled = true;
        };
    }, [fallbackError, load, reportError, workspaceRefresh]);
    return state;
}
