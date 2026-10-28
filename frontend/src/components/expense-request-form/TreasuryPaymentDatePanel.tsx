import { useEffect, useState } from 'react';
import { getTreasuryPaymentDate, type TreasuryPaymentDateState } from '../../api/treasuryPaymentDate';
import type { SavedExpenseRequest } from '../../types';
import { treasuryPaymentDateErrorMessage } from '../../utils/uiErrorMessages';
import { useWorkspaceRefreshRevision } from '../WorkspaceRefreshContext';
type Props = {
    id: string;
    version: string;
    locked: boolean;
    onSaved: (saved: SavedExpenseRequest) => void;
    onBusyChange: (busy: boolean) => void;
};
// Bank facts are authoritative. Even a stale response with canEdit set must
// never reintroduce a date input or a client-side write.
export function TreasuryPaymentDatePanel({ id, version }: Props) {
    const refresh = useWorkspaceRefreshRevision();
    const [state, setState] = useState<TreasuryPaymentDateState | null>(null);
    const [error, setError] = useState<string | null>(null);
    useEffect(() => {
        let current = true;
        const controller = new AbortController();
        setState(null);
        setError(null);
        void getTreasuryPaymentDate(id, controller.signal).then(value => {
            if (!current)
                return;
            if (value && value.requestVersion.toLowerCase() !== version.toLowerCase()) {
                setError('Заявка изменена. Обновите сведения об оплате.');
                return;
            }
            setState(value);
        }).catch((e: unknown) => {
            if (current && !controller.signal.aborted)
                setError(treasuryPaymentDateErrorMessage(e));
        });
        return () => { current = false; controller.abort(); };
    }, [id, version, refresh]);
    if (!error && (!state?.confirmed || !state.date))
        return null;
    return <section className="panel treasury-payment-date" aria-label="Фактическая оплата из банка">
    {error ? <div className="notice error" role="status">{error}</div> : <>
      <div className="section-title">Дата последнего банковского платежа</div>
      <div>{state!.date!.split('-').reverse().join('.')}</div>
      <div className="section-hint">Заполнена автоматически из разнесённых банковских платежей при обмене с 1С.
        Дата полной оплаты и суммы — на вкладке «Оплата». Плановая дата не меняется.</div>
    </>}
  </section>;
}
