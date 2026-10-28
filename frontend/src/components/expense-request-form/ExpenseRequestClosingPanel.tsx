import { useWorkspaceRefreshGuard, useWorkspaceRefreshBusy } from '../WorkspaceRefreshContext';
import { useMemo, useState, type KeyboardEvent } from 'react';
import type { ExpenseRequestClosingLine, ExpenseRequestClosingState } from '../../api';
import { expenseRequestStatusLabel } from '../../expenseRequestStatus';
import { useExpenseRequestClosing } from '../../hooks/useExpenseRequestClosing';
import { formatMoneyInput, normalizeMoneyInput } from '../../utils/expenseRequestDraft';
import { exactMoneyNumber } from '../../utils/moneyInput';
import { formatBusinessTimestamp } from '../../utils/businessTime';
type Props = {
    disabled?: boolean;
    requestId: string | null;
    requestVersion: string | null;
    onStateChanged?: (state: ExpenseRequestClosingState, recovered: boolean) => void;
};
const moneyFormatter = new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
});
function formatMoney(value: number, currencyCode: string): string {
    return `${moneyFormatter.format(value)} ${currencyCode}`;
}
function formatDate(value: string | null): string {
    if (value === null)
        return 'Не зафиксирована';
    const [year, month, day] = value.split('-');
    return `${day}.${month}.${year}`;
}
function formatTimestamp(value: string | null): string {
    if (value === null)
        return 'Не зафиксирована';
    return formatBusinessTimestamp(value);
}
function cents(value: number): number {
    return Math.round(value * 100);
}
function sumMoney(values: number[]): number {
    return values.reduce((total, value) => total + cents(value), 0) / 100;
}
function parseMoneyInput(value: string, allowZero: boolean): number | null {
    const normalized = normalizeMoneyInput(value);
    if (!/^\d{1,14}(?:\.\d{1,2})?$/u.test(normalized))
        return null;
    const parsed = exactMoneyNumber(normalized, allowZero);
    if (parsed === null)
        return null;
    if (!Number.isFinite(parsed) || parsed > 90071992547409.9)
        return null;
    if (parsed < (allowZero ? 0 : 0.01))
        return null;
    return parsed;
}
function lineRemaining(line: ExpenseRequestClosingLine): string {
    return line.remainingAmount === 0
        ? 'Закрыта'
        : moneyFormatter.format(line.remainingAmount);
}
export function ExpenseRequestClosingPanel({ requestId, requestVersion, onStateChanged, disabled = false, }: Props) {
    const closing = useExpenseRequestClosing({ requestId, requestVersion, onStateChanged });
    const [partialDate, setPartialDate] = useState('');
    const [controlAmount, setControlAmount] = useState('');
    const [partialReason, setPartialReason] = useState('');
    const [manualAllocation, setManualAllocation] = useState(false);
    const [lineAmounts, setLineAmounts] = useState<Record<number, string>>({});
    const [fullDate, setFullDate] = useState('');
    const [fullReason, setFullReason] = useState('');
    const [reopenReason, setReopenReason] = useState('');
    const [validationError, setValidationError] = useState<string | null>(null);
    const state = closing.state;
    const remainingTotal = useMemo(() => state ? sumMoney(state.lines.map((line) => line.remainingAmount)) : 0, [state]);
    const confirmedTotal = useMemo(() => state ? sumMoney(state.lines.map((line) => line.confirmedClosedAmount)) : 0, [state]);
    const excess = state
        ? Math.max(0, cents(state.confirmedClosingControlAmount) - cents(confirmedTotal)) / 100
        : 0;
    const busy = closing.executingCode !== null;
    useWorkspaceRefreshGuard(busy || partialDate !== '' || controlAmount !== '' || partialReason !== '' || manualAllocation
        || Object.values(lineAmounts).some(value => value !== '') || fullDate !== '' || fullReason !== '' || reopenReason !== ''
        ? 'Сначала завершите или очистите ввод на вкладке «Закрытие».' : null);
    useWorkspaceRefreshBusy(closing.loading);
    const canClose = !disabled && state?.canExecuteClosingCommands === true && state.status === 'PAID';
    const canReopen = !disabled && state?.canExecuteClosingCommands === true && state.status === 'CLOSED';
    const keepEnterInsidePanel = (event: KeyboardEvent<HTMLElement>) => {
        if (event.key === 'Enter' && !(event.target instanceof HTMLTextAreaElement)) {
            event.preventDefault();
        }
    };
    const runPartial = async () => {
        if (!state || !canClose)
            return;
        const control = parseMoneyInput(controlAmount, false);
        const reason = partialReason.trim();
        if (partialDate === '' || control === null || reason === '') {
            setValidationError('Укажите дату документов, положительную контрольную сумму и причину.');
            return;
        }
        let allocations: Array<{
            lineId: string;
            amount: number;
        }> | undefined;
        if (manualAllocation) {
            allocations = [];
            let allocatedCents = 0;
            for (const line of state.lines) {
                const source = lineAmounts[line.sortOrder] ?? '';
                const amount = source.trim() === '' ? 0 : parseMoneyInput(source, true);
                if (amount === null || cents(amount) > cents(line.remainingAmount)) {
                    setValidationError(`Проверьте распределение по строке ${line.sortOrder}.`);
                    return;
                }
                if (amount > 0)
                    allocations.push({ lineId: line.lineId, amount });
                allocatedCents += cents(amount);
            }
            const targetCents = Math.min(cents(control), cents(remainingTotal));
            if (allocatedCents !== targetCents) {
                setValidationError(`Ручное распределение должно составлять ${moneyFormatter.format(targetCents / 100)} ${state.currencyCode}.`);
                return;
            }
        }
        setValidationError(null);
        const result = await closing.execute('CLOSE_PARTIAL', {
            expectedVersion: state.concurrencyStamp,
            documentsReceivedDate: partialDate,
            controlAmount: control,
            reason,
            ...(allocations === undefined ? {} : { lineAllocations: allocations }),
        });
        if (result) {
            setControlAmount('');
            setPartialReason('');
            setLineAmounts({});
        }
    };
    const runFull = async () => {
        if (!state || !canClose)
            return;
        const reason = fullReason.trim();
        if (fullDate === '' || reason === '') {
            setValidationError('Укажите дату документов и причину полного закрытия.');
            return;
        }
        setValidationError(null);
        const result = await closing.execute('CLOSE_FULL', {
            expectedVersion: state.concurrencyStamp,
            documentsReceivedDate: fullDate,
            reason,
        });
        if (result)
            setFullReason('');
    };
    const runReopen = async () => {
        if (!state || !canReopen)
            return;
        const reason = reopenReason.trim();
        if (reason === '') {
            setValidationError('Укажите причину переоткрытия заявки.');
            return;
        }
        setValidationError(null);
        const result = await closing.execute('REOPEN', {
            expectedVersion: state.concurrencyStamp,
            reason,
        });
        if (result)
            setReopenReason('');
    };
    if (requestId === null || requestVersion === null) {
        return (<section id="expense-panel-closing" role="tabpanel" aria-labelledby="expense-tab-closing" className="form-card closing-panel">
        <h3>Закрытие начислений</h3>
        <p className="closing-note">Сведения о закрытии появятся после сохранения заявки.</p>
      </section>);
    }
    if (state && !closing.loading && !closing.loadError && state.status !== 'PAID' && state.status !== 'CLOSED') {
        return <section id="expense-panel-closing" role="tabpanel" aria-labelledby="expense-tab-closing" className="form-card closing-panel">
      <h3>Закрытие начислений</h3><p>Закрытие доступно после подтверждения полной оплаты заявки.</p>
    </section>;
    }
    return (<section id="expense-panel-closing" role="tabpanel" aria-labelledby="expense-tab-closing" className="form-card closing-panel" aria-busy={closing.loading || busy} onKeyDown={keepEnterInsidePanel}>
      <div className="closing-heading">
        <div>
          <h3>Закрытие начислений</h3>
          <p className="closing-note">
            Фиксирует получение документов и управленческое закрытие. Оплату этот раздел не создаёт.
          </p>
        </div>
        {state && <span className="closing-status">{expenseRequestStatusLabel(state.status)}</span>}
      </div>

      {closing.loading && <p className="closing-note" role="status">Загрузка состояния закрытия…</p>}
      {closing.loadError && (<div className="closing-message error" role="alert">
          <span>{closing.loadError}</span>
          <button type="button" className="secondary-button compact" onClick={closing.retry}>Повторить</button>
        </div>)}

      {state && (<>
          <dl className="closing-summary">
            <div><dt>Полностью оплачена</dt><dd>{formatTimestamp(state.fullyPaidAtUtc)}</dd></div>
            <div><dt>Последние документы</dt><dd>{formatDate(state.latestDocumentsReceivedDate)}</dd></div>
            <div><dt>Контрольная сумма</dt><dd>{formatMoney(state.confirmedClosingControlAmount, state.currencyCode)}</dd></div>
            <div><dt>Подтверждено закрытием</dt><dd>{formatMoney(confirmedTotal, state.currencyCode)}</dd></div>
            <div><dt>Остаток начисления</dt><dd>{formatMoney(remainingTotal, state.currencyCode)}</dd></div>
            <div><dt>Превышение контроля</dt><dd>{formatMoney(excess, state.currencyCode)}</dd></div>
          </dl>

          <div className="closing-table-wrap">
            <table className="closing-table">
              <caption>Закрытие строк расходной заявки</caption>
              <thead>
                <tr>
                  <th scope="col">№</th>
                  <th scope="col">Исходная сумма</th>
                  <th scope="col">Подтверждено</th>
                  <th scope="col">Остаток</th>
                  <th scope="col">Остаток, KZT</th>
                  {manualAllocation && canClose && <th scope="col">Распределить</th>}
                </tr>
              </thead>
              <tbody>
                {state.lines.map((line) => (<tr key={line.lineId}>
                    <th scope="row">{line.sortOrder}</th>
                    <td className="amount-cell">{formatMoney(line.originalAmount, state.currencyCode)}</td>
                    <td className="amount-cell">{formatMoney(line.confirmedClosedAmount, state.currencyCode)}</td>
                    <td className="amount-cell">{lineRemaining(line)} {line.remainingAmount > 0 ? state.currencyCode : ''}</td>
                    <td className="amount-cell">{formatMoney(line.remainingAmountKzt, 'KZT')}</td>
                    {manualAllocation && canClose && (<td className="amount-cell">
                        <input type="text" inputMode="decimal" aria-label={`Сумма закрытия строки ${line.sortOrder}`} value={formatMoneyInput(lineAmounts[line.sortOrder] ?? '')} onChange={(event) => setLineAmounts((current) => ({
                        ...current,
                        [line.sortOrder]: normalizeMoneyInput(event.target.value),
                    }))} disabled={busy || line.remainingAmount === 0}/>
                      </td>)}
                  </tr>))}
              </tbody>
            </table>
          </div>

          {!state.canExecuteClosingCommands && (<p className="closing-note" role="status">
              Состояние доступно только для чтения. Команды закрытия доступны ответственному бухгалтеру организации.
            </p>)}

          {canClose && (<div className="closing-actions">
              <section aria-labelledby="partial-closing-title" className="closing-action-card">
                <h4 id="partial-closing-title">Частичное закрытие</h4>
                <label className="closing-field required">
                  <span>Дата документов</span>
                  <input type="date" value={partialDate} onChange={(event) => setPartialDate(event.target.value)} disabled={busy}/>
                </label>
                <label className="closing-field required">
                  <span>Контрольная сумма</span>
                  <input type="text" inputMode="decimal" maxLength={24} value={formatMoneyInput(controlAmount)} onChange={(event) => setControlAmount(normalizeMoneyInput(event.target.value))} disabled={busy}/>
                </label>
                <label className="closing-field closing-field-wide required">
                  <span>Причина</span>
                  <textarea rows={2} maxLength={2000} value={partialReason} onChange={(event) => setPartialReason(event.target.value)} disabled={busy}/>
                </label>
                <label className="closing-check">
                  <input type="checkbox" checked={manualAllocation} onChange={(event) => setManualAllocation(event.target.checked)} disabled={busy}/>
                  <span>Распределить сумму по строкам вручную</span>
                </label>
                <button type="button" className="primary-button" disabled={busy} onClick={runPartial}>
                  {closing.executingCode === 'CLOSE_PARTIAL' ? 'Фиксация…' : 'Закрыть частично'}
                </button>
              </section>

              <section aria-labelledby="full-closing-title" className="closing-action-card">
                <h4 id="full-closing-title">Полное закрытие</h4>
                <label className="closing-field required">
                  <span>Дата документов</span>
                  <input type="date" value={fullDate} onChange={(event) => setFullDate(event.target.value)} disabled={busy}/>
                </label>
                <label className="closing-field closing-field-wide required">
                  <span>Причина</span>
                  <textarea rows={2} maxLength={2000} value={fullReason} onChange={(event) => setFullReason(event.target.value)} disabled={busy}/>
                </label>
                <button type="button" className="primary-button" disabled={busy} onClick={runFull}>
                  {closing.executingCode === 'CLOSE_FULL' ? 'Закрытие…' : 'Закрыть полностью'}
                </button>
              </section>
            </div>)}

          {canReopen && (<section aria-labelledby="reopen-title" className="closing-action-card closing-reopen">
              <h4 id="reopen-title">Переоткрытие</h4>
              <p className="closing-note">Подтверждённое закрытие текущего цикла будет сброшено; история сохранится.</p>
              <label className="closing-field closing-field-wide required">
                <span>Причина</span>
                <textarea rows={2} maxLength={2000} value={reopenReason} onChange={(event) => setReopenReason(event.target.value)} disabled={busy}/>
              </label>
              <button type="button" className="secondary-button" disabled={busy} onClick={runReopen}>
                {closing.executingCode === 'REOPEN' ? 'Переоткрытие…' : 'Переоткрыть заявку'}
              </button>
            </section>)}

          {validationError && <div className="closing-message error" role="alert">{validationError}</div>}
          {closing.commandError && <div className="closing-message error" role="alert">{closing.commandError}</div>}
          {closing.successMessage && <div className="closing-message success" role="status">{closing.successMessage}</div>}
        </>)}
    </section>);
}
