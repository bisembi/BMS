import { useEffect, useRef, useState } from 'react';
import { apiUrl, fetchWithSafeTransport, secureReadInit } from '../../api/http';
import { operationNames, taxNames, isBudgetTaxKind, socialPaymentCodes, operationFieldNames, type ExpensePaymentOperation as Op } from '../../utils/expensePaymentOperation';
import { MoneyInput } from './MoneyInput';
import { ExpensePaymentOperationReadView } from './ExpensePaymentOperationReadView';
import { exactMoneyNumber } from '../../utils/moneyInput';
import { BusinessTripEmployees399 } from './BusinessTripEmployees399';
function OperationMoneyAmount({ value, onChange }: {
    value: number | null | undefined;
    onChange: (value: number | null) => void;
}) {
    const [text, setText] = useState(value == null ? '' : String(value));
    const written = useRef(value);
    useEffect(() => {
        if (value !== written.current) {
            setText(value == null ? '' : String(value));
            written.current = value;
        }
    }, [value]);
    return <MoneyInput value={text} onValueChange={next => {
            setText(next);
            // Validate the original cents before Number can discard them. A trailing
            // decimal separator remains an editable value, as before.
            const amount = exactMoneyNumber(next.endsWith('.') ? next + '0' : next, true);
            written.current = amount;
            onChange(amount);
        }} aria-invalid={text !== '' && exactMoneyNumber(text.endsWith('.') ? text + '0' : text, true) === null}/>;
}
type Options = {
    employees: {
        id: string;
        name: string;
    }[];
    taxAuthorities: {
        id: string;
        name: string;
        bin?: string | null;
    }[];
};
export function ExpensePaymentOperationPanel({ operation: o, onChange, requestNumber, viewOnly = false }: {
    operation: Op;
    onChange: (v: Op) => void;
    requestNumber?: string;
    viewOnly?: boolean;
}) {
    const [options, setOptions] = useState<Options>({ employees: [], taxAuthorities: [] }), [error, setError] = useState(false), [retry, setRetry] = useState(0);
    useEffect(() => {
        if (viewOnly || o.kind === 'PayrollAdvance' || o.kind === 'BusinessTrip')
            return;
        const c = new AbortController();
        setError(false);
        void fetchWithSafeTransport(apiUrl('/api/v1/reference-data/payment-operation-options'), { ...secureReadInit(), signal: c.signal }).then(async (r) => {
            if (!r.ok)
                throw Error();
            const v: unknown = await r.json();
            if (!v || typeof v !== 'object' || !('employees' in v) || !('taxAuthorities' in v) || !Array.isArray(v.employees) || !Array.isArray(v.taxAuthorities))
                throw Error();
            for (const x of [...v.employees, ...v.taxAuthorities])
                if (!x || typeof x.id !== 'string' || typeof x.name !== 'string' || x.name.length > 1000)
                    throw Error();
            if (!c.signal.aborted)
                setOptions(v as Options);
        }).catch(() => {
            if (!c.signal.aborted)
                setError(true);
        });
        return () => c.abort();
    }, [retry, o.kind, viewOnly]);
    if (viewOnly)
        return <ExpensePaymentOperationReadView operation={o} requestNumber={requestNumber}/>;
    const patch = (v: Partial<Op>) => onChange({ ...o, ...v });
    if (o.kind === 'BusinessTrip')
        return <section id="expense-panel-counterparty" role="tabpanel" aria-labelledby="expense-tab-counterparty" className="form-card">
  <div className="section-title">Командировочные расходы · сотрудники</div>
  <BusinessTripEmployees399 names={o.employeeNames} onChange={employeeNames => patch({ employeeNames })}/>
 </section>;
    const input = (key: keyof Op, type = 'text', required = true) => <label className={`field${required ? ' required' : ''}`} key={key}><span>{operationFieldNames[key]}</span>
  <input type={type} maxLength={['employeeIin', 'recipientBinIin'].includes(key) ? 12 : key === 'budgetCode' ? 6 : key === 'paymentCode' ? 3 : key === 'recipientKbe' ? 2 : key === 'recipientIban' ? 34 : key === 'recipientBik' ? 11 : key === 'advancePurpose' ? 1000 : key === 'employeeName' ? 150 : 300} step={key === 'registerAmount' ? '0.01' : undefined} min={key === 'registerAmount' ? '0.01' : undefined} value={String(o[key] ?? '')} onChange={e => patch({ [key]: key === 'registerAmount' ? exactMoneyNumber(e.target.value, true) : e.target.value })}/></label>;
    const tax = o.kind === 'Tax', payroll = o.kind === 'Payroll', advance = o.kind === 'Accountable', social = tax && !isBudgetTaxKind(o.paymentKind);
    const register = social || (payroll && o.payrollMode === 'Register'), employee = advance || (payroll && o.payrollMode === 'Employee'), bank = !tax && o.transferMethod === 'Bank';
    function replaceScenario(v: Op) {
        if (window.confirm('Реквизиты предыдущего варианта будут очищены. Продолжить?'))
            onChange(v);
    }
    if (o.kind === 'PayrollAdvance')
        return <section id="expense-panel-counterparty" role="tabpanel" aria-labelledby="expense-tab-counterparty" className="form-card">
  <div className="section-title">Выплата аванса · реквизиты</div>
  <div className="form-grid">
   <label className="field required"><span>Способ выплаты</span><select value={o.transferMethod ?? ''} onChange={e => patch({ transferMethod: e.target.value as 'Bank' | 'Cash' })}>
    <option value="">Не выбран</option><option value="Bank">Банковский перевод</option><option value="Cash">Наличными</option></select></label>
   {input('period', 'month')}{input('employeeName')}
   <label className="field required"><span>Сумма аванса</span><OperationMoneyAmount value={o.advanceAmount} onChange={advanceAmount => patch({ advanceAmount })}/></label>
  </div>
 </section>;
    return <section id="expense-panel-counterparty" role="tabpanel" aria-labelledby="expense-tab-counterparty" className="form-card">
  <div className="section-title">{operationNames[o.kind]} · реквизиты</div>
  {!tax && <><p className="muted">Проверьте реквизиты по первичному документу. Суммы удержаний и взносов автоматически не рассчитываются. Для операции применяется «Без НДС».</p>
  <p className="muted">Для автоматической сверки укажите в назначении платежа 1С точную ссылку{' '}
   {requestNumber ? <strong>BMS:{requestNumber}</strong> : 'BMS:номер-заявки (появится после сохранения)'}. Она не заменяет обязательное назначение платежа.
   Дата фактической оплаты поступает из проведённых банковских / кассовых записей. Без точной ссылки оплата автоматически не подтверждается.</p></>}
  {error && <p role="status">Справочник временно недоступен. Можно ввести реквизиты вручную. <button type="button" onClick={() => setRetry(x => x + 1)}>Повторить загрузку</button></p>}
  <div className="form-grid">
   {tax && <><label className="field required"><span>Вид платежа</span><select value={o.paymentKind ?? ''} onChange={e => {
                const kind = e.target.value as NonNullable<Op['paymentKind']>;
                replaceScenario({ kind: 'Tax', paymentKind: kind, paymentCode: socialPaymentCodes[kind]?.[0] ?? '911' });
            }}>{Object.entries(taxNames).map(([id, name]) => <option value={id} key={id}>{name}</option>)}</select></label>
   <label className="field checkbox-field"><input type="checkbox" checked={o.isPenalty ?? false} onChange={e => patch({ isPenalty: e.target.checked, paymentCode: socialPaymentCodes[o.paymentKind ?? '']?.[e.target.checked ? 1 : 0] ?? (e.target.checked ? '912' : '911') })}/><span>Пеня</span></label>
   {input('period', social ? 'month' : 'text')}
   {!social && <p className="muted" style={{ gridColumn: '1 / -1' }}>Период налога: ГГГГ-ММ, ГГГГ-Q1…Q4 или ГГГГ — по обязательству. КБК и УГД выбираются по реквизитам конкретного налога.</p>}
   {!social && <><label className="field required"><span>Наименование УГД</span><input list="operation-tax-authorities" value={o.taxAuthorityName ?? ''} maxLength={300} onChange={e => patch({ taxAuthorityName: e.target.value, taxAuthorityCode: null })}/><datalist id="operation-tax-authorities">{options.taxAuthorities.map(x => <option key={x.id} value={x.name}/>)}</datalist></label>{input('taxAuthorityCode')}{input('budgetCode')}</>}
   {input('paymentCode')}
   </>}
   {!tax && <label className="field required"><span>Способ выплаты</span><select value={o.transferMethod ?? ''} onChange={e => {
                const method = e.target.value as 'Bank' | 'Cash';
                if (method === 'Cash') {
                    if (!window.confirm('Банковские реквизиты будут очищены. Продолжить?'))
                        return;
                    patch({ transferMethod: method, recipientIban: null, recipientBik: null, recipientKbe: null, paymentCode: null });
                }
                else
                    patch({ transferMethod: method });
            }}><option value="">Не выбран</option><option value="Bank">Банковский перевод</option><option value="Cash">Наличными</option></select></label>}
   {payroll && <><label className="field required"><span>Оформление зарплаты</span><select value={o.payrollMode ?? ''} onChange={e => replaceScenario({ kind: 'Payroll', payrollMode: e.target.value as 'Register' | 'Employee', transferMethod: o.transferMethod })}><option value="Register">По ведомости</option><option value="Employee">Отдельному сотруднику</option></select></label>{input('period', 'month')}</>}
   {employee && <><label className="field"><span>Сотрудник справочника BMS</span><select value={o.employeeId ?? ''} onChange={e => { const person = options.employees.find(x => x.id === e.target.value); patch({ employeeId: person?.id ?? null, employeeName: person?.name ?? '', employeeIin: null, recipientName: person?.name ?? null, recipientBinIin: null, recipientIban: null, recipientBik: null, recipientKbe: null }); }}><option value="">Ручной ввод / сотрудник вне BMS</option>{o.employeeId && !options.employees.some(x => x.id === o.employeeId) && <option value={o.employeeId}>{o.employeeName} (сохранённый выбор)</option>}{options.employees.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
   <label className="field required"><span>ФИО сотрудника</span><input value={o.employeeName ?? ''} readOnly={!!o.employeeId} maxLength={300} onChange={e => patch({ employeeName: e.target.value, recipientName: e.target.value })}/></label>
   <label className="field required"><span>ИИН сотрудника</span><input value={o.employeeIin ?? ''} inputMode="numeric" maxLength={12} onChange={e => patch({ employeeIin: e.target.value, recipientBinIin: e.target.value })}/></label></>}
   {register && <>{input('registerNumber')}{input('registerDate', 'date')}{input('registerAmount', 'number')}<p className="muted" style={{ gridColumn: '1 / -1' }}>Приложите ведомость / реестр как подтверждающий документ на первой вкладке. Итог реестра должен совпадать с итогом заявки.</p></>}
   {advance && <>{input('advancePurpose')}{input('advanceReportDueDate', 'date')}</>}
  </div>
  {bank && <><h3>Банковские реквизиты получателя</h3><div className="form-grid">
   {input('recipientName')}{input('recipientBinIin')}{input('recipientIban')}{input('recipientBik')}{input('recipientKbe')}{!tax && input('paymentCode')}
  </div><p className="muted">БИН / ИИН, IBAN, БИК, КБе и КНП заполняются по платёжным реквизитам. Выбор сотрудника не подставляет его банковский счёт.</p></>}
  {!bank && o.transferMethod === 'Cash' && <p>Выплата через кассу; банковский счёт не требуется. Факт оплаты подтверждается данными обмена, а не заполнением этой формы.</p>}
 </section>;
}
