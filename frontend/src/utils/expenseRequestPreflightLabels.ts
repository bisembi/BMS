import type { SubmissionPreflight, SubmissionPreflightBlocker } from '../types';
/// Fixed, safe labels for readiness and preflight codes. Server text is never
/// rendered through these helpers.
const readinessLabels: Record<string, string> = {
    'PaymentOperation.AdvanceAmount': 'Сумма аванса',
    'PaymentOperation.AdvanceTotalMismatch': 'Сумма аванса не совпадает с суммой заявки',
    'PaymentOperation.BudgetCostCenterId': 'Не настроен бюджет авансов',
    'PaymentOperation.PaymentCode': 'КНП',
    'PaymentOperation.PaymentKind': 'Вид платежа',
    'PaymentOperation.Period': 'Период',
    'PaymentOperation.TaxCurrencyKzt': 'Для налогов и социальных платежей требуется KZT',
    'PaymentOperation.TaxAuthorityName': 'Наименование УГД',
    'PaymentOperation.TaxAuthorityCode': 'Код УГД',
    'PaymentOperation.BudgetCode': 'КБК',
    'PaymentOperation.RegisterNumber': 'Номер ведомости / реестра',
    'PaymentOperation.RegisterDate': 'Дата ведомости / реестра',
    'PaymentOperation.RegisterDocument': 'Приложите ведомость / реестр',
    'PaymentOperation.RegisterAmount': 'Итог ведомости / реестра',
    'PaymentOperation.RegisterTotalMismatch': 'Итог ведомости / реестра не совпадает с суммой заявки',
    'PaymentOperation.PayrollMode': 'Оформление зарплаты',
    'PaymentOperation.EmployeeName': 'ФИО сотрудника',
    'PaymentOperation.EmployeeNames': 'Заполните ФИО каждого сотрудника',
    'PaymentOperation.EmployeeIin': 'ИИН сотрудника',
    'PaymentOperation.AdvancePurpose': 'Цель подотчёта',
    'PaymentOperation.AdvanceReportDueDate': 'Срок авансового отчёта не ранее даты оплаты',
    'PaymentOperation.TransferMethod': 'Способ выплаты',
    'PaymentOperation.RecipientName': 'Получатель',
    'PaymentOperation.RecipientBinIin': 'БИН / ИИН получателя',
    'PaymentOperation.RecipientIban': 'IBAN получателя',
    'PaymentOperation.RecipientBik': 'БИК банка',
    'PaymentOperation.RecipientKbe': 'КБе',
    'PaymentOperation.WithoutVat': 'Для операции требуется «Без НДС»',
    InitiatorExternalId: 'Инициатор',
    OrganizationId: 'Организация',
    DefaultCostCenterId: 'ЦФО',
    PlannedPaymentDate: 'Планируемая дата оплаты',
    'CurrencyCode.KZT': 'Валюта KZT',
    CurrencyCode: 'Валюта',
    CurrencyRate: 'Не удалось подтвердить валютный расчёт. Проверьте наличие курса в справочнике «Курсы валют» и повторите автоматический пересчёт.',
    'BudgetReservation.PositionFacts': 'Сохранённые данные бюджетной позиции неполны',
    BusinessJustification: 'Назначение платежа',
    UrgencyReason: 'Причина срочности',
    Lines: 'Строки расходов',
    CounterpartyRequirementUnresolved: 'Требование к контрагенту требует утверждения бизнес-матрицы',
    CandidateCounterpartyName: 'Наименование нового контрагента',
    CandidateResidencyCode: 'Резидентность нового контрагента',
    CandidateBinIin: 'БИН/ИИН нового контрагента',
    CandidateCountry: 'Страна нового контрагента',
    CandidateForeignRegistrationNumber: 'Иностранный регистрационный номер',
    CandidateRegistrationNumberMissingExplanation: 'Пояснение отсутствия регистрационного номера',
    'CandidateRegistrationNumberMissing.NonResidentOnly': 'Признак отсутствия номера допустим только нерезиденту',
    PaymentBasisCode: 'Основание оплаты',
    ContractIdOrIsNewContract: 'Существующий или новый договор',
    InvoiceNumber: 'Номер счёта',
    InvoiceDate: 'Дата счёта',
    PublicOfferName: 'Наименование публичной оферты',
    PublicOfferIdentifier: 'Ссылка или идентификатор оферты',
    OtherBasisExplanation: 'Пояснение иного основания',
    'IsNewContract.ContractBasisOnly': 'Новый договор допустим только для основания «Договор»',
    CandidateContractNumberOrMissingFlag: 'Номер нового договора или признак «Без номера»',
    CandidateContractDate: 'Дата нового договора',
    CandidateContractSubject: 'Предмет нового договора',
    'CandidateContractNumberMissing.NewContractOnly': 'Признак «Без номера» допустим только новому договору',
    SupportingDocumentOrMissingFlag: 'Подтверждающий файл или причина его отсутствия',
    SupportingDocumentMissingExplanation: 'Пояснение отсутствия подтверждающего документа',
    'SupportingDocumentMissing.FilePresent': 'Нельзя одновременно отметить отсутствие подтверждающего документа и иметь файл',
    NewContractDocumentOrMissingFlag: 'Файл нового договора или причина его отсутствия',
    NewContractDocumentMissingExplanation: 'Пояснение отсутствия файла договора',
    'NewContractDocumentMissing.NewContractOnly': 'Признак отсутствия файла допустим только новому договору',
    'NewContractDocumentMissing.FilePresent': 'Нельзя одновременно отметить отсутствие файла договора и иметь файл',
};
export function readinessLabel(code: string): string {
    const lineRequirement = /^Lines\[(\d+)]\.(CurrencyConversion|CashFlowItemId|CostCenterId|Amount|VatRateCode|CalculatedAmounts)$/.exec(code);
    if (lineRequirement) {
        const fieldLabels: Record<string, string> = {
            CurrencyConversion: 'пересчёт суммы в тенге',
            CashFlowItemId: 'статья ДДС',
            CostCenterId: 'ЦФО',
            Amount: 'сумма',
            VatRateCode: 'ставка НДС',
            CalculatedAmounts: 'расчётные суммы',
        };
        return `Строка ${lineRequirement[1]}: ${fieldLabels[lineRequirement[2]]}`;
    }
    return readinessLabels[code] ?? 'Неизвестное требование подачи. Обратитесь к администратору.';
}
const preflightBlockerLabels: Record<string, string> = {
    'Command.AdministratorReadOnly': 'Администратор может только просматривать заявку и не выполняет бизнес-команды.',
    'Command.BudgetOwnerRequired': 'Для подачи нужно действующее право создания РЗ по выбранному ЦФО и право действовать от имени автора.',
    'PlannedPaymentDate.BeforeBusinessDate': 'Планируемая дата оплаты раньше текущей бизнес-даты.',
    'BudgetProjection.Incomplete': 'Для сохранённой заявки невозможно сформировать полный бюджетный расчёт.',
    'BudgetLimit.Unavailable': 'Для бюджетной позиции не настроен доступный лимит.',
    'BudgetProjection.Drift': 'Сохранённый бюджетный расчёт расходится с актуальными данными заявки.',
    'BudgetSnapshot.Unavailable': 'Актуальный снимок бюджета временно недоступен.',
    'ApprovalRoute.DeficitScaleUnsupported': 'Точность суммы дефицита не поддерживается маршрутом согласования.',
    'WorkflowDefinition.Unavailable': 'Рабочий процесс согласования не настроен.',
    'WorkflowSubmitCommand.Unavailable': 'Команда запуска согласования ещё не подключена.',
    'ApprovalRoleAssignments.Unavailable': 'Не настроены назначения обязательных ролей согласования.',
    'AttachmentFacts.Unavailable': 'Сервер пока не может подтвердить факты вложений.',
    'AccrualSnapshotSource.Unavailable': 'Данные для проверки начислений временно недоступны.',
    'AccrualSnapshotSource.CashFlowItemMissing': 'Выбранная статья ДДС отсутствует в актуальном справочнике.',
    'AccrualSnapshotSource.CashFlowItemInactive': 'Выбранная статья ДДС неактивна.',
    'AccrualSnapshotSource.CashFlowItemGroup': 'Выбранная статья ДДС является группой и недоступна для заявки.',
    'ExpenseRequestLine.AccrualPeriodMissing': 'Не указан период начисления.',
    'ExpenseRequestLine.AccrualPeriodNotMonthStart': 'Период начисления должен начинаться с первого дня месяца.',
    'ForeignCurrency.SubmissionUnsupported': 'Подача заявки в иностранной валюте пока не поддерживается.',
    'LineOrder.InvariantViolation': 'Нарушена сохранённая последовательность строк расходов.',
};
export const preflightRoleLabels: Record<string, string> = {
    PAYROLL_CALCULATOR: 'Расчетчик зарплаты',
    FIN_CONTROLLER: 'Финансовый контролёр',
    BUDGET_OWNER: 'Владелец ЦФО',
    FIN_DIRECTOR: 'Финансовый директор',
    GENERAL_DIRECTOR: 'Генеральный директор',
};
const dataPreflightBlockerCodes = new Set([
    'PlannedPaymentDate.BeforeBusinessDate',
    'ForeignCurrency.SubmissionUnsupported',
    'AccrualSnapshotSource.CashFlowItemMissing',
    'AccrualSnapshotSource.CashFlowItemInactive',
    'AccrualSnapshotSource.CashFlowItemGroup',
    'ExpenseRequestLine.AccrualPeriodMissing',
    'ExpenseRequestLine.AccrualPeriodNotMonthStart',
]);
export function preflightBlockerLabel(blocker: SubmissionPreflightBlocker): string {
    const label = preflightBlockerLabels[blocker.code];
    if (!label) {
        return 'Неизвестная системная проверка. Обратитесь к администратору.';
    }
    return blocker.lineNumber !== null && blocker.lineNumber > 0
        ? `Строка ${blocker.lineNumber}: ${label}`
        : label;
}
export function isDataPreflightBlocker(blocker: SubmissionPreflightBlocker): boolean {
    return blocker.category.toLocaleLowerCase('en-US') === 'data'
        || dataPreflightBlockerCodes.has(blocker.code);
}
export function uniquePreflightBlockers(preflight: SubmissionPreflight): SubmissionPreflightBlocker[] {
    const unique = new Map<string, SubmissionPreflightBlocker>();
    const budgetBlockers = preflight.budgetReadiness.blockers.map((code) => ({
        category: 'budget',
        code,
        lineNumber: null,
    }));
    for (const blocker of [...preflight.blockers, ...budgetBlockers]) {
        const key = `${blocker.category}:${blocker.code}:${blocker.lineNumber ?? ''}`;
        if (!unique.has(key))
            unique.set(key, blocker);
    }
    return [...unique.values()];
}
