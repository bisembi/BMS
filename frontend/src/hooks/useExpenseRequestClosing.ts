import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useRef, useState } from 'react';
import { closeExpenseRequestFully, closeExpenseRequestPartially, getExpenseRequestClosingState, reopenExpenseRequest, type CloseExpenseRequestFullyInput, type CloseExpenseRequestPartiallyInput, type ExpenseRequestClosingCommandCode, type ExpenseRequestClosingExecutionResult, type ExpenseRequestClosingState, type ReopenExpenseRequestInput } from '../api';
import { expenseRequestClosingErrorMessage } from '../utils/uiErrorMessages';
type ClosingInput = CloseExpenseRequestPartiallyInput | CloseExpenseRequestFullyInput | ReopenExpenseRequestInput;
export type ExpenseRequestClosingReader = {
    state: ExpenseRequestClosingState | null;
    loading: boolean;
    loadError: string | null;
    executingCode: ExpenseRequestClosingCommandCode | null;
    commandError: string | null;
    successMessage: string | null;
    retry: () => void;
    execute: (code: ExpenseRequestClosingCommandCode, input: ClosingInput) => Promise<ExpenseRequestClosingExecutionResult | null>;
};
type Props = {
    requestId: string | null;
    requestVersion: string | null;
    onStateChanged?: (state: ExpenseRequestClosingState, recovered: boolean) => void;
};
const successMessages: Record<ExpenseRequestClosingCommandCode, string> = {
    CLOSE_PARTIAL: 'Частичное закрытие зафиксировано.',
    CLOSE_FULL: 'Заявка закрыта.',
    REOPEN: 'Заявка переоткрыта.',
};
/// Owns one load generation and one command at a time. A late response for a
/// previous request/version can neither replace the visible state nor update
/// the parent form.
export function useExpenseRequestClosing({ requestId, requestVersion, onStateChanged, }: Props): ExpenseRequestClosingReader {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [state, setState] = useState<ExpenseRequestClosingState | null>(null);
    const [loading, setLoading] = useState(false);
    const [loadError, setLoadError] = useState<string | null>(null);
    const [executingCode, setExecutingCode] = useState<ExpenseRequestClosingCommandCode | null>(null);
    const [commandError, setCommandError] = useState<string | null>(null);
    const [successMessage, setSuccessMessage] = useState<string | null>(null);
    const [retryToken, setRetryToken] = useState(0);
    const loadGeneration = useRef(0);
    const commandInFlight = useRef(false);
    const identity = useRef({ requestId, requestVersion });
    identity.current = { requestId, requestVersion };
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        const generation = ++loadGeneration.current;
        setCommandError(null);
        setSuccessMessage(null);
        if (requestId === null || requestVersion === null) {
            setState(null);
            setLoading(false);
            setLoadError(null);
            setExecutingCode(null);
            return undefined;
        }
        const controller = new AbortController();
        setState(null);
        setLoading(true);
        setLoadError(null);
        void getExpenseRequestClosingState(requestId, controller.signal)
            .then((loaded) => {
            if (generation !== loadGeneration.current)
                return;
            if (identity.current.requestId !== requestId
                || identity.current.requestVersion !== requestVersion)
                return;
            if (loaded.concurrencyStamp.toLocaleLowerCase('en-US')
                !== requestVersion.toLocaleLowerCase('en-US')) {
                setLoadError('Заявка была изменена. Обновите её перед выполнением закрытия.');
                return;
            }
            setState(loaded);
        })
            .catch((error: unknown) => {
            if (generation !== loadGeneration.current)
                return;
            if (error instanceof DOMException && error.name === 'AbortError')
                return;
            if (error instanceof Error && error.name === 'AbortError')
                return;
            setLoadError(expenseRequestClosingErrorMessage(error));
        })
            .finally(() => {
            if (generation === loadGeneration.current)
                setLoading(false);
        });
        return () => {
            controller.abort();
            loadGeneration.current += 1;
        };
    }, [requestId, requestVersion, retryToken, workspaceRefresh]);
    const execute = useCallback(async (code: ExpenseRequestClosingCommandCode, input: ClosingInput): Promise<ExpenseRequestClosingExecutionResult | null> => {
        const requestIdAtStart = identity.current.requestId;
        const versionAtStart = identity.current.requestVersion;
        if (requestIdAtStart === null || versionAtStart === null || commandInFlight.current) {
            return null;
        }
        commandInFlight.current = true;
        setExecutingCode(code);
        setCommandError(null);
        setSuccessMessage(null);
        try {
            let result: ExpenseRequestClosingExecutionResult;
            if (code === 'CLOSE_PARTIAL') {
                result = await closeExpenseRequestPartially(requestIdAtStart, input as CloseExpenseRequestPartiallyInput);
            }
            else if (code === 'CLOSE_FULL') {
                result = await closeExpenseRequestFully(requestIdAtStart, input as CloseExpenseRequestFullyInput);
            }
            else {
                result = await reopenExpenseRequest(requestIdAtStart, input as ReopenExpenseRequestInput);
            }
            if (identity.current.requestId !== requestIdAtStart
                || identity.current.requestVersion !== versionAtStart) {
                return null;
            }
            setState(result.state);
            setSuccessMessage(result.recovered
                ? 'Предыдущая попытка подтверждена сервером. Показано актуальное состояние.'
                : successMessages[code]);
            onStateChanged?.(result.state, result.recovered);
            return result;
        }
        catch (error: unknown) {
            if (identity.current.requestId === requestIdAtStart
                && identity.current.requestVersion === versionAtStart) {
                setCommandError(expenseRequestClosingErrorMessage(error));
            }
            return null;
        }
        finally {
            commandInFlight.current = false;
            setExecutingCode(null);
        }
    }, [onStateChanged]);
    return {
        state,
        loading,
        loadError,
        executingCode,
        commandError,
        successMessage,
        retry: () => setRetryToken((value) => value + 1),
        execute,
    };
}
