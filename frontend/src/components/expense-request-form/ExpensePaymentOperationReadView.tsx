import { operationNames, operationFieldNames, operationDisplay, isBudgetTaxKind, type ExpensePaymentOperation } from '../../utils/expensePaymentOperation';
import { formatMoney } from '../../utils/expenseRequestDraft';
import { ExpenseReadField, expenseViewDate, expenseViewMonth } from './ExpenseRequestReadView';
import { BusinessTripEmployees399 } from './BusinessTripEmployees399';
/** Explicit allow-list: the advance's hidden HR budget key never enters the view. */
export function ExpensePaymentOperationReadView({ operation: o, requestNumber }: {
    operation: ExpensePaymentOperation;
    requestNumber?: string;
}) {
    if (o.kind === 'BusinessTrip')
        return <section id="expense-panel-counterparty" role="tabpanel" aria-labelledby="expense-tab-counterparty" className="form-card">
    <div className="section-title">Командировочные расходы · сотрудники</div>
    <BusinessTripEmployees399 names={o.employeeNames} viewOnly/>
    {requestNumber && <p className="section-hint">Ссылка для сверки оплаты: BMS:{requestNumber}. Факт оплаты подтверждается данными обмена.</p>}
  </section>;
    const tax = o.kind === 'Tax', payroll = o.kind === 'Payroll', accountable = o.kind === 'Accountable';
    const social = tax && !isBudgetTaxKind(o.paymentKind);
    const register = social || (payroll && o.payrollMode === 'Register');
    const employee = accountable || (payroll && o.payrollMode === 'Employee');
    const bank = !tax && o.transferMethod === 'Bank';
    function field(key: keyof ExpensePaymentOperation, format?: 'date' | 'month' | 'money', label = operationFieldNames[key], wide = false) {
        const value = o[key];
        const text = format === 'date' ? expenseViewDate(typeof value === 'string' ? value : '')
            : format === 'month' ? expenseViewMonth(typeof value === 'string' ? value : '')
                : format === 'money' ? typeof value === 'number' && Number.isFinite(value) ? formatMoney(value) : 'Не указано'
                    : operationDisplay(key, value);
        return <ExpenseReadField key={key} label={label} wide={wide}>{text}</ExpenseReadField>;
    }
    return <section id="expense-panel-counterparty" role="tabpanel" aria-labelledby="expense-tab-counterparty" className="form-card">
    <div className="section-title">{operationNames[o.kind]} · реквизиты</div>
    {o.kind === 'PayrollAdvance' ? <dl className="expense-view-grid">
      {field('transferMethod')}{field('period', 'month')}{field('employeeName')}{field('advanceAmount', 'money')}
    </dl> : <>
      <dl className="expense-view-grid">
        {tax && <>
          {field('paymentKind')}{field('isPenalty')}{field('period', social ? 'month' : undefined)}
          {!social && <>{field('taxAuthorityName')}{field('taxAuthorityCode')}{field('budgetCode')}</>}
          {field('paymentCode')}
        </>}
        {!tax && field('transferMethod')}
        {payroll && <>{field('payrollMode', undefined, 'Оформление зарплаты')}{field('period', 'month')}</>}
        {employee && <>{field('employeeName')}{field('employeeIin')}</>}
        {register && <>{field('registerNumber')}{field('registerDate', 'date')}{field('registerAmount', 'money')}</>}
        {accountable && <>{field('advancePurpose', undefined, undefined, true)}{field('advanceReportDueDate', 'date')}</>}
      </dl>
      {bank && <div className="expense-view-subsection"><h3>Банковские реквизиты получателя</h3>
        <dl className="expense-view-grid">
          {field('recipientName')}{field('recipientBinIin')}{field('recipientIban')}{field('recipientBik')}{field('recipientKbe')}{!tax && field('paymentCode')}
        </dl>
      </div>}
      {!bank && o.transferMethod === 'Cash' && <p className="section-hint">Выплата через кассу; банковский счёт не требуется.</p>}
      {!tax && requestNumber && <p className="section-hint">Ссылка для сверки оплаты: BMS:{requestNumber}. Факт оплаты подтверждается данными обмена.</p>}
    </>}
  </section>;
}
