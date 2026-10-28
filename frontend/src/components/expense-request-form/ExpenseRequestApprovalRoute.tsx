import { Fragment, useEffect, useState } from 'react';
import { getExpenseApprovalRoute, type ExpenseApprovalRoute } from '../../api/expenseApprovalRoute';
import { expenseDraftBudgetInput } from '../../hooks/useExpenseDraftBudgetPreview';
import { preflightRoleLabels } from '../../utils/expenseRequestPreflightLabels';
import { useWorkspaceRefreshRevision } from '../WorkspaceRefreshContext';
import type { ExpenseRequestDraft, ExpenseRequestStatus } from '../../types';
import './ExpenseRequestApprovalRoute.css';
export function ExpenseRequestApprovalRoute({ draft, requestId, version, status, author, enabled, actor }: {
    draft: ExpenseRequestDraft;
    requestId: string | null;
    version: string | null;
    status: ExpenseRequestStatus | null;
    author: string;
    enabled: boolean;
    actor: string | null;
}) {
    const refresh = useWorkspaceRefreshRevision();
    const editable = status === 'DRAFT' || status === 'RETURNED';
    const input = editable && draft.paymentOperation?.kind !== 'PayrollAdvance' ? expenseDraftBudgetInput(draft, requestId, version)
        : requestId && version ? { id: requestId, version } : null;
    const serialized = input ? JSON.stringify(input) : null;
    const [retry, setRetry] = useState(0);
    const key = enabled && serialized ? JSON.stringify([serialized, status, actor, refresh, retry]) : null;
    const [capture, setCapture] = useState<{
        key: string;
        route: ExpenseApprovalRoute | null;
    } | null>(null);
    useEffect(() => {
        if (!key || !serialized)
            return;
        const controller = new AbortController();
        const timer = window.setTimeout(() => {
            void getExpenseApprovalRoute(JSON.parse(serialized), controller.signal)
                .then(route => {
                if (!controller.signal.aborted)
                    setCapture({ key, route });
            })
                .catch(() => {
                if (!controller.signal.aborted)
                    setCapture({ key, route: null });
            });
        }, 200);
        return () => { window.clearTimeout(timer); controller.abort(); };
    }, [key, serialized]);
    const current = key && capture?.key === key ? capture : null;
    const route = current?.route;
    return <section className="expense-approval-route" aria-label="Маршрут согласования">
    <div className="expense-route-heading"><div className="section-title">Маршрут согласования</div></div>
    {!key ? <span>{draft.paymentOperation?.kind === 'PayrollAdvance' ? 'Сохраните заявку, чтобы проверить участников маршрута.' : 'Укажите ЦФО, дату, статью и сумму расходов.'}</span>
            : !current ? <span role="status">Загрузка…</span>
                : !route ? <span role="status">Не удалось получить маршрут. <button type="button" className="secondary-button compact" onClick={() => setRetry(value => value + 1)}>Повторить</button></span>
                    : !route.available ? <span>{route.unavailableReason === 'AUTHOR_ROLE_REQUIRED'
                            ? 'Маршрут будет определён при подаче автором в выбранной роли.'
                            : 'Не удалось рассчитать маршрут. Проверьте активные назначения автора и согласующих.'}</span>
                        : <div className="expense-route-chain"><span>Инициатор: {author}</span>
        {route.stages.map(stage => <Fragment key={stage.stageNumber}><span aria-hidden="true">→</span>
          <span className={route.currentStageNumber === stage.stageNumber ? 'current' : undefined} aria-current={route.currentStageNumber === stage.stageNumber ? 'step' : undefined}>
            {preflightRoleLabels[stage.roleCode]}: {stage.names.join(' / ')}
          </span></Fragment>)}
        {route.nsiSpecialistNames && <><span aria-hidden="true">→</span>
          <span className={status === 'AWAITING_NSI' ? 'current' : undefined} aria-current={status === 'AWAITING_NSI' ? 'step' : undefined}>
            Специалист НСИ: {route.nsiSpecialistNames.length ? route.nsiSpecialistNames.join(' / ') : 'не назначен'}
          </span></>}
        <span aria-hidden="true">→</span><span className={status === 'APPROVED' ? 'current' : undefined} aria-current={status === 'APPROVED' ? 'step' : undefined}>
          Казначей: {route.treasurerNames?.length ? route.treasurerNames.join(' / ')
                                : route.treasurerNames ? 'не назначен' : 'имя недоступно'}
        </span>
      </div>}
    {route?.available && route.previewOnly && <div className="section-hint">Предварительный маршрут. Окончательно фиксируется при подаче заявки.</div>}
  </section>;
}
