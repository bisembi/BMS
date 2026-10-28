import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useRef, useState } from 'react';
import { getExpenseRequestHistory } from '../api';
import { getExpenseApprovalComments360 } from '../api/expenseApprovalComments360';
import { historyPageSize } from '../components/expense-request-history/expenseRequestHistoryFormat';
import type { ExpenseRequestHistoryEvent } from '../types';
import { expenseRequestHistoryErrorMessage, expenseRequestHistoryMoreErrorMessage } from '../utils/uiErrorMessages';
export type HistoryLoadState = 'idle' | 'loading' | 'success' | 'error';
export type ExpenseRequestHistoryReader = {
    items: ExpenseRequestHistoryEvent[];
    total: number;
    loadState: HistoryLoadState;
    loadError: string | null;
    isLoadingMore: boolean;
    loadMoreError: string | null;
    retry: () => void;
    loadMore: () => Promise<void>;
};
/// History events for exactly one saved request, newest first. Owns its own
/// load generation so a request switch or an invalidation discards in-flight
/// answers instead of mixing pages from two different requests.
export function useExpenseRequestHistory(requestId: string | null, invalidationToken: number, commentsOnly = false): ExpenseRequestHistoryReader {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [items, setItems] = useState<ExpenseRequestHistoryEvent[]>([]);
    const [total, setTotal] = useState(0);
    const [loadState, setLoadState] = useState<HistoryLoadState>('idle');
    const [loadError, setLoadError] = useState<string | null>(null);
    const [isLoadingMore, setIsLoadingMore] = useState(false);
    const [loadMoreError, setLoadMoreError] = useState<string | null>(null);
    const [retryToken, setRetryToken] = useState(0);
    const mounted = useRef(true);
    const loadGeneration = useRef(0);
    const loadedItems = useRef(items);
    loadedItems.current = items;
    const lastRead = useRef<{
        requestId: string | null;
        invalidationToken: number;
        retryToken: number;
        workspaceRefresh: number;
    } | null>(null);
    useWorkspaceRefreshBusy(loadState === 'loading' || isLoadingMore);
    useEffect(() => {
        mounted.current = true;
        return () => {
            mounted.current = false;
        };
    }, []);
    useEffect(() => {
        const generation = ++loadGeneration.current;
        const previous = lastRead.current;
        const preserveDepth = previous?.requestId === requestId && previous.invalidationToken === invalidationToken
            && previous.retryToken === retryToken && previous.workspaceRefresh !== workspaceRefresh;
        const targetCount = preserveDepth ? loadedItems.current.length : 0;
        lastRead.current = { requestId, invalidationToken, retryToken, workspaceRefresh };
        if (!requestId) {
            setItems([]);
            setTotal(0);
            setLoadState('idle');
            setLoadError(null);
            setLoadMoreError(null);
            setIsLoadingMore(false);
            return;
        }
        let cancelled = false;
        setItems([]);
        setTotal(0);
        setLoadState('loading');
        setLoadError(null);
        setLoadMoreError(null);
        setIsLoadingMore(false);
        const readDepth = async () => {
            const read = commentsOnly ? getExpenseApprovalComments360 : getExpenseRequestHistory;
            const first = await read(requestId, 0, historyPageSize);
            const freshItems = [...first.items];
            let freshTotal = first.total;
            while (preserveDepth && !cancelled && loadGeneration.current === generation && freshItems.length < Math.min(targetCount, freshTotal)) {
                const next = await read(requestId, freshItems.length, historyPageSize);
                freshTotal = next.total;
                if (next.items.length === 0)
                    break;
                freshItems.push(...next.items);
            }
            return { items: freshItems, total: freshTotal };
        };
        void readDepth()
            .then((page) => {
            if (cancelled || loadGeneration.current !== generation)
                return;
            setItems(page.items);
            setTotal(page.total);
            setLoadState('success');
        })
            .catch((error) => {
            if (cancelled || loadGeneration.current !== generation)
                return;
            setLoadError(expenseRequestHistoryErrorMessage(error));
            setLoadState('error');
        });
        return () => {
            cancelled = true;
        };
    }, [requestId, invalidationToken, retryToken, workspaceRefresh, commentsOnly]);
    const loadMore = async () => {
        if (!requestId || isLoadingMore || items.length >= total)
            return;
        const generation = loadGeneration.current;
        setIsLoadingMore(true);
        setLoadMoreError(null);
        try {
            const read = commentsOnly ? getExpenseApprovalComments360 : getExpenseRequestHistory;
            const page = await read(requestId, items.length, historyPageSize);
            if (!mounted.current || loadGeneration.current !== generation)
                return;
            setItems((current) => [...current, ...page.items]);
            setTotal(page.total);
        }
        catch (error) {
            if (mounted.current && loadGeneration.current === generation) {
                setLoadMoreError(expenseRequestHistoryMoreErrorMessage());
            }
        }
        finally {
            if (mounted.current && loadGeneration.current === generation)
                setIsLoadingMore(false);
        }
    };
    const retry = () => setRetryToken((value) => value + 1);
    const matchesRequest = lastRead.current?.requestId === requestId;
    return { items: matchesRequest ? items : [], total: matchesRequest ? total : 0,
        loadState, loadError, isLoadingMore, loadMoreError, retry, loadMore };
}
