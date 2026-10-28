import { useWorkspaceRefreshGuard } from '../WorkspaceRefreshContext';
import { useState } from 'react';
import type { ManualDeficitAttempt, ManualDeficitCorrection, ManualDeficit } from '../../api/manualDeficitTypes';
import { isManualDeficitText } from '../../api/manualDeficitPublicResponse';
import { isPublicExpenseRequestMoney } from '../../api/expenseRequestPublicResponse';
import { formatMoneyInput, normalizeMoneyInput } from '../../utils/expenseRequestDraft';
import { exactMoneyNumber } from '../../utils/moneyInput';
import { deficitMoney } from './manualDeficitFormat';
type Props = {
    deficit: ManualDeficit;
    pending: ManualDeficitCorrection | undefined;
    locked: boolean;
    onExecute: (attempt: ManualDeficitAttempt) => void;
};
export function ManualDeficitCorrectionForm({ deficit, pending, locked, onExecute }: Props) {
    const [amount, setAmount] = useState('');
    const [basis, setBasis] = useState('');
    const [comment, setComment] = useState('');
    const [reason, setReason] = useState('');
    const [confirmed, setConfirmed] = useState(false);
    useWorkspaceRefreshGuard(locked || amount !== '' || basis !== '' || comment !== '' || reason !== '' || confirmed
        ? 'Сначала завершите или очистите ввод корректировки дефицита.' : null);
    const amountNumber = exactMoneyNumber(amount, false) ?? NaN;
    const canCreate = isPublicExpenseRequestMoney(amountNumber, false) && amountNumber <= deficit.currentAmount
        && isManualDeficitText(basis.trim());
    const validComment = comment.trim() === '' || isManualDeficitText(comment.trim());
    if (deficit.canPropose)
        return <form onSubmit={(event) => {
                event.preventDefault();
                if (!locked && canCreate)
                    onExecute({ operation: 'create', deficitId: deficit.id, amount: amountNumber, basis });
            }}>
    <fieldset disabled={locked}>
      <legend>Предложение ответственного бухгалтера</legend>
      <label>Сумма корректировки, {deficit.currencyCode}<input inputMode="decimal" maxLength={24} autoComplete="off" value={formatMoneyInput(amount)} onChange={(event) => setAmount(normalizeMoneyInput(event.target.value))} required/></label>
      <label>Основание корректировки<textarea value={basis} maxLength={1000} required onChange={(event) => setBasis(event.target.value)}/></label>
      <p>Сумму в KZT рассчитает сервер по сохранённой паре остатка. До подтверждения дефицит не уменьшается.</p>
      <button className="primary" type="submit" disabled={locked || !canCreate}>Предложить корректировку</button>
    </fieldset>
  </form>;
    if (deficit.canDecide && pending)
        return <fieldset disabled={locked}>
    <legend>Решение финансового директора</legend>
    <p>Предложено: <strong>{deficitMoney(pending.amount, deficit.currencyCode)}</strong> / {deficitMoney(pending.amountKzt, 'KZT')}.</p>
    <p className="manual-deficit-basis">{pending.basis}</p>
    <label>Комментарий к решению<textarea maxLength={1000} value={comment} onChange={(event) => setComment(event.target.value)}/></label>
    <label className="manual-deficit-check"><input type="checkbox" checked={confirmed} onChange={(event) => setConfirmed(event.target.checked)}/>Сумма и основание проверены</label>
    <button className="primary" type="button" disabled={locked || !confirmed || !validComment} onClick={() => onExecute({ operation: 'approve', deficitId: deficit.id, correctionId: pending.id, comment })}>Подтвердить корректировку</button>
    <label>Причина отклонения<textarea maxLength={1000} value={reason} onChange={(event) => setReason(event.target.value)}/></label>
    <button type="button" disabled={locked || !isManualDeficitText(reason.trim()) || !validComment} onClick={() => onExecute({ operation: 'reject', deficitId: deficit.id, correctionId: pending.id, reason, comment })}>Отклонить корректировку</button>
  </fieldset>;
    return <p>{deficit.hasPendingCorrection ? 'Предложение ожидает решения финансового директора.'
            : deficit.currentAmount === 0 ? 'Дефицит погашен; новое предложение не требуется.'
                : 'Доступ только для просмотра. Предложение создаёт ответственный бухгалтер с доступом к организации.'}</p>;
}
