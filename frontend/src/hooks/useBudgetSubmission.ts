import { useEffect, useRef, useState } from 'react';
import type { BudgetInstance } from '../api/budgetInstances';
import { CfoWorkflowError, getBudgetReview, isUncertainWorkflowError, writeBudgetReview } from '../api/cfoWorkflow';
import { useCommandLock } from '../components/WorkspaceCommandLockContext';
class SubmissionUnavailable extends Error {
    constructor(readonly code: 'locked' | 'not-ready') { super(); }
}
const refusalText = {
    locked: 'Другая команда блокирует подачу. Дождитесь её завершения.',
    'not-ready': 'Бюджет сохранён, но подача недоступна. Проверьте текущий статус, участников маршрута и права.',
};
/** Save and review the exact confirmed version. Never repeat the save after an unknown submit. */
export function useBudgetSubmission({ prepare, onSubmitted }: {
    prepare: () => Promise<BudgetInstance | undefined>;
    onSubmitted: (id: string) => Promise<void>;
}) {
    const [busy, setBusy] = useState(false);
    const [unknown, setUnknown] = useState(false);
    const [error, setError] = useState('');
    const inFlight = useRef(false);
    const mounted = useRef(true);
    const frozen = useRef<{
        id: string;
        input: {
            expectedConcurrencyStamp: string;
        };
        key: string;
    } | null>(null);
    const command = useCommandLock('budget-submit');
    useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
    async function execute() {
        if (inFlight.current)
            return;
        inFlight.current = true;
        setBusy(true);
        setError('');
        let ticket: ReturnType<typeof command.acquire> = null;
        let retained = false;
        let submittedId: string | undefined;
        try {
            if (!frozen.current) {
                const confirmed = await prepare();
                if (!mounted.current || !confirmed)
                    return;
                ticket = command.acquire();
                if (!ticket)
                    throw new SubmissionUnavailable('locked');
                const review = await getBudgetReview(confirmed.id);
                if (!command.isCurrent(ticket))
                    return;
                if (!review.canSubmit)
                    throw new SubmissionUnavailable('not-ready');
                frozen.current = { id: confirmed.id, input: { expectedConcurrencyStamp: confirmed.concurrencyStamp }, key: crypto.randomUUID() };
            }
            else {
                ticket = command.acquire();
                if (!ticket)
                    return;
            }
            const attempt = frozen.current!;
            await writeBudgetReview(attempt.id, attempt.input, attempt.key);
            if (!command.isCurrent(ticket!))
                return;
            frozen.current = null;
            setUnknown(false);
            submittedId = attempt.id;
        }
        catch (cause) {
            if (!mounted.current || (ticket && !command.isCurrent(ticket)))
                return;
            retained = frozen.current !== null && (unknown || isUncertainWorkflowError(cause));
            setUnknown(retained);
            if (!retained)
                frozen.current = null;
            setError(retained ? 'Результат подачи неизвестен. Повторите ту же попытку — повторное сохранение не выполняется.'
                : cause instanceof CfoWorkflowError ? cause.status === 409
                    ? 'Бюджет изменился. Обновите документ и проверьте изменения перед подачей.'
                    : 'Бюджет не подан. Проверьте заполнение, участников маршрута и права.'
                    : cause instanceof SubmissionUnavailable ? refusalText[cause.code] : 'Не удалось подать бюджет на согласование.');
        }
        finally {
            if (ticket && command.isCurrent(ticket)) {
                if (retained)
                    command.retain(ticket);
                else
                    command.release(ticket);
            }
            // Keep the form locked while loading the confirmed post-submit state.
            try {
                if (submittedId && mounted.current)
                    await onSubmitted(submittedId);
            }
            catch {
                if (mounted.current)
                    setError('Подача подтверждена, но документ не перечитан. Обновите его состояние.');
            }
            finally {
                inFlight.current = false;
                if (mounted.current)
                    setBusy(false);
            }
        }
    }
    return { busy, unknown, error, execute };
}
