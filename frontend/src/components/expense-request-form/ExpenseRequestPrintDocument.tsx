import { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { getExpenseRequestAttachments, type ExpenseRequestAttachment } from '../../api';
import { calculateLineAmounts, isValidPositiveMoneyInput } from '../../api/expenseRequestCalculations';
import type { ExpenseRequestDraft, ReferenceDataItem, ReferenceCodeItem } from '../../types';
import type { ExpenseRequestSystemFields } from '../../utils/expenseRequestSystemFields';
import type { ExpenseLineBudgetControl } from '../../utils/expenseLineBudgetControl';
import type { ExpenseBudgetLimit } from '../../hooks/useExpenseBudgetLimits';
import { expenseLineBudgetVerdict } from '../../utils/expenseLineBudgetVerdict';
import { expenseBudgetMonthLabel362 } from '../../utils/expenseBudgetMonth362';
import { formatMoney, sameOpaqueIdentifier } from '../../utils/expenseRequestDraft';
import './ExpenseRequestPrintDocument.css';
import { operationNames, operationFieldNames, operationDisplay, type ExpensePaymentOperation } from '../../utils/expensePaymentOperation';
import { printPreparationErrorMessage } from '../../utils/uiErrorMessages';
export type ExpenseRequestPrintData = {
    draft: ExpenseRequestDraft;
    number: string | null;
    system: ExpenseRequestSystemFields;
    status: string;
    unsaved: boolean;
    organizations: ReferenceDataItem[];
    costCenters: ReferenceDataItem[];
    counterparties: ReferenceDataItem[];
    contracts: ReferenceDataItem[];
    articles: ReferenceDataItem[];
    paymentBases: ReferenceCodeItem[];
    residencies: ReferenceCodeItem[];
    budgetControls: readonly ExpenseLineBudgetControl[];
    budgetLimits: readonly ExpenseBudgetLimit[];
    requestId: string | null;
    version: string | null;
    uploadSessionId: string;
    stagedCount: number;
};
const day = (v: string) => /^(\d{4})-(\d{2})-(\d{2})$/u.test(v) ? v.split('-').reverse().join('.') : 'Не указана';
const ref = (items: readonly ReferenceDataItem[], id: string) => items.find(i => sameOpaqueIdentifier(i.id, id))?.name || (id ? 'Недоступно в справочнике' : 'Не указано');
const code = (items: readonly ReferenceCodeItem[], value: string) => items.find(i => i.code === value)?.name || value || 'Не указано';
const kinds = { SUPPORTING: 'Подтверждающий документ', NEW_CONTRACT: 'Новый договор', ADDITIONAL: 'Дополнительный документ' };
function Field({ label, value, wide = false }: {
    label: string;
    value: string;
    wide?: boolean;
}) {
    return <div className={wide ? 'print-field-wide' : undefined}><dt>{label}</dt><dd>{value || 'Не указано'}</dd></div>;
}
function Text({ label, value }: {
    label: string;
    value: string;
}) { return value.trim() ? <section className="expense-print-prose"><h2>{label}</h2><p>{value}</p></section> : null; }
export function ExpenseRequestPrintSheet({ data, attachments, ready = true }: {
    data: ExpenseRequestPrintData;
    attachments: readonly ExpenseRequestAttachment[];
    ready?: boolean;
}) {
    const { draft: d } = data, valid = d.lines.every(l => isValidPositiveMoneyInput(l.amount));
    const amounts = d.lines.map(l => calculateLineAmounts(l.amount, l.vatRateCode)), total = amounts.reduce((s, a) => s + a.amountKzt, 0);
    const budget = data.budgetControls.some(c => c.availableAmountKzt !== null) || data.budgetLimits.some(l => l.amountKzt !== null);
    return <section className="expense-request-print-summary" aria-label="Печатная форма расходной заявки" data-print-ready={ready}>
  <header className="expense-print-header"><div><p>BMS · Внутренний документ, не платёжное поручение</p><h1>Расходная заявка{data.number ? ' № ' + data.number : ''}</h1><p>от {data.system.createdDateLabel}</p></div><strong>{data.status}</strong></header>
  {data.unsaved && <p className="expense-print-warning">Печать текущих несохранённых данных. Сохранённая заявка может отличаться.</p>}
  {!ready && <p className="expense-print-warning">Подготовка перечня вложений не завершена.</p>}
  <dl className="expense-print-facts">
   <Field label="Операция" value={operationNames[d.paymentOperation?.kind ?? 'Supplier']} wide/>
   <Field label="Плательщик / организация" value={ref(data.organizations, d.organizationId)} wide/>
   {!d.paymentOperation && <Field label="Получатель / контрагент" value={d.isNewCounterparty ? d.candidateCounterpartyName : ref(data.counterparties, d.counterpartyId)} wide/>}
   <Field label="Инициатор" value={data.system.initiatorLabel}/><Field label="ЦФО заявки" value={ref(data.costCenters, d.defaultCostCenterId)}/>
   <Field label="Плановая дата оплаты" value={day(d.plannedPaymentDate)}/><Field label="Валюта" value={d.currencyCode || 'Не указана'}/>
   {!d.paymentOperation && <Field label="Основание оплаты" value={code(data.paymentBases, d.paymentBasisCode)}/>}<Field label="Срочность" value={d.isUrgent ? 'Срочная' : 'Обычная'}/>
  </dl>
  {d.paymentOperation && <dl className="expense-print-facts">{(Object.keys(operationFieldNames) as (keyof ExpensePaymentOperation)[]).filter(k => !['kind', 'employeeId', 'budgetCostCenterId'].includes(k) && (d.paymentOperation?.kind !== 'BusinessTrip' || k === 'employeeNames') && d.paymentOperation?.[k] != null && d.paymentOperation?.[k] !== '').map(k => <Field key={k} label={operationFieldNames[k]} value={operationDisplay(k, d.paymentOperation?.[k])} wide={k === 'employeeNames'}/>)}</dl>}
  {d.isNewCounterparty && <><h2>Новый контрагент</h2><dl className="expense-print-facts">
   <Field label="Резидентность" value={code(data.residencies, d.candidateResidencyCode)}/>
   {d.candidateResidencyCode === 'ResidentKz' ? <Field label="БИН / ИИН" value={d.candidateBinIin}/> : <><Field label="Страна" value={d.candidateCountry}/><Field label="Регистрационный номер" value={d.candidateRegistrationNumberMissing ? 'Отсутствует' : d.candidateForeignRegistrationNumber} wide/></>}
  </dl>{d.candidateRegistrationNumberMissing && <Text label="Причина отсутствия регистрационного номера" value={d.candidateRegistrationNumberMissingExplanation}/>}</>}
  {d.paymentBasisCode === 'Contract' && <><h2>{d.isNewContract ? 'Новый договор' : 'Договор'}</h2><dl className="expense-print-facts">
   {d.isNewContract ? <><Field label="Номер договора" value={d.candidateContractNumberMissing ? 'Без номера' : d.candidateContractNumber}/><Field label="Дата договора" value={day(d.candidateContractDate)}/></> : <Field label="Договор" value={ref(data.contracts, d.contractId)} wide/>}
  </dl>{d.isNewContract && <><Text label="Предмет договора" value={d.candidateContractSubject}/><Text label="Условия оплаты по договору" value={d.candidateContractTerms}/></>}</>}
  {d.paymentBasisCode === 'InvoiceWithoutContract' && <dl className="expense-print-facts"><Field label="Номер счёта" value={d.invoiceNumber}/><Field label="Дата счёта" value={day(d.invoiceDate)}/></dl>}
  {d.paymentBasisCode === 'PublicOffer' && <dl className="expense-print-facts"><Field label="Наименование оферты" value={d.publicOfferName} wide/><Field label="Ссылка / идентификатор оферты" value={d.publicOfferIdentifier} wide/></dl>}
  {d.paymentBasisCode === 'OtherBasis' && <Text label="Иное основание оплаты" value={d.otherBasisExplanation}/>} 
  <Text label="Назначение платежа" value={d.purpose || 'Не указано'}/>
  {d.paymentOperation && data.number && <Text label="Ссылка для сверки оплаты в 1С (добавить к назначению платежа)" value={'BMS:' + data.number}/>}
  <Text label="Бизнес-обоснование" value={d.justification}/><Text label="Комментарий инициатора" value={d.comment}/>
  {d.isUrgent && <Text label="Причина срочности" value={d.urgencyReason || 'Не указана'}/>}
  {d.paymentOperation?.kind !== 'PayrollAdvance' && <table className="expense-print-lines" aria-label="Строки расходов"><caption>Расходы · суммы с НДС</caption><colgroup><col className="print-col-index"/><col /><col className="print-col-cfo"/><col className="print-col-period"/><col className="print-col-money"/></colgroup>
   <thead><tr><th scope="col">№</th><th scope="col">Статья ДДС</th><th scope="col">ЦФО</th><th scope="col">Период</th><th scope="col">Сумма, {d.currencyCode || 'валюта не указана'}</th></tr></thead>
   <tbody>{d.lines.map((line, i) => {
                const accrual = data.articles.find(a => sameOpaqueIdentifier(a.id, line.cashFlowItemId))?.formsManagerialAccrual === true;
                return <tr key={line.clientRowKey ?? line.id ?? i}><td>{i + 1}</td><td>{ref(data.articles, line.cashFlowItemId)}{accrual && <small>НДС: {line.vatRateCode === 'WithoutVat' ? 'без НДС' : '16%'}{isValidPositiveMoneyInput(line.amount) ? `; сумма НДС ${formatMoney(amounts[i].vatAmount)}; без НДС ${formatMoney(amounts[i].amountExcludingVat)} ${d.currencyCode}` : ''}</small>}</td>
     <td>{ref(data.costCenters, line.accrualCostCenterId || line.costCenterId || d.defaultCostCenterId)}</td><td>{day(line.accrualPeriod)}</td><td className="print-money">{isValidPositiveMoneyInput(line.amount) ? formatMoney(amounts[i].amountKzt) : line.amount.trim() ? 'Некорректная сумма' : 'Не указана'}</td></tr>;
            })}</tbody>
   <tfoot><tr><th scope="row" colSpan={4}>Итого, {d.currencyCode || 'валюта не указана'}</th><td className="print-money">{valid ? formatMoney(total) : 'Не рассчитано'}</td></tr></tfoot>
  </table>}
  {budget && d.paymentOperation?.kind !== 'PayrollAdvance' && <table className="expense-print-budget" aria-label="Бюджетный контроль"><caption>Бюджетный контроль на момент печати, ₸</caption><thead><tr><th scope="col">Строка</th><th scope="col">Бюджет {expenseBudgetMonthLabel362(d.plannedPaymentDate)}</th><th scope="col">Остаток до заявки</th><th scope="col">Остаток после заявки</th><th scope="col">Бюджет</th></tr></thead>
   <tbody>{d.lines.map((l, i) => {
                const c = data.budgetControls[i], limit = data.budgetLimits[i], v = expenseLineBudgetVerdict(c, limit?.amountKzt, isValidPositiveMoneyInput(l.amount));
                const m = (x: number | null | undefined) => typeof x === 'number' && Number.isFinite(x) ? formatMoney(x) : 'Не определён';
                return <tr key={i}><td>{i + 1}</td><td className="print-money">{m(limit?.amountKzt)}</td><td className="print-money">{m(c?.availableBeforeRequestKzt)}</td><td className="print-money">{m(c?.availableAmountKzt)}</td><td>{v === 'over' ? 'Сверх бюджета' : v === 'within' ? 'В бюджете' : 'Не определён'}</td></tr>;
            })}</tbody></table>}
  <section className="expense-print-attachments"><h2>Вложения и подтверждающие документы</h2>
   {attachments.length > 0 ? <ol>{attachments.map(a => <li key={a.id}>{a.fileName} <span>({kinds[a.kind]})</span>{a.comment && <p>{a.comment}</p>}</li>)}</ol> : <p>{ready ? 'Вложенных файлов нет.' : 'Перечень загружается…'}</p>}
   {d.supportingDocumentMissing && <Text label="Подтверждающий документ отсутствует" value={d.supportingDocumentMissingExplanation || 'Отмечено в заявке'}/>}
   {d.isNewContract && d.newContractDocumentMissing && <Text label="Файл договора отсутствует" value={d.newContractDocumentMissingExplanation || 'Отмечено в заявке'}/>}
  </section>
 </section>;
}
export function ExpenseRequestPrintDocument({ data, onClose }: {
    data: ExpenseRequestPrintData;
    onClose: () => void;
}) {
    const required = !!data.requestId || data.stagedCount > 0;
    const [retry, setRetry] = useState(0), [state, setState] = useState<{
        kind: 'loading' | 'ready' | 'error';
        items: ExpenseRequestAttachment[];
        notice?: string;
    }>({ kind: required ? 'loading' : 'ready', items: [] });
    const close = useRef(onClose);
    close.current = onClose;
    useEffect(() => {
        if (!required)
            return;
        const c = new AbortController();
        setState({ kind: 'loading', items: [] });
        const timer = window.setTimeout(() => { c.abort(); setState({ kind: 'error', items: [], notice: 'Подготовка печати заняла слишком много времени. Повторите попытку.' }); }, 10000);
        void getExpenseRequestAttachments(data.requestId ?? data.uploadSessionId, c.signal, !data.requestId).then(value => {
            if (c.signal.aborted)
                return;
            if (data.requestId && (!data.version || !sameOpaqueIdentifier(value.requestVersion, data.version)))
                throw new Error('Заявка изменена. Закройте подготовку печати, сохраните введённые данные и обновите карточку.');
            setState({ kind: 'ready', items: value.items });
        }).catch(e => {
            if (!c.signal.aborted)
                setState({ kind: 'error', items: [], notice: printPreparationErrorMessage(e) });
        }).finally(() => window.clearTimeout(timer));
        return () => { window.clearTimeout(timer); c.abort(); };
    }, [data, required, retry]);
    useEffect(() => {
        if (state.kind !== 'ready')
            return;
        const finish = () => close.current();
        window.addEventListener('afterprint', finish);
        const frame = window.requestAnimationFrame(() => {
            try {
                window.print();
            }
            catch {
                setState(s => ({ ...s, kind: 'error', notice: 'Браузер не открыл печать. Повторите попытку.' }));
            }
        });
        return () => { window.cancelAnimationFrame(frame); window.removeEventListener('afterprint', finish); };
    }, [state.kind, retry]);
    return createPortal(<><ExpenseRequestPrintSheet data={data} attachments={state.items} ready={state.kind === 'ready'}/>
  {state.kind !== 'ready' && <div className="expense-print-preparation" role="dialog" aria-label="Подготовка печати"><div className="form-card"><h2>Подготовка печатной формы</h2><p role={state.kind === 'error' ? 'alert' : 'status'}>{state.kind === 'error' ? state.notice : 'Загружаем перечень вложений…'}</p>{state.kind === 'error' && <button type="button" onClick={() => setRetry(r => r + 1)}>Повторить подготовку печати</button>}<button type="button" onClick={() => close.current()}>Закрыть подготовку печати</button></div></div>}</>, document.body);
}
