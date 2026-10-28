import type { ExpenseRequestDraft } from '../types';
export type ExpensePaymentOperation = {
    kind: 'Tax' | 'Payroll' | 'Accountable' | 'PayrollAdvance' | 'BusinessTrip';
    employeeNames?: readonly string[] | null;
    advanceAmount?: number | null;
    budgetCostCenterId?: string | null;
    paymentKind?: 'BudgetTax' | 'VAT' | 'CIT' | 'OPV' | 'OPVR' | 'SO' | 'OOSMS' | 'VOSMS' | null;
    period?: string | null;
    taxAuthorityName?: string | null;
    taxAuthorityCode?: string | null;
    budgetCode?: string | null;
    paymentCode?: string | null;
    recipientName?: string | null;
    recipientBinIin?: string | null;
    recipientIban?: string | null;
    recipientBik?: string | null;
    recipientKbe?: string | null;
    payrollMode?: 'Register' | 'Employee' | null;
    registerNumber?: string | null;
    registerDate?: string | null;
    registerAmount?: number | null;
    employeeId?: string | null;
    employeeName?: string | null;
    employeeIin?: string | null;
    advancePurpose?: string | null;
    advanceReportDueDate?: string | null;
    transferMethod?: 'Bank' | 'Cash' | null;
    isPenalty?: boolean;
};
export const operationNames = { Supplier: 'Оплата поставщику', Tax: 'Оплата налогов и социальных платежей', Payroll: 'Оплата зарплаты (ранее)', PayrollAdvance: 'Выплата аванса', Accountable: 'Выдача подотчёт', BusinessTrip: 'Командировочные расходы' };
export const maximumBusinessTripEmployees = 40;
export const taxNames = { BudgetTax: 'Налог в бюджет', VAT: 'НДС', CIT: 'КПН', OPV: 'ОПВ', OPVR: 'ОПВР', SO: 'Социальные отчисления (СО)', OOSMS: 'ОСМС — отчисления работодателя', VOSMS: 'ОСМС — взносы работника' };
export function isBudgetTaxKind(kind: ExpensePaymentOperation['paymentKind']): boolean { return kind === 'BudgetTax' || kind === 'VAT' || kind === 'CIT'; }
export const socialPaymentCodes: Record<string, readonly [
    string,
    string
]> = { OPV: ['010', '019'], OPVR: ['089', '098'], SO: ['012', '017'], OOSMS: ['121', '123'], VOSMS: ['122', '124'] };
export const operationFieldNames: Record<keyof ExpensePaymentOperation, string> = {
    employeeNames: 'ФИО сотрудников',
    advanceAmount: 'Сумма аванса', budgetCostCenterId: 'Бюджетный ЦФО',
    kind: 'Операция', paymentKind: 'Вид платежа', period: 'Период', taxAuthorityName: 'Наименование УГД', taxAuthorityCode: 'Код УГД', budgetCode: 'КБК', paymentCode: 'КНП',
    recipientName: 'Получатель', recipientBinIin: 'БИН / ИИН получателя', recipientIban: 'Счёт получателя (IBAN)', recipientBik: 'БИК банка', recipientKbe: 'КБе',
    payrollMode: 'Способ оформления зарплаты', registerNumber: 'Номер ведомости / реестра', registerDate: 'Дата ведомости / реестра', registerAmount: 'Итого по ведомости / реестру',
    employeeId: 'Сотрудник справочника', employeeName: 'ФИО сотрудника', employeeIin: 'ИИН сотрудника', advancePurpose: 'Цель подотчёта', advanceReportDueDate: 'Срок авансового отчёта', transferMethod: 'Способ выплаты', isPenalty: 'Пеня',
};
export function operationDisplay(key: keyof ExpensePaymentOperation, value: unknown): string {
    if (value === null || value === undefined || value === '')
        return 'Не указано';
    if (key === 'kind')
        return operationNames[value as keyof typeof operationNames] ?? String(value);
    if (key === 'paymentKind')
        return taxNames[value as keyof typeof taxNames] ?? String(value);
    if (key === 'payrollMode')
        return value === 'Register' ? 'По ведомости' : 'Отдельному сотруднику';
    if (key === 'transferMethod')
        return value === 'Cash' ? 'Наличными' : 'Банковский перевод';
    if (key === 'employeeNames' && Array.isArray(value))
        return value.length ? value.join('; ') : 'Не указано';
    if (typeof value === 'boolean')
        return value ? 'Да' : 'Нет';
    return String(value);
}
const textKeys = Object.keys(operationFieldNames).filter(k => !['kind', 'registerAmount', 'advanceAmount', 'isPenalty', 'employeeNames'].includes(k));
export function isPaymentOperation(value: unknown): value is ExpensePaymentOperation | null | undefined {
    if (value == null)
        return true;
    if (typeof value !== 'object' || Array.isArray(value))
        return false;
    const v = value as Record<string, unknown>;
    return ['Tax', 'Payroll', 'Accountable', 'PayrollAdvance', 'BusinessTrip'].includes(String(v.kind))
        && Object.keys(v).every(k => k in operationFieldNames)
        && textKeys.every(k => v[k] == null || (typeof v[k] === 'string' && v[k].length <= 1000 && !/[\u0000-\u001f\u007f-\u009f]/u.test(v[k])))
        && (v.paymentKind == null || (typeof v.paymentKind === 'string' && v.paymentKind in taxNames))
        && (v.payrollMode == null || ['Register', 'Employee'].includes(String(v.payrollMode)))
        && (v.transferMethod == null || ['Bank', 'Cash'].includes(String(v.transferMethod)))
        && (v.isPenalty === undefined || typeof v.isPenalty === 'boolean')
        && (v.employeeNames == null || (v.kind === 'BusinessTrip' && Array.isArray(v.employeeNames)
            && v.employeeNames.length <= maximumBusinessTripEmployees && v.employeeNames.every(name => typeof name === 'string'
            && name.length <= 150 && !/[\u0000-\u001f\u007f-\u009f]/u.test(name))))
        && (v.kind !== 'BusinessTrip' || Object.entries(v).every(([key, field]) => key === 'kind' || key === 'employeeNames'
            || field == null || (key === 'isPenalty' && field === false)))
        && (v.registerAmount == null || (typeof v.registerAmount === 'number' && Number.isFinite(v.registerAmount) && v.registerAmount > 0))
        && (v.advanceAmount == null || (typeof v.advanceAmount === 'number' && Number.isFinite(v.advanceAmount) && v.advanceAmount >= 0));
}
/** Changing scenario never silently carries hidden supplier/person/bank data. Gross sums and budget keys stay unchanged. */
export function switchOperation(d: ExpenseRequestDraft, kind: keyof typeof operationNames): Partial<ExpenseRequestDraft> {
    const patch: Partial<ExpenseRequestDraft> = { paymentOperation: kind === 'Supplier' ? null : kind === 'BusinessTrip' ? { kind, employeeNames: [''] } : kind === 'Tax' ? { kind, paymentKind: 'BudgetTax', paymentCode: '911' } : { kind, transferMethod: 'Bank', ...(kind === 'Payroll' ? { payrollMode: 'Register' as const } : {}) },
        counterpartyId: '', contractId: '', isNewCounterparty: false, isNewContract: false, paymentBasisCode: '', invoiceNumber: '', invoiceDate: '', publicOfferName: '', publicOfferIdentifier: '', otherBasisExplanation: '',
        candidateCounterpartyName: '', candidateResidencyCode: '', candidateBinIin: '', candidateCountry: '', candidateForeignRegistrationNumber: '', candidateRegistrationNumberMissing: false, candidateRegistrationNumberMissingExplanation: '',
        candidateContractNumber: '', candidateContractNumberMissing: false, candidateContractDate: '', candidateContractSubject: '', candidateContractTerms: '', newContractDocumentMissing: false, newContractDocumentMissingExplanation: '' };
    if (kind === 'PayrollAdvance')
        patch.lines = [{ ...d.lines[0], cashFlowItemId: '', costCenterId: d.defaultCostCenterId, amount: '', vatRateCode: 'WithoutVat' }];
    else if (d.paymentOperation?.kind === 'PayrollAdvance')
        patch.lines = d.lines.map(l => ({ ...l, cashFlowItemId: '' }));
    else if (kind !== 'Supplier')
        patch.lines = d.lines.map(l => ({ ...l, vatRateCode: 'WithoutVat' }));
    return patch;
}
