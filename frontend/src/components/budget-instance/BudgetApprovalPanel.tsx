import { useWorkspaceRefreshRevision, useWorkspaceRefreshGuard, useWorkspaceRefreshBusy } from '../WorkspaceRefreshContext';
import { useEffect, useId, useRef, useState } from 'react';
import { Check, CircleX, RotateCcw } from 'lucide-react';
import { createPortal } from 'react-dom';
import { getBudgetReview, writeBudgetReview, isUncertainWorkflowError, CfoWorkflowError, type BudgetReview, type BudgetReviewCommand } from '../../api/cfoWorkflow';
import type { BudgetInstance } from '../../api/budgetInstances';
import { useCommandLock } from '../WorkspaceCommandLockContext';
import { budgetApprovalRoleLabels as labels, budgetInstanceListStatusParts } from '../../pages/budgetInstanceApprovalStatus';
const states: Record<string, string> = { Draft: 'Черновик', Pending: 'На согласовании', Returned: 'Возвращён',
    Rejected: 'Отклонён', Approved: 'Утверждён' };
export function BudgetApprovalPanel({ budget, disabled, onChanged, onLocked, compact = false, actionTarget, statusTarget, routeTarget, showSubmit = true, refreshToken = 0, onSubmitRequested, submissionDisabled = false }: {
    budget: BudgetInstance;
    disabled: boolean;
    onChanged: () => void;
    onLocked?: (locked: boolean) => void;
    compact?: boolean;
    actionTarget?: HTMLElement | null;
    statusTarget?: HTMLElement | null;
    routeTarget?: HTMLElement | null;
    showSubmit?: boolean;
    refreshToken?: number;
    onSubmitRequested?: () => void;
    submissionDisabled?: boolean;
}) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [review, setReview] = useState<BudgetReview | null>(null);
    const [busy, setBusy] = useState(false);
    const [readLoading, setReadLoading] = useState(true);
    const [unknown, setUnknown] = useState(false);
    const [error, setError] = useState('');
    const [comment, setComment] = useState('');
    const [reasonRequired, setReasonRequired] = useState(false);
    const commentInput = useRef<HTMLInputElement>(null);
    const reasonId = useId();
    const [revision, setRevision] = useState(0);
    const [reviewedSource, setReviewedSource] = useState<{
        budget: BudgetInstance;
        token: number;
    } | null>(null);
    const attempt = useRef<{
        id: string;
        input: BudgetReviewCommand;
        key: string;
    } | null>(null);
    const command = useCommandLock('budget-review:' + budget.id);
    useWorkspaceRefreshGuard(busy || unknown || comment !== '' ? 'Сначала завершите согласование бюджета или очистите комментарий.' : null);
    useWorkspaceRefreshBusy(readLoading);
    useEffect(() => {
        const abort = new AbortController();
        setReview(null);
        setError('');
        setReadLoading(true);
        void getBudgetReview(budget.id, abort.signal).then(result => {
            if (!abort.signal.aborted) {
                setReviewedSource({ budget, token: refreshToken });
                setReview(result);
            }
        })
            .catch(() => {
            if (!abort.signal.aborted)
                setError('Не удалось загрузить согласование бюджета.');
        })
            .finally(() => {
            if (!abort.signal.aborted)
                setReadLoading(false);
        });
        return () => abort.abort();
    }, [budget, revision, workspaceRefresh, refreshToken]);
    useEffect(() => { onLocked?.(busy || unknown); return () => onLocked?.(false); }, [busy, unknown, onLocked]);
    async function execute(decision?: 'Approve' | 'Return' | 'Reject') {
        if (busy || !review || disabled)
            return;
        if (compact && (readLoading || reviewedSource?.budget !== budget || reviewedSource?.token !== refreshToken))
            return;
        if (!decision && !attempt.current && !review.canSubmit)
            return;
        if (decision === 'Approve' && !review.canDecide)
            return;
        if (decision === 'Return' && !(review.canReturn ?? review.canDecide))
            return;
        if (decision === 'Reject' && !(review.canReject ?? review.canDecide))
            return;
        if (decision && (!review.reviewId || !review.concurrencyStamp))
            return;
        if (decision && decision !== 'Approve' && !comment.trim()) {
            setReasonRequired(true);
            commentInput.current?.focus();
            return;
        }
        setReasonRequired(false);
        const input: BudgetReviewCommand = decision ? { reviewId: review.reviewId!, expectedConcurrencyStamp: review.concurrencyStamp!,
            decision, comment: comment.trim() || null } : { expectedConcurrencyStamp: budget.concurrencyStamp };
        const frozen = attempt.current ?? { id: budget.id, input, key: crypto.randomUUID() };
        const ticket = command.acquire();
        if (!ticket)
            return;
        attempt.current = frozen;
        setBusy(true);
        setError('');
        let retained = false, changed = false;
        try {
            const result = await writeBudgetReview(frozen.id, frozen.input, frozen.key);
            if (!command.isCurrent(ticket) || attempt.current !== frozen)
                return;
            setReview(result);
            setUnknown(false);
            attempt.current = null;
            setComment('');
            changed = true;
        }
        catch (cause) {
            if (!command.isCurrent(ticket) || attempt.current !== frozen)
                return;
            const uncertain = unknown || isUncertainWorkflowError(cause);
            retained = uncertain;
            setUnknown(uncertain);
            if (!uncertain) {
                attempt.current = null;
                setReview(null);
            }
            setError(uncertain ? 'Результат не подтверждён. Повторите ту же попытку.'
                : cause instanceof CfoWorkflowError && cause.code === 'budget-baseline-changed'
                    ? 'Состояние изменилось. Обновите документ и повторите действие.'
                    : 'Команда не выполнена. Обновите состояние; проверьте участников и полномочия.');
        }
        finally {
            if (command.isCurrent(ticket)) {
                setBusy(false);
                if (retained)
                    command.retain(ticket);
                else
                    command.release(ticket);
            }
        }
        if (changed)
            onChanged();
    }
    const submitButton = <button type="button" className={compact ? 'budget-primary-action' : undefined} disabled={(onSubmitRequested ? submissionDisabled : disabled) || busy || unknown || readLoading || reviewedSource?.budget !== budget || reviewedSource?.token !== refreshToken || !review?.canSubmit} title={onSubmitRequested ? 'Сохранить, проверить и отправить на согласование' : undefined} onClick={() => onSubmitRequested ? onSubmitRequested() : void execute()}>Подать на согласование</button>;
    const retryButton = <button type="button" disabled={busy || disabled || (compact && readLoading)} onClick={() => void execute()}>Повторить ту же попытку</button>;
    // The technical Draft flag also covers documents awaiting review. Use the
    // same presentation as the list and never reuse a previous document's stage.
    const currentReview = !readLoading && reviewedSource?.budget === budget
        && reviewedSource?.token === refreshToken ? review : null;
    const status = currentReview ? budgetInstanceListStatusParts({ status: budget.status,
        approvalState: currentReview.state === 'Draft' ? undefined : currentReview.state }, currentReview.currentRoleCode ? labels[currentReview.currentRoleCode] : undefined) : null;
    return <section className={compact ? 'budget-approval-compact' : 'cash-flow-settings'} aria-label="Согласование бюджета">
    {compact && statusTarget && createPortal(<span role="status" aria-label="Статус бюджета">
      {status ? status.label + status.supplement : error ? 'Статус не загружен' : 'Статус уточняется…'}
    </span>, statusTarget)}
    {compact && actionTarget && createPortal(unknown ? retryButton : showSubmit ? submitButton : null, actionTarget)}
    {!compact && <h2>Согласование бюджета</h2>}
    {review && <>
      {!compact && <p>Статус: {states[review.state]}{review.currentRoleCode ? ' — ' + labels[review.currentRoleCode] : ''}</p>}
      {review.stages.length > 0 && (compact && routeTarget
                ? createPortal(<span>Маршрут: {review.stages.map(s => labels[s.roleCode]).join(' → ')}</span>, routeTarget)
                : <p>Маршрут: {review.stages.map(s => labels[s.roleCode]).join(' → ')}</p>)}
      {review.state === 'Pending' && !review.canDecide && <p>
        Решение сейчас ожидается от: {labels[review.currentRoleCode ?? ''] ?? review.currentRoleCode}.
        {review.canReturn || review.canReject
                    ? ' Финансовый контролёр может вернуть бюджет на доработку или отклонить его, без права утверждения.'
                    : ' Кнопки доступны участникам текущего этапа согласования.'}
      </p>}
      {review.state === 'Pending' && review.canDecide && review.currentRoleCode === 'FIN_DIRECTOR'
                && review.stages.at(-1)?.roleCode === 'GENERAL_DIRECTOR' && <p>
          Ваше утверждение завершит этап финансового директора и передаст бюджет генеральному директору.
          Лимиты изменятся только после завершения всего маршрута.
        </p>}
      {!compact && review.canSubmit && submitButton}
      {review.state === 'Pending' && (review.canDecide || review.canReturn || review.canReject) && <section className="workflow-command-panel" aria-label="Действия согласования бюджета">
        <div className="workflow-command-heading"><div>
          <div className="section-title">Доступные действия</div>
          <div className="section-hint">Права и текущий этап проверяются сервером повторно.</div>
        </div></div>
        <div className="workflow-command-list">
          {review.canDecide && <div className="workflow-command"><button type="button" className="secondary-button" data-workflow-command="APPROVE" disabled={disabled || busy || unknown} onClick={() => void execute('Approve')}>
            <Check size={16} aria-hidden="true"/>{['BUDGET_OWNER', 'FIN_CONTROLLER'].includes(review.currentRoleCode ?? '') ? 'Согласовать' : 'Утвердить'}
          </button></div>}
          {(review.canReturn ?? review.canDecide) && <div className="workflow-command"><button type="button" className="secondary-button" data-workflow-command="RETURN" disabled={disabled || busy || unknown} onClick={() => void execute('Return')}><RotateCcw size={16} aria-hidden="true"/>Вернуть</button></div>}
          {(review.canReject ?? review.canDecide) && <div className="workflow-command"><button type="button" className="secondary-button" data-workflow-command="REJECT" disabled={disabled || busy || unknown} onClick={() => void execute('Reject')}><CircleX size={16} aria-hidden="true"/>Отклонить</button></div>}
          <label className="workflow-approval-comment360"><span>Комментарий к согласованию</span>
            <input type="text" ref={commentInput} value={comment} maxLength={2000} disabled={disabled || busy || unknown} placeholder="Например: уточнить расчёт по статье" title="Необязателен при утверждении; при возврате или отклонении укажите причину." aria-invalid={reasonRequired || undefined} aria-describedby={reasonRequired ? reasonId : undefined} onChange={e => { setComment(e.target.value); setReasonRequired(false); }}/>
          </label>
        </div>
        <p className="section-hint">Для доработки или отклонения укажите причину в комментарии. Для утверждения комментарий необязателен.</p>
        {reasonRequired && <p id={reasonId} role="alert">Укажите причину доработки или отклонения в комментарии.</p>}
      </section>}
    </>}
    {!compact && unknown && retryButton}
    {error && <p role="alert">{error}</p>}
    {compact && error && !review && !unknown && <button type="button" disabled={busy || disabled || readLoading} onClick={() => setRevision(v => v + 1)}>Перечитать согласование после ошибки</button>}
    {!compact && !unknown && <button type="button" disabled={busy || disabled} onClick={() => setRevision(v => v + 1)}>Обновить согласование</button>}
  </section>;
}
