import type { ExpenseRequestStatus } from './types';
export type FinanceWorkspaceId = 'debtors' | 'payment-priorities' | 'cashless-payments' | 'reconciliations' | 'debt-offsets' | 'debt-writeoffs' | 'cashless-receipts' | 'cashless-withdrawals';
export type FinanceWorkspaceDefinition = {
    title: string;
    section: 'budgeting' | 'treasury';
    description: string;
    sourceLabel: string;
    boundary: string;
    initialStatus?: ExpenseRequestStatus;
    businessColumns: boolean;
    draft?: 'reconciliation' | 'offset' | 'writeoff' | 'receipt' | 'withdrawal';
};
// These are frontend workspaces over existing reads, not newly invented bank
// APIs or permissions. A local preparation document is never a posted command.
export const FINANCE_WORKSPACES: Record<FinanceWorkspaceId, FinanceWorkspaceDefinition> = {
    debtors: {
        title: 'Отчет должников', section: 'budgeting',
        description: 'Оплаченные заявки, по которым ещё требуется закрытие.',
        sourceLabel: 'Текущие оплаченные заявки', initialStatus: 'PAID', businessColumns: true,
        boundary: 'Показано текущее состояние заявок PAID, а не исторический срез. Датированный отчёт, активный акрул и последнее событие требуют отдельного серверного отчёта.',
    },
    'payment-priorities': {
        title: 'Приоритеты оплаты', section: 'treasury',
        description: 'Отбор заявок к оплате по дате, организации, ЦФО и срочности.',
        sourceLabel: 'Очередь расходных заявок', initialStatus: 'TO_PAY', businessColumns: false,
        boundary: 'Срочность берётся из заявки. Баллы, автоматический ранг и ручная перестановка не назначаются: правила приоритизации ещё не утверждены.',
    },
    'cashless-payments': {
        title: 'Безналичные платежи', section: 'treasury',
        description: 'Подтверждённые денежные платежи и сторно по выбранной расходной заявке.',
        sourceLabel: 'Заявки для просмотра платежей', businessColumns: false,
        boundary: 'Платежи загружаются отдельно по выбранной заявке и с проверкой прав на сервере. Это не глобальная банковская выписка.',
    },
    reconciliations: {
        title: 'Сверки взаиморасчетов', section: 'treasury', draft: 'reconciliation',
        description: 'Подготовка сверки: период, контрагент, основание и данные выбранной заявки.',
        sourceLabel: 'Заявки-основания', businessColumns: false,
        boundary: 'Акт сверки, встречные данные контрагента и его подтверждение к API не подключены. Черновик ниже не изменяет взаиморасчёты.',
    },
    'debt-offsets': {
        title: 'Взаимозачеты задолженности', section: 'treasury', draft: 'offset',
        description: 'Подготовка оснований и суммы предполагаемого взаимозачёта.',
        sourceLabel: 'Заявки-основания', businessColumns: false,
        boundary: 'Выбор основания не подтверждает право на взаимозачёт. Проведение, проверка встречных обязательств и согласование выполняются только будущей серверной командой.',
    },
    'debt-writeoffs': {
        title: 'Списания задолженностей', section: 'treasury', draft: 'writeoff',
        description: 'Подготовка суммы, основания и пояснения к списанию задолженности.',
        sourceLabel: 'Заявки-основания', businessColumns: false,
        boundary: 'Основания, лимиты и полномочия списания ещё требуют согласования. Проверка формы не является разрешением списать задолженность.',
    },
    'cashless-receipts': {
        title: 'Поступление безналичных ДС', section: 'treasury', draft: 'receipt',
        description: 'Подготовка реквизитов входящего банковского документа.',
        sourceLabel: 'Связанные расходные заявки', businessColumns: false,
        boundary: 'Банковский приём не подключён. Ввод суммы или номера источника не создаёт факт оплаты, поступление либо движение бюджета.',
    },
    'cashless-withdrawals': {
        title: 'Списание безналичных ДС', section: 'treasury', draft: 'withdrawal',
        description: 'Подготовка реквизитов исходящего банковского документа.',
        sourceLabel: 'Заявки-основания', businessColumns: false,
        boundary: 'Отправка в банк и проведение не подключены. Этот экран не меняет статус заявки, резерв или подтверждённую оплату.',
    },
};
