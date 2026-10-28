import { useState } from 'react';
import { MoneyInput } from './MoneyInput';
import { isValidPositiveMoneyInput } from '../../api/expenseRequestCalculations';
import { Plus, Trash2 } from 'lucide-react';
import { formatMoney, opaqueReferenceOptionValue, sameOpaqueIdentifier } from '../../utils/expenseRequestDraft';
import { parseVatRateCode } from '../../utils/expenseRequestSelectValues';
import { CashFlowItemCombobox } from '../reference-directory/CashFlowItemCombobox';
import type { ExpenseLineDraft, ReferenceDataItem } from '../../types';
import type { ExpenseLineBudgetControl } from '../../utils/expenseLineBudgetControl';
import type { ExpenseBudgetLimit } from '../../hooks/useExpenseBudgetLimits';
import './ExpenseRequestLinesPanel.css';
import { expenseLineBudgetVerdict } from '../../utils/expenseLineBudgetVerdict';
import { expenseBudgetMonthLabel362 } from '../../utils/expenseBudgetMonth362';
import { expenseViewMonth, expenseViewReference } from './ExpenseRequestReadView';
import type { AccrualDepartmentCatalog485 } from '../../hooks/useAccrualDepartments485';
import { AccrualDepartmentsNotice485 } from './AccrualDepartmentsNotice485';
export type ExpenseLineCalculatedAmounts = {
    vatAmount: number;
    amountExcludingVat: number;
};
type Props = {
    viewOnly?: boolean;
    withoutVat?: boolean;
    lines: readonly ExpenseLineDraft[];
    lineAmounts: readonly ExpenseLineCalculatedAmounts[];
    currencyCode: string;
    cashFlowItems: readonly ReferenceDataItem[];
    cashFlowItemLabels?: readonly ReferenceDataItem[];
    costCenters: readonly ReferenceDataItem[];
    departmentCatalog?: AccrualDepartmentCatalog485;
    showDepartmentNotice?: boolean;
    total: number;
    totalVat: number;
    totalExcludingVat: number;
    budgetControls?: readonly ExpenseLineBudgetControl[];
    budgetLimits?: readonly ExpenseBudgetLimit[];
    plannedPaymentDate?: string;
    accrualSettingsLoading?: boolean;
    onOpenBudget?: (index: number, source: HTMLAnchorElement) => void;
    onAddLine: () => void;
    onUpdateLine: (index: number, patch: Partial<ExpenseLineDraft>) => void;
    onRemoveLine: (index: number) => void;
};
export function ExpenseRequestLinesPanel({ viewOnly = false, withoutVat = false, lines, lineAmounts, currencyCode, cashFlowItems, cashFlowItemLabels, costCenters, departmentCatalog, showDepartmentNotice = true, total, budgetControls, budgetLimits, plannedPaymentDate, accrualSettingsLoading = false, onOpenBudget, onAddLine, onUpdateLine, onRemoveLine, }: Props) {
    const [pendingLineRemoval, setPendingLineRemoval] = useState<number | null>(null);
    const invalidAmounts = lines.some(line => line.amount.trim() !== '' && !isValidPositiveMoneyInput(line.amount));
    const accrualRows = lines.map(line => (cashFlowItemLabels?.find(item => sameOpaqueIdentifier(item.id, line.cashFlowItemId))
        ?? cashFlowItems.find(item => sameOpaqueIdentifier(item.id, line.cashFlowItemId)))?.formsManagerialAccrual === true);
    const hasAccrual = accrualRows.some(Boolean);
    const departmentSelectionUnavailable = departmentCatalog !== undefined && (departmentCatalog.status !== 'ready' || departmentCatalog.items.length === 0);
    const accrualVat = lineAmounts.reduce((sum, amounts, index) => sum + (accrualRows[index] ? amounts.vatAmount : 0), 0);
    const accrualNet = lineAmounts.reduce((sum, amounts, index) => sum + (accrualRows[index] ? amounts.amountExcludingVat : 0), 0);
    const addLine = () => {
        if (lines.length >= 10000)
            return;
        setPendingLineRemoval(null);
        onAddLine();
    };
    const updateLine = (index: number, patch: Partial<ExpenseLineDraft>) => {
        setPendingLineRemoval(null);
        onUpdateLine(index, patch);
    };
    const removeLine = (index: number) => {
        if (pendingLineRemoval === index) {
            setPendingLineRemoval(null);
            onRemoveLine(index);
        }
        else {
            setPendingLineRemoval(index);
        }
    };
    return (<section id="expense-panel-lines" role="tabpanel" aria-labelledby="expense-tab-lines" className="form-card lines-card">
      <div className="lines-heading">
        <div><div className="section-title" id="lines-title">Расходы</div><div className="section-hint">{accrualSettingsLoading ? 'Загружаем настройки акрула выбранной статьи…' : viewOnly ? 'Суммы с НДС' : 'Суммы вводятся с НДС'}</div></div>
        {!viewOnly && <button className="secondary-button compact" type="button" disabled={lines.length >= 10000} title={lines.length >= 10000 ? 'Максимум 10 000 строк в заявке' : undefined} onClick={addLine}><Plus size={17}/>Добавить строку</button>}
      </div>
      {hasAccrual && showDepartmentNotice && departmentCatalog && <AccrualDepartmentsNotice485 catalog={departmentCatalog}/>}
      <div className="expense-table-wrap">
        <table className={`expense-table${viewOnly ? ' expense-view-table' : ''}${budgetControls ? ' has-inline-budget' : ''}${hasAccrual ? '' : ' no-accrual-columns'}`} aria-label="Строки расходной заявки">
          <colgroup>
            <col className="expense-col-number"/>
            <col className="expense-col-cash-flow"/>
            {hasAccrual && <><col className="expense-col-cost-center"/><col className="expense-col-period"/><col className="expense-col-vat-rate"/></>}
            <col className="expense-col-amount"/>
            {budgetLimits && <col className="expense-col-budget"/>}
            {budgetControls && <><col className="expense-col-budget"/><col className="expense-col-budget"/><col className="expense-col-verdict"/></>}
            {hasAccrual && <><col className="expense-col-vat-amount"/><col className="expense-col-net-amount"/></>}
            {!viewOnly && <col className="table-action-column"/>}
          </colgroup>
          <thead><tr>
            <th scope="col">№</th><th scope="col">Статья<br /> ДДС</th>
            {hasAccrual && <><th scope="col">Подразделение<br /> затрат</th><th scope="col">Период<br /> начисления</th><th scope="col">Ставка<br /> НДС</th></>}
            <th scope="col">Сумма<br /> с НДС</th>
            {budgetLimits && <th scope="col" title="Утверждённый расходный лимит по ЦФО, статье и месяцу планируемой оплаты, до вычета факта и резервов">Бюджет<br /> {expenseBudgetMonthLabel362(plannedPaymentDate)}</th>}
            {budgetControls && <><th scope="col">Остаток бюджета<br /> до заявки, ₸</th><th scope="col">Остаток бюджета<br /> после заявки, ₸</th><th scope="col">Бюджет</th></>}
            {hasAccrual && <><th scope="col">Сумма<br /> НДС</th><th scope="col">Сумма<br /> без НДС</th></>}
            {!viewOnly && <th scope="col" aria-label="Действия"/>}
          </tr></thead>
          <tbody>
            {lines.map((line, index) => {
            const calculated = lineAmounts[index];
            const hasAmount = line.amount.trim() !== '';
            const validAmount = isValidPositiveMoneyInput(line.amount);
            const budget = budgetControls?.[index];
            const accrual = accrualRows[index];
            const budgetHint = budget?.state === 'pending' ? 'Расчёт остатка…' : 'Лимит и остаток недоступны';
            const articleLabel = viewOnly ? expenseViewReference([...(cashFlowItemLabels ?? []), ...cashFlowItems], line.cashFlowItemId, 'Выбранная статья ДДС недоступна в справочнике', true) : undefined;
            const departmentLabel = viewOnly && accrual ? expenseViewReference(costCenters, line.accrualCostCenterId ?? line.costCenterId, departmentSelectionUnavailable ? 'Название подразделения не загружено' : 'Выбранное подразделение недоступно') : undefined;
            return (<tr key={index}>
                  <td>{index + 1}</td>
                  <td>{viewOnly ? <span className="expense-view-cell" aria-label={`Статья ДДС, строка ${index + 1}`} title={articleLabel}>
                    {articleLabel}
                  </span> : <CashFlowItemCombobox ariaLabel={`Статья ДДС, строка ${index + 1}`} value={line.cashFlowItemId} items={cashFlowItems} displayItem={cashFlowItemLabels?.find(item => sameOpaqueIdentifier(item.id, line.cashFlowItemId))} emptyLabel="Не выбрана" onChange={(cashFlowItemId) => updateLine(index, { cashFlowItemId })}/>}</td>
                  {hasAccrual && <><td>{accrual ? viewOnly ? <span className="expense-view-cell" aria-label={`Подразделение затрат, строка ${index + 1}`} title={departmentLabel}>
                    {departmentLabel}
                  </span> : <select disabled={departmentSelectionUnavailable} aria-label={`Подразделение затрат, строка ${index + 1}`} value={line.accrualCostCenterId ?? line.costCenterId} onChange={(event) => {
                            if (!departmentSelectionUnavailable)
                                updateLine(index, { accrualCostCenterId: event.target.value });
                        }}><option value="">Не выбрано</option>{(line.accrualCostCenterId ?? line.costCenterId) && !costCenters.some((item) => sameOpaqueIdentifier(item.id, line.accrualCostCenterId ?? line.costCenterId)) && <option value={line.accrualCostCenterId ?? line.costCenterId}>{departmentSelectionUnavailable ? 'Текущее подразделение сохранено' : 'Выбранное подразделение недоступно'}</option>}{costCenters.map((item) => <option key={item.id} value={opaqueReferenceOptionValue(item.id, line.accrualCostCenterId ?? line.costCenterId)}>{item.name}</option>)}</select> : '—'}</td>
                  <td>{accrual ? viewOnly ? <span className="expense-view-cell" aria-label={`Период начисления, строка ${index + 1}`}>{expenseViewMonth(line.accrualPeriod)}</span>
                        : <input aria-label={`Период начисления, строка ${index + 1}`} type="month" value={line.accrualPeriod.slice(0, 7)} onChange={(event) => updateLine(index, { accrualPeriod: event.target.value ? `${event.target.value}-01` : '' })}/> : '—'}</td></>}
                  {hasAccrual && <td>{accrual ? viewOnly ? <span className="expense-view-cell" aria-label={`Ставка НДС, строка ${index + 1}`}>{line.vatRateCode === 'Vat16' ? '16%' : line.vatRateCode === 'WithoutVat' ? 'Без НДС' : 'Ставка недоступна'}</span>
                        : <select disabled={withoutVat} aria-label={`Ставка НДС, строка ${index + 1}`} value={line.vatRateCode} onChange={e => {
                                const rate = parseVatRateCode(e.target.value);
                                if (rate !== null)
                                    updateLine(index, { vatRateCode: rate });
                            }}><option value="Vat16">16%</option><option value="WithoutVat">Без НДС</option></select> : '—'}</td>}
                  <td>{viewOnly ? <span className="expense-view-cell expense-view-amount" aria-label={`Сумма, строка ${index + 1}`}>
                    {validAmount ? formatMoney(Number(line.amount)) : hasAmount ? 'Некорректная сумма' : 'Не указано'}
                  </span> : <MoneyInput aria-label={`Сумма, строка ${index + 1}`} aria-invalid={hasAmount && !validAmount} className="money-input" maxLength={24} value={line.amount} onValueChange={amount => updateLine(index, { amount })} placeholder="0,00"/>}</td>
                  {budgetLimits && <td className="inline-budget-cell"><output aria-label={`Бюджет, строка ${index + 1}`} title="Утверждённый лимит по статье на месяц оплаты; не остаток после расходов">
                    {budgetLimits[index]?.amountKzt != null ? formatMoney(budgetLimits[index].amountKzt)
                        : budgetLimits[index]?.state === 'loading' ? 'Загрузка…' : line.cashFlowItemId ? 'Недоступен' : '—'}
                  </output></td>}
                  {budgetControls && <>
                    <td className="inline-budget-cell"><output aria-label={`Остаток бюджета до заявки, строка ${index + 1}`} title="Текущий расчёт: лимит минус факт 1С и резервы других заявок. Все строки этой заявки по одному бюджетному ключу исключены вместе. Не исторический остаток на дату создания; после начала оплаты недоступен.">
                      {budget?.availableBeforeRequestKzt != null ? formatMoney(budget.availableBeforeRequestKzt)
                        : budget?.reason ? <small className="budget-input-hint">{budget.reason}</small> : '—'}
                    </output></td>
                    <td className="inline-budget-cell"><output aria-label={`Остаток бюджета, строка ${index + 1}`} title={budget?.state === 'preview' ? 'Остаток после всех введённых сумм по статье, с учётом других резервов и оплат; предварительный расчёт' : budget?.state === 'saved' ? 'Остаток с учётом резервов и оплат; общий для одинакового ЦФО, статьи и месяца оплаты' : budgetHint}>
                      {budget?.availableAmountKzt !== null && budget?.availableAmountKzt !== undefined ? formatMoney(budget.availableAmountKzt)
                        : budget?.reason ? <small className="budget-input-hint">{budget.reason}</small> : '—'}
                    </output></td>
                    <td className="inline-budget-status">{onOpenBudget && budgetLimits?.[index]?.state === 'ready' && expenseLineBudgetVerdict(budget, budgetLimits[index].amountKzt, validAmount) !== 'unknown'
                        ? <a href="#expense-panel-explanation" className={`expense-budget-link440 line-budget-verdict ${expenseLineBudgetVerdict(budget, budgetLimits[index].amountKzt, validAmount)}`} aria-label={`Расшифровка бюджета, строка ${index + 1}`} onClick={event => { event.preventDefault(); onOpenBudget(index, event.currentTarget); }}>
                        {expenseLineBudgetVerdict(budget, budgetLimits[index].amountKzt, validAmount) === 'over' ? 'Сверх бюджета' : 'В бюджете'}</a>
                        : <span className={`line-budget-verdict ${expenseLineBudgetVerdict(budget, budgetLimits?.[index]?.amountKzt, validAmount)}`}>
                      {expenseLineBudgetVerdict(budget, budgetLimits?.[index]?.amountKzt, validAmount) === 'over' ? 'Сверх бюджета'
                                : expenseLineBudgetVerdict(budget, budgetLimits?.[index]?.amountKzt, validAmount) === 'within' ? 'В бюджете' : '—'}
                    </span>}</td>
                  </>}
                  {hasAccrual && <>
                  <td className="calculated-cell">{accrual ? <output aria-label={`Сумма НДС, строка ${index + 1}`}>{validAmount ? formatMoney(calculated.vatAmount) : '—'}</output> : '—'}</td>
                  <td className="calculated-cell">{accrual ? <output aria-label={`Сумма без НДС, строка ${index + 1}`}>{validAmount ? formatMoney(calculated.amountExcludingVat) : '—'}</output> : '—'}</td></>}
                  {!viewOnly && <td><button className={`row-action${pendingLineRemoval === index ? ' is-pending' : ''}`} type="button" onClick={() => removeLine(index)} aria-label={pendingLineRemoval === index ? `Подтвердить удаление строки ${index + 1}` : `Удалить строку ${index + 1}`} title={pendingLineRemoval === index ? 'Нажмите ещё раз для удаления строки' : 'Подготовить удаление строки'}><Trash2 size={17} aria-hidden="true"/></button></td>}
                </tr>);
        })}
          </tbody>
          <tfoot><tr><td colSpan={hasAccrual ? 5 : 2}>Итого</td><td className="amount-cell">{invalidAmounts ? 'Проверьте суммы' : `${formatMoney(total)} ${currencyCode}`}</td>{budgetLimits && <td title="Лимиты повторяющихся статей не суммируются">—</td>}{budgetControls && <><td title="Остатки одинаковых бюджетных ключей не суммируются">—</td><td title="Остатки одинаковых бюджетных ключей не суммируются">—</td><td /></>}{hasAccrual && <><td className="amount-cell" title="НДС только по строкам с акрулом">{invalidAmounts ? '—' : formatMoney(accrualVat)}</td><td className="amount-cell" title="Без НДС только по строкам с акрулом">{invalidAmounts ? '—' : formatMoney(accrualNet)}</td></>}{!viewOnly && <td />}</tr></tfoot>
        </table>
      </div>
    </section>);
}
