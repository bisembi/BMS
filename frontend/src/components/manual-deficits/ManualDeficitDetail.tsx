import type { ManualDeficitAttempt, ManualDeficitDetail as Detail } from '../../api/manualDeficitTypes';
import { ManualDeficitCorrectionForm } from './ManualDeficitCorrectionForm';
import { ManualDeficitPagination } from './ManualDeficitPagination';
import { correctionStateLabels, deficitDate, deficitMoney } from './manualDeficitFormat';
type Props = {
    data: Detail;
    locked: boolean;
    onExecute: (attempt: ManualDeficitAttempt) => void;
    onPage: (skip: number) => void;
};
export function ManualDeficitDetail({ data, locked, onExecute, onPage }: Props) {
    const { deficit } = data;
    const pending = data.corrections.find((row) => row.state === 'PendingApproval');
    return <>
    <dl className="manual-deficit-summary">
      <div><dt>Организация</dt><dd>{deficit.organizationName}</dd></div>
      <div><dt>Контрагент</dt><dd>{deficit.counterpartyName}</dd></div>
      <div><dt>Возникновение, UTC+5</dt><dd>{deficitDate(deficit.occurredAtUtc)}</dd></div>
      <div><dt>Исходная сумма</dt><dd>{deficitMoney(deficit.originalAmount, deficit.currencyCode)} / {deficitMoney(deficit.originalAmountKzt, 'KZT')}</dd></div>
      <div><dt>Текущий остаток</dt><dd>{deficitMoney(deficit.currentAmount, deficit.currencyCode)} / {deficitMoney(deficit.currentAmountKzt, 'KZT')}</dd></div>
    </dl>
    {data.skip === 0 && <ManualDeficitCorrectionForm key={`${deficit.id}:${pending?.id ?? 'new'}`} deficit={deficit} pending={pending} locked={locked} onExecute={onExecute}/>}
    <h4>История корректировок</h4>
    {data.corrections.length === 0 ? <p>На этой странице предложений нет.</p> :
            <ol className="manual-deficit-history" start={data.skip + 1}>
        {data.corrections.map((row) => <li key={row.id}>
          <strong>{correctionStateLabels[row.state]}</strong>
          <p>{deficitMoney(row.amount, row.currencyCode)} / {deficitMoney(row.amountKzt, 'KZT')}</p>
          <p className="manual-deficit-basis">Основание: {row.basis}</p>
          {row.decidedAtUtc && <p>Решение: {deficitDate(row.decidedAtUtc)} UTC+5</p>}
          {row.decisionComment && <p className="manual-deficit-basis">Комментарий: {row.decisionComment}</p>}
          {row.rejectionReason && <p className="manual-deficit-basis">Причина отклонения: {row.rejectionReason}</p>}
        </li>)}
      </ol>}
    <ManualDeficitPagination label="Страницы корректировок" skip={data.skip} count={data.corrections.length} total={data.total} disabled={locked} onChange={onPage}/>
  </>;
}
