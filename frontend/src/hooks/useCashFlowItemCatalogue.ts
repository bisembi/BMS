import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import { loadCashFlowItemCatalogueWindow, type CashFlowItemPageSize } from './cashFlowItemCatalogueWindow';
import { loadErrorMessages } from '../components/reference-directory/referenceDirectoryText';
import type { ReferenceDataItem } from '../types';
import { filterCashFlowDirectory } from './cashFlowDirectoryFilters';
/// Default UI window. Larger choices are composed from bounded server pages.
export const cashFlowItemPageSize = 50;
/// How long the reader waits after the last keystroke before asking the
/// server. Without it every character is a request, and the answers arrive out
/// of order at exactly the moment the catalogue is large enough to be slow.
export const cashFlowItemSearchDelayMs = 300;
export type CashFlowItemCatalogue = {
    items: ReferenceDataItem[];
    total: number;
    skip: number;
    take: number;
    pageSize: CashFlowItemPageSize;
    query: string;
    appliedQuery: string;
    loading: boolean;
    error: string | null;
    hasPreviousPage: boolean;
    hasNextPage: boolean;
    setQuery: (query: string) => void;
    setPageSize: (pageSize: CashFlowItemPageSize) => void;
    goToPreviousPage: () => void;
    goToNextPage: () => void;
    reload: () => void;
};
/// The cash-flow-item catalogue read one page at a time.
///
/// Nothing here filters or sorts in the browser. Fifty rows remain the default;
/// loading 500 or every match is an explicit choice and retains server filtering.
///
/// Two orderings are defended against. A term that is still being typed does
/// not become a request until the typing stops, and an answer that arrives
/// after a newer request was made is dropped rather than displayed - the
/// sequence number is the same device the other directory reader uses.
export function useCashFlowItemCatalogue(source: readonly ReferenceDataItem[] | null = null, center = '', group = ''): CashFlowItemCatalogue {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [query, setQuery] = useState('');
    const [appliedQuery, setAppliedQuery] = useState('');
    const [pagination, setPagination] = useState<{
        skip: number;
        pageSize: CashFlowItemPageSize;
    }>({
        skip: 0, pageSize: cashFlowItemPageSize,
    });
    const { skip, pageSize } = pagination;
    const [page, setPage] = useState<{
        items: ReferenceDataItem[];
        total: number;
    }>({
        items: [],
        total: 0,
    });
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [reloadToken, setReloadToken] = useState(0);
    const requestSequence = useRef(0);
    // Typing is not a request. Only a pause in typing is, and a new term always
    // starts at the first page: keeping the offset would show page four of a
    // filter the reader has never seen.
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        const trimmed = query.trim();
        if (trimmed === appliedQuery)
            return undefined;
        const timer = setTimeout(() => {
            setAppliedQuery(trimmed);
            setPagination(current => ({ ...current, skip: 0 }));
        }, cashFlowItemSearchDelayMs);
        return () => clearTimeout(timer);
    }, [query, appliedQuery]);
    useEffect(() => {
        const requestNumber = requestSequence.current + 1;
        requestSequence.current = requestNumber;
        // Local order snapshots already contain the complete catalogue. Never
        // collapse the table to a loading placeholder for a synchronous re-slice:
        // doing so clamps the document/viewport scroll position to the page top.
        if (source !== null) {
            const matches = filterCashFlowDirectory(source, appliedQuery, center, group);
            if (skip > 0 && skip >= matches.length) {
                const lastSkip = matches.length === 0 || pageSize === 'all' ? 0 : Math.floor((matches.length - 1) / pageSize) * pageSize;
                setPagination(current => ({ ...current, skip: lastSkip }));
            }
            else {
                setPage({ items: pageSize === 'all' ? matches : matches.slice(skip, skip + pageSize), total: matches.length });
            }
            setError(null);
            setLoading(false);
            return () => { requestSequence.current += 1; };
        }
        setLoading(true);
        setError(null);
        const read = loadCashFlowItemCatalogueWindow(appliedQuery, skip, pageSize, () => requestNumber === requestSequence.current);
        void read
            .then((answer) => {
            if (requestNumber !== requestSequence.current)
                return;
            if (skip > 0 && skip >= answer.total) {
                const lastSkip = answer.total === 0 || pageSize === 'all' ? 0 : Math.floor((answer.total - 1) / pageSize) * pageSize;
                setPage({ items: [], total: answer.total });
                setPagination(current => ({ ...current, skip: lastSkip }));
                return;
            }
            setPage({ items: answer.items, total: answer.total });
            setLoading(false);
        })
            .catch(() => {
            if (requestNumber !== requestSequence.current)
                return;
            setPage({ items: [], total: 0 });
            setError(loadErrorMessages['cash-flow-items']);
            setLoading(false);
        });
        return () => {
            requestSequence.current += 1;
        };
    }, [appliedQuery, skip, pageSize, reloadToken, workspaceRefresh, source, center, group]);
    useEffect(() => { setPagination(current => ({ ...current, skip: 0 })); }, [center, group]);
    const setPageSize = useCallback((value: CashFlowItemPageSize) => {
        if (value !== 50 && value !== 100 && value !== 500 && value !== 'all')
            return;
        if (value === pageSize)
            return;
        requestSequence.current += 1;
        setPagination({ skip: 0, pageSize: value });
    }, [pageSize]);
    const goToPreviousPage = useCallback(() => {
        setPagination(current => current.pageSize === 'all' ? current : {
            ...current, skip: Math.max(0, current.skip - current.pageSize),
        });
    }, []);
    const goToNextPage = useCallback(() => {
        setPagination(current => current.pageSize === 'all' ? current : {
            ...current, skip: current.skip + current.pageSize,
        });
    }, []);
    const reload = useCallback(() => {
        setReloadToken((token) => token + 1);
    }, []);
    return {
        items: page.items,
        total: page.total,
        skip,
        take: pageSize === 'all' ? Math.max(1, page.total) : pageSize,
        pageSize,
        query,
        appliedQuery,
        loading,
        error,
        hasPreviousPage: pageSize !== 'all' && skip > 0,
        hasNextPage: pageSize !== 'all' && skip + pageSize < page.total,
        setQuery,
        setPageSize,
        goToPreviousPage,
        goToNextPage,
        reload,
    };
}
