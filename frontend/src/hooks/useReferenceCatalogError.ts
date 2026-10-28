import { useCallback, useState } from 'react';
import { referenceCatalogErrorMessage } from '../utils/uiErrorMessages';
export type ReferenceCatalogErrorReporter = (error: unknown, fallback: string) => void;
export type ReferenceCatalogError = {
    referenceError: string | null;
    reportError: ReferenceCatalogErrorReporter;
};
/// One shared notice for every catalogue reader of the form. The readers stay
/// independent, but the form keeps showing a single message and the latest
/// failure wins, exactly as before the split.
export function useReferenceCatalogError(): ReferenceCatalogError {
    const [referenceError, setReferenceError] = useState<string | null>(null);
    const reportError = useCallback<ReferenceCatalogErrorReporter>((error, fallback) => {
        setReferenceError(referenceCatalogErrorMessage(error, fallback));
    }, []);
    return { referenceError, reportError };
}
