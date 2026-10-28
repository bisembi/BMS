import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../WorkspaceRefreshContext';
import { useEffect, useState } from 'react';
import { expenseRequestStatusLabel } from '../../expenseRequestStatus';
import { getExpenseRequestPaymentSummary, type ExpenseRequestPaymentSummary } from '../../api/expenseRequestPaymentSummary';
const progressLabels: Record<ExpenseRequestPaymentSummary['progress'], string> = {
    unpaid: 'Не оплачено',
    partiallyPaid: expenseRequestStatusLabel('PARTIALLY_PAID'),
    paid: expenseRequestStatusLabel('PAID'),
};
const amountFormatter = new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
});
function formatAmount(amount: number, currencyCode: string): string {
    return `${amountFormatter.format(amount)} ${currencyCode}`;
}
function formatDate(date: string | null): string {
    if (!date)
        return '—';
    const [year, month, day] = date.split('-');
    return `${day}.${month}.${year}`;
}
type Props = {
    savedId: string | null;
};
type LoadState = {
    kind: 'idle';
} | {
    kind: 'loading';
} | {
    kind: 'unavailable';
} | {
    kind: 'ready';
    summary: ExpenseRequestPaymentSummary;
};
/// Read-only «Оплата»: the factual accounting state of the request. There is
/// deliberately no button here — a production payment fact can only arrive
/// from the accounting circuit, never from this form.
export function ExpenseRequestPaymentPanel({ savedId }: Props) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [state, setState] = useState<LoadState>({ kind: 'idle' });
    useWorkspaceRefreshBusy(state.kind === 'loading');
    useEffect(() => {
        if (!savedId) {
            setState({ kind: 'idle' });
            return undefined;
        }
        let cancelled = false;
        setState({ kind: 'loading' });
        getExpenseRequestPaymentSummary(savedId)
            .then((summary) => {
            if (!cancelled) {
                setState({ kind: 'ready', summary });
            }
        })
            .catch(() => {
            // The reason is dropped on purpose: no server failure text may
            // reach the DOM, and unavailability must never be shown as
            // «не оплачена».
            if (!cancelled) {
                setState({ kind: 'unavailable' });
            }
        });
        return () => {
            cancelled = true;
        };
    }, [savedId, workspaceRefresh]);
    if (!savedId) {
        return (<section className="payment-panel" aria-label="Оплата">
        <h3>Оплата</h3>
        <p className="payment-panel-note">
          Сведения об оплате появятся после сохранения заявки.
        </p>
      </section>);
    }
    return (<section className="payment-panel" aria-label="Оплата">
      <h3>Оплата</h3>
      {state.kind === 'loading' && (<p className="payment-panel-note" role="status">Загрузка сведений об оплате…</p>)}
      {state.kind === 'unavailable' && (<p className="payment-panel-note" role="status">
          Сведения об оплате сейчас недоступны. Это не означает, что заявка не
          оплачена.
        </p>)}
      {state.kind === 'ready' && (<>
          <p role="status" className="payment-panel-progress">
            {progressLabels[state.summary.progress]}
          </p>
          <dl className="payment-panel-amounts">
            <div>
              <dt>Сумма заявки</dt>
              <dd>{formatAmount(state.summary.obligationAmount, state.summary.currencyCode)}</dd>
            </div>
            {(state.summary.hasConfirmedPaymentFacts || state.summary.paidAmount !== 0) && <div>
              <dt>Погашено всего в валюте РЗ</dt>
              <dd>{formatAmount(state.summary.paidAmount, state.summary.currencyCode)}</dd>
            </div>}
            {state.summary.approvedRoundingAmount !== undefined && <>
              <div><dt>Оплачено по банковскому документу</dt>
                <dd>{formatAmount(state.summary.cashPaymentAmount!, state.summary.currencyCode)}</dd></div>
              <div><dt>Согласованное округление (не денежный платёж)</dt>
                <dd>{formatAmount(state.summary.approvedRoundingAmount, state.summary.currencyCode)}</dd></div>
            </>}
            <div>
              <dt>Осталось оплатить в валюте РЗ</dt>
              <dd>{formatAmount(state.summary.remainingAmount, state.summary.currencyCode)}</dd>
            </div>
            {state.summary.fullyPaidDate !== null && <div>
              <dt>Фактическая дата полной оплаты</dt>
              <dd>{formatDate(state.summary.fullyPaidDate)}</dd>
            </div>}
            {state.summary.currencyCode !== 'KZT' && (<>
                <div>
                  <dt>Первоначальный эквивалент обязательства в KZT</dt>
                  <dd>{state.summary.obligationAmountKzt === null
                    ? 'Недоступно'
                    : formatAmount(state.summary.obligationAmountKzt, 'KZT')}</dd>
                </div>
                {(state.summary.hasConfirmedPaymentFacts || state.summary.paidAmountKzt !== 0 && state.summary.paidAmountKzt !== null) && <div>
                  <dt>Погашено всего в KZT</dt>
                  <dd>{state.summary.paidAmountKzt === null
                        ? 'Недоступно'
                        : formatAmount(state.summary.paidAmountKzt, 'KZT')}</dd>
                </div>}
                {(state.summary.hasConfirmedPaymentFacts || state.summary.bankPaymentAmountKzt !== 0 && state.summary.bankPaymentAmountKzt !== null) && <div>
                  <dt>Банковский KZT-эквивалент</dt>
                  <dd>{state.summary.bankPaymentAmountKzt === null
                        ? 'Недоступно'
                        : formatAmount(state.summary.bankPaymentAmountKzt, 'KZT')}</dd>
                </div>}
                {(state.summary.hasConfirmedPaymentFacts || state.summary.currencyDeviationKzt !== 0 && state.summary.currencyDeviationKzt !== null) && <div>
                  <dt>Валютное отклонение в KZT</dt>
                  <dd>{state.summary.currencyDeviationKzt === null
                        ? 'Недоступно'
                        : formatAmount(state.summary.currencyDeviationKzt, 'KZT')}</dd>
                </div>}
              </>)}
          </dl>
          {state.summary.currencyCode !== 'KZT'
                && state.summary.obligationAmountKzt !== null
                && state.summary.paidAmountKzt === null && (<p className="payment-panel-note">
                KZT-разбивка оплаты недоступна: банковский KZT-эквивалент нельзя
                однозначно отделить от переплаты.
              </p>)}
          {!state.summary.hasConfirmedPaymentFacts && (<p className="payment-panel-note">
              Подтверждённых денежных фактов оплаты из бухгалтерского контура пока нет.
            </p>)}
        </>)}
    </section>);
}
