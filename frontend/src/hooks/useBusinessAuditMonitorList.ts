import { useWorkspaceRefreshAction, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import { getBusinessAuditMonitor, searchBusinessAuditMonitor } from '../api';
import { pageSize } from '../components/business-audit/businessAuditFormat';
import type { BusinessAuditMonitorItem, BusinessAuditSearchFilters } from '../types';
import { businessAuditListErrorMessage } from '../utils/uiErrorMessages';
export type BusinessAuditListReader = {
    items: BusinessAuditMonitorItem[];
    hasMore: boolean;
    loading: boolean;
    error: string | null;
    skip: number;
    appliedFilters: BusinessAuditSearchFilters | null;
    setSkip: (skip: number) => void;
    setAppliedFilters: (filters: BusinessAuditSearchFilters | null) => void;
    reload: () => Promise<void>;
};
/// Audit page reader with its own request sequence. Loading a page drops the
/// open detail, exactly as the page did before the decomposition, so the
/// invalidation callback must stay referentially stable.
export function useBusinessAuditMonitorList(invalidateDetail: () => void): BusinessAuditListReader {
    const [skip, setSkip] = useState(0);
    const [items, setItems] = useState<BusinessAuditMonitorItem[]>([]);
    const [hasMore, setHasMore] = useState(false);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [appliedFilters, setAppliedFilters] = useState<BusinessAuditSearchFilters | null>(null);
    const requestSequence = useRef(0);
    const read = useCallback(async (preserveDetail = false) => {
        const requestNumber = requestSequence.current + 1;
        requestSequence.current = requestNumber;
        setLoading(true);
        setError(null);
        if (!preserveDetail)
            invalidateDetail();
        try {
            const page = appliedFilters
                ? await searchBusinessAuditMonitor(appliedFilters, skip, pageSize)
                : await getBusinessAuditMonitor(skip, pageSize);
            if (requestNumber !== requestSequence.current)
                return;
            setItems(page.items);
            setHasMore(page.hasMore);
        }
        catch (loadError) {
            if (requestNumber !== requestSequence.current)
                return;
            setItems([]);
            setHasMore(false);
            setError(businessAuditListErrorMessage(loadError));
        }
        finally {
            if (requestNumber === requestSequence.current)
                setLoading(false);
        }
    }, [appliedFilters, invalidateDetail, skip]);
    const reload = useCallback(() => read(), [read]);
    useWorkspaceRefreshAction(() => read(true));
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        void reload();
        return () => {
            requestSequence.current += 1;
        };
    }, [reload]);
    return {
        items, hasMore, loading, error, skip, appliedFilters,
        setSkip, setAppliedFilters, reload,
    };
}
