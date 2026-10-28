export type ExpenseReadinessTarget = {
    tab: 'main' | 'counterparty' | 'lines';
    label: string;
    attachment?: boolean;
};
const fields: Record<string, ExpenseReadinessTarget> = {
    OrganizationId: { tab: 'main', label: 'Организация' },
    DefaultCostCenterId: { tab: 'main', label: 'ЦФО' },
    PlannedPaymentDate: { tab: 'main', label: 'Дата оплаты' },
    'PlannedPaymentDate.BeforeBusinessDate': { tab: 'main', label: 'Дата оплаты' },
    CurrencyCode: { tab: 'main', label: 'Валюта расходной заявки' },
    'CurrencyCode.KZT': { tab: 'main', label: 'Валюта расходной заявки' },
    CurrencyRate: { tab: 'main', label: 'Валюта расходной заявки' },
    BusinessJustification: { tab: 'main', label: 'Назначение платежа' },
    UrgencyReason: { tab: 'main', label: 'Причина срочности' },
    CandidateCounterpartyName: { tab: 'counterparty', label: 'Наименование' },
    CandidateResidencyCode: { tab: 'counterparty', label: 'Резидентность' },
    CandidateBinIin: { tab: 'counterparty', label: 'БИН/ИИН' },
    CandidateCountry: { tab: 'counterparty', label: 'Страна' },
    CandidateForeignRegistrationNumber: { tab: 'counterparty', label: 'Регистрационный номер' },
    CandidateRegistrationNumberMissingExplanation: { tab: 'counterparty', label: 'Пояснение отсутствия номера' },
    PaymentBasisCode: { tab: 'counterparty', label: 'Основание оплаты' },
    ContractIdOrIsNewContract: { tab: 'counterparty', label: 'Договор' },
    InvoiceNumber: { tab: 'counterparty', label: 'Номер счёта' },
    InvoiceDate: { tab: 'counterparty', label: 'Дата счёта' },
    PublicOfferName: { tab: 'counterparty', label: 'Наименование оферты' },
    PublicOfferIdentifier: { tab: 'counterparty', label: 'Ссылка/идентификатор' },
    OtherBasisExplanation: { tab: 'counterparty', label: 'Пояснение' },
    CandidateContractNumberOrMissingFlag: { tab: 'counterparty', label: 'Номер договора' },
    CandidateContractDate: { tab: 'counterparty', label: 'Дата договора' },
    CandidateContractSubject: { tab: 'counterparty', label: 'Предмет договора' },
    SupportingDocumentOrMissingFlag: { tab: 'main', label: 'Добавить файл', attachment: true },
    SupportingDocumentMissingExplanation: { tab: 'main', label: 'Пояснение отсутствия', attachment: true },
    NewContractDocumentOrMissingFlag: { tab: 'main', label: 'Добавить файл', attachment: true },
    NewContractDocumentMissingExplanation: { tab: 'counterparty', label: 'Пояснение отсутствия файла' },
};
export function expenseReadinessTarget(code: string, lineNumber?: number | null): ExpenseReadinessTarget | null {
    if (code === 'PaymentOperation.RegisterDocument')
        return { tab: 'main', label: 'Добавить файл', attachment: true };
    if (code.startsWith('PaymentOperation.'))
        return { tab: 'counterparty', label: 'Реквизиты операции' };
    if (fields[code])
        return fields[code];
    const line = /^Lines\[(\d+)\]\.(CashFlowItemId|CostCenterId|Amount|VatRateCode|CalculatedAmounts|CurrencyConversion)$/u.exec(code);
    const number = line ? Number(line[1]) : lineNumber ?? 1;
    if (!Number.isSafeInteger(number) || number < 1 || number > 1000)
        return null;
    const label = line ? ({
        CashFlowItemId: 'Статья ДДС', CostCenterId: 'Подразделение затрат', Amount: 'Сумма',
        VatRateCode: 'Ставка НДС', CalculatedAmounts: 'Сумма', CurrencyConversion: 'Сумма',
    } as Record<string, string>)[line[2]] : code.startsWith('AccrualSnapshotSource.CashFlowItem') || code === 'Lines' ? 'Статья ДДС'
        : code === 'ExpenseRequestLine.AccrualPeriodMissing' || code === 'ExpenseRequestLine.AccrualPeriodNotMonthStart' ? 'Период начисления' : null;
    return label ? { tab: 'lines', label: `${label}, строка ${number}` } : null;
}
