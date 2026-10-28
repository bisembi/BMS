import { useEffect, useState } from 'react';
import { getExpenseRequest } from '../api';
import type { SubmissionReadiness } from '../types';
import { sameOpaqueIdentifier } from '../utils/expenseRequestDraft';
// Refresh only the server summary after an attachment mutation. Never reload
// the editable draft: the user may have unsaved fields while adding a file.
export function useExpenseRequestAttachmentReadiness(requestId: string | null, version: string | null, source: SubmissionReadiness | null, revision: number) {
    const [capture, setCapture] = useState<{
        requestId: string;
        version: string;
        source: SubmissionReadiness | null;
        revision: number;
        readiness: SubmissionReadiness | null;
        failed: boolean;
    } | null>(null);
    useEffect(() => {
        if (!requestId || !version || revision === 0)
            return;
        let cancelled = false;
        void (async () => {
            try {
                const saved = await getExpenseRequest(requestId);
                if (cancelled)
                    return;
                if (!sameOpaqueIdentifier(saved.id, requestId)
                    || !sameOpaqueIdentifier(saved.concurrencyStamp, version) || !saved.submissionReadiness)
                    throw new Error('Unverified attachment readiness');
                setCapture({ requestId, version, source, revision, readiness: saved.submissionReadiness, failed: false });
            }
            catch {
                if (!cancelled)
                    setCapture({ requestId, version, source, revision, readiness: null, failed: true });
            }
        })();
        return () => { cancelled = true; };
    }, [requestId, version, source, revision]);
    if (!requestId || !version || revision === 0)
        return { readiness: source, failed: false, loading: false };
    if (capture && sameOpaqueIdentifier(capture.requestId, requestId)
        && sameOpaqueIdentifier(capture.version, version) && capture.source === source && capture.revision === revision)
        return { readiness: capture.readiness, failed: capture.failed, loading: false };
    return { readiness: null, failed: false, loading: true };
}
