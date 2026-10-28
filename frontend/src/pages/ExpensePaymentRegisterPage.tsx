import { useEffect, useState } from 'react';
import { ArrowLeft, Printer, RefreshCw } from 'lucide-react';
import { getExpensePaymentRegister, formatRegisterMoney, type ExpensePaymentRegister } from '../api/expensePaymentRegister';
import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import './ExpensePaymentRegisterPage.css';
const date = (value: string | null) => value ? value.split('-').reverse().join('.') : '—';
export function ExpensePaymentRegisterPage({ onBack }: {
    onBack: () => void;
}) {
    const refresh = useWorkspaceRefreshRevision();
    const [retry, setRetry] = useState(0);
    const key = `${refresh}/${retry}`;
    const [capture, setCapture] = useState<{
        key: string;
        data: ExpensePaymentRegister | null;
    } | null>(null);
    const current = capture?.key === key ? capture : null;
    const loading = current === null, data = current?.data;
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        const controller = new AbortController();
        void getExpensePaymentRegister(controller.signal)
            .then(data => {
            if (!controller.signal.aborted)
                setCapture({ key, data });
        })
            .catch(() => {
            if (!controller.signal.aborted)
                setCapture({ key, data: null });
        });
        return () => controller.abort();
    }, [key]);
    return <section className="payment-register-page" aria-label="Реестр заявок к оплате" aria-busy={loading}>
    <div className="document-heading"><h1>Реестр заявок к оплате</h1></div>
    <div className="list-toolbar register-actions">
      <button type="button" className="secondary-button compact" onClick={onBack}><ArrowLeft size={15}/>К заявкам</button>
      <button type="button" className="secondary-button compact" disabled={loading} onClick={() => setRetry(value => value + 1)}><RefreshCw size={15}/>Обновить</button>
      <span className="list-toolbar-spacer"/>
      <button type="button" className="primary-button compact" disabled={!data || loading} onClick={() => window.print()}><Printer size={15}/>Печать / PDF</button>
    </div>
    {loading ? <p role="status">Формирование реестра…</p> : !data ? <div role="alert" className="notice error">Не удалось загрузить полный реестр. Повторите обновление.</div> : <>
      <p className="register-timestamp">На {new Intl.DateTimeFormat('ru-RU', { timeZone: 'Asia/Almaty', dateStyle: 'short', timeStyle: 'short' }).format(new Date(data.generatedAtUtc))} (UTC+5)</p>
      <div className="payment-register-table-wrap"><table className="payment-register-table" aria-label="Заявки к оплате">
        <colgroup><col /><col /><col /><col /><col /><col /><col /></colgroup>
        <thead><tr>{['Организация', 'Планируемая дата', 'ЦФО', 'Инициатор', 'Контрагент', 'Сумма в валюте', 'Эквивалент, ₸'].map(name => <th key={name} scope="col">{name}</th>)}</tr></thead>
        <tbody>{data.items.map(row => <tr key={row.expenseRequestId}>
          <td>{row.organizationName}</td><td className="register-date">{date(row.plannedPaymentDate)}</td><td>{row.costCenterName}</td>
          <td>{row.initiatorName}</td><td>{row.counterpartyName}</td>
          <td className="register-money">{formatRegisterMoney(row.amount)} {row.currencyCode}</td>
          <td className="register-money">{row.equivalentKzt === null ? 'Не рассчитан' : formatRegisterMoney(row.equivalentKzt)}</td>
        </tr>)}{data.items.length === 0 && <tr><td colSpan={7}>Заявок к оплате нет.</td></tr>}</tbody>
        <tfoot><tr><th scope="row" colSpan={5}>ИТОГО</th><td className="register-money">
          {data.currencyTotals.length ? data.currencyTotals.map(total => <div key={total.currencyCode}>{formatRegisterMoney(total.amount)} {total.currencyCode}</div>) : '—'}
        </td><td className="register-money">{data.totalEquivalentKzt === null ? 'Не рассчитан' : formatRegisterMoney(data.totalEquivalentKzt)}</td></tr></tfoot>
      </table></div>
      {data.hasUnknownEquivalents && <p className="notice warning">Не у всех заявок рассчитан эквивалент. Общий итог в тенге не определён.</p>}
    </>}
  </section>;
}
