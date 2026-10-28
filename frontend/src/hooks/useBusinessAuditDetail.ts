import { useWorkspaceRefreshAction, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import { getBusinessAuditMonitorDetail } from '../api';
import type { BusinessAuditMonitorDetail } from '../types';
import { businessAuditDetailErrorMessage } from '../utils/uiErrorMessages';
import { sameBusinessAuditIdentifier } from '../utils/businessAuditIdentity';
export type BusinessAuditDetailReader = {
    selectedAuditId: string | null;
    detail: BusinessAuditMonitorDetail | null;
    loading: boolean;
    error: string | null;
    /// Opens the detail of an event, or closes it when it is already open.
    toggle: (auditId: string) => Promise<void>;
    /// Drops any open detail and invalidates an in-flight detail request.
    invalidate: () => void;
};
/// Safe detail of one audit event with its own request sequence.
export function useBusinessAuditDetail(): BusinessAuditDetailReader {
    const [selectedAuditId, setSelectedAuditId] = useState<string | null>(null);
    const [detail, setDetail] = useState<BusinessAuditMonitorDetail | null>(null);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const detailSequence = useRef(0);
    const invalidate = useCallback(() => {
        detailSequence.current += 1;
        setSelectedAuditId(null);
        setDetail(null);
        setError(null);
        setLoading(false);
    }, []);
    const toggle = useCallback(async (auditId: string) => {
        if (sameBusinessAuditIdentifier(selectedAuditId, auditId)) {
            invalidate();
            return;
        }
        const requestNumber = detailSequence.current + 1;
        detailSequence.current = requestNumber;
        setSelectedAuditId(auditId);
        setDetail(null);
        setError(null);
        setLoading(true);
        try {
            const result = await getBusinessAuditMonitorDetail(auditId);
            if (requestNumber !== detailSequence.current)
                return;
            setDetail(result);
        }
        catch (loadError) {
            if (requestNumber !== detailSequence.current)
                return;
            setError(businessAuditDetailErrorMessage(loadError));
        }
        finally {
            if (requestNumber === detailSequence.current)
                setLoading(false);
        }
    }, [invalidate, selectedAuditId]);
    useWorkspaceRefreshBusy(loading);
    useWorkspaceRefreshAction(async () => {
        if (selectedAuditId === null)
            return;
        const sequence = ++detailSequence.current;
        setLoading(true);
        setError(null);
        try {
            const result = await getBusinessAuditMonitorDetail(selectedAuditId);
            if (sequence === detailSequence.current)
                setDetail(result);
        }
        catch (error) {
            if (sequence === detailSequence.current) {
                setDetail(null);
                setError(businessAuditDetailErrorMessage(error));
            }
            throw error;
        }
        finally {
            if (sequence === detailSequence.current)
                setLoading(false);
        }
    });
    useEffect(() => () => {
        detailSequence.current += 1;
    }, []);
    return { selectedAuditId, detail, loading, error, toggle, invalidate };
}
