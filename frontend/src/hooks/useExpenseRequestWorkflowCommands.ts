import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import { executeExpenseRequestWorkflowCommand, getExpenseRequestWorkflowCommands } from '../api';
import { RecoveredPendingSaveError } from '../api/expenseRequestErrors';
import type { SavedExpenseRequest } from '../types';
import type { ExpenseRequestWorkflowCommandAvailability, ExpenseRequestWorkflowCommandCode } from '../api/expenseRequestWorkflowCommandPublicResponse';
import { expenseRequestWorkflowCommandErrorMessage } from '../utils/uiErrorMessages';
export type ExpenseRequestWorkflowCommandsReader = {
    commands: ExpenseRequestWorkflowCommandAvailability[];
    loading: boolean;
    executingCode: ExpenseRequestWorkflowCommandCode | null;
    error: string | null;
    execute: (code: ExpenseRequestWorkflowCommandCode, reason?: string, counterpartyId?: string | null, contractId?: string | null) => Promise<SavedExpenseRequest | null>;
};
type Props = {
    requestId: string | null;
    requestVersion: string | null;
    onSaved?: (request: SavedExpenseRequest) => void;
};
/// Reads commands with a sequence guard. The command POST is deliberately
/// separate from discovery: a stale discovery response can never authorize or
/// overwrite a newer document version.
export function useExpenseRequestWorkflowCommands({ requestId, requestVersion, onSaved, }: Props): ExpenseRequestWorkflowCommandsReader {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [commands, setCommands] = useState<ExpenseRequestWorkflowCommandAvailability[]>([]);
    const [loading, setLoading] = useState(false);
    const [executingCode, setExecutingCode] = useState<ExpenseRequestWorkflowCommandCode | null>(null);
    const [error, setError] = useState<string | null>(null);
    const sequence = useRef(0);
    const identity = useRef({ requestId, requestVersion });
    identity.current = { requestId, requestVersion };
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        const currentSequence = ++sequence.current;
        if (requestId === null || requestVersion === null) {
            setCommands([]);
            setError(null);
            setLoading(false);
            return;
        }
        const controller = new AbortController();
        setCommands([]);
        setError(null);
        setLoading(true);
        void getExpenseRequestWorkflowCommands(requestId, controller.signal)
            .then((result) => {
            if (currentSequence !== sequence.current)
                return;
            if (identity.current.requestId !== requestId
                || identity.current.requestVersion !== requestVersion)
                return;
            setCommands(result.commands);
        })
            .catch((loadError: unknown) => {
            if (currentSequence !== sequence.current)
                return;
            if (loadError instanceof DOMException && loadError.name === 'AbortError')
                return;
            if (loadError instanceof Error && loadError.name === 'AbortError')
                return;
            setError(expenseRequestWorkflowCommandErrorMessage(loadError));
        })
            .finally(() => {
            if (currentSequence === sequence.current)
                setLoading(false);
        });
        return () => {
            controller.abort();
            sequence.current += 1;
        };
    }, [requestId, requestVersion, workspaceRefresh]);
    const execute = useCallback(async (code: ExpenseRequestWorkflowCommandCode, reason?: string, counterpartyId?: string | null, contractId?: string | null): Promise<SavedExpenseRequest | null> => {
        const requestIdAtStart = identity.current.requestId;
        const requestVersionAtStart = identity.current.requestVersion;
        if (requestIdAtStart === null || requestVersionAtStart === null)
            return null;
        setExecutingCode(code);
        setError(null);
        try {
            const saved = await executeExpenseRequestWorkflowCommand(requestIdAtStart, code, requestVersionAtStart, reason, counterpartyId, contractId);
            if (identity.current.requestId !== requestIdAtStart
                || identity.current.requestVersion !== requestVersionAtStart) {
                return null;
            }
            onSaved?.(saved);
            return saved;
        }
        catch (commandError: unknown) {
            if (commandError instanceof RecoveredPendingSaveError) {
                if (identity.current.requestId === requestIdAtStart
                    && identity.current.requestVersion === requestVersionAtStart) {
                    onSaved?.(commandError.recoveredRequest);
                }
                setError('Предыдущая попытка команды подтверждена сервером. Данные обновлены.');
            }
            else {
                setError(expenseRequestWorkflowCommandErrorMessage(commandError));
            }
            return null;
        }
        finally {
            setExecutingCode(null);
        }
    }, [onSaved]);
    return { commands, loading, executingCode, error, execute };
}
