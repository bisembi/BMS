import { FINANCE_WORKSPACES, type FinanceWorkspaceId } from './financeWorkspaces';
export type SectionId = 'home' | 'budgeting' | 'treasury' | 'management' | 'directories' | 'administration' | 'staff';
/// Read-only reference catalogues that already have a public backend contract.
export type ReferenceDirectoryId = 'organizations' | 'cost-centers' | 'cash-flow-items' | 'expense-items' | 'counterparties' | 'contracts' | 'currencies';
export type ReferenceDirectoryDefinition = {
    id: ReferenceDirectoryId;
    title: string;
    /// Caption of the accessible table.
    caption: string;
    /// Only cost centres and cash-flow items carry an approved public hierarchy.
    showsHierarchy: boolean;
    /// Contracts are readable only inside an explicit organisation/counterparty scope.
    requiresScope: boolean;
};
export const REFERENCE_DIRECTORIES: Record<ReferenceDirectoryId, ReferenceDirectoryDefinition> = {
    organizations: {
        id: 'organizations',
        title: 'Организации',
        caption: 'Справочник организаций',
        showsHierarchy: false,
        requiresScope: false,
    },
    'cost-centers': {
        id: 'cost-centers',
        title: 'Подразделения',
        caption: 'Справочник подразделений',
        showsHierarchy: true,
        requiresScope: false,
    },
    'cash-flow-items': {
        id: 'cash-flow-items',
        title: 'Статьи движения денежных средств',
        caption: 'Справочник статей движения денежных средств',
        showsHierarchy: true,
        requiresScope: false,
    },
    'expense-items': {
        id: 'expense-items',
        title: 'Статьи затрат',
        caption: 'Справочник статей затрат',
        showsHierarchy: false,
        requiresScope: false,
    },
    counterparties: {
        id: 'counterparties',
        title: 'Контрагенты',
        caption: 'Справочник контрагентов',
        showsHierarchy: false,
        requiresScope: false,
    },
    contracts: {
        id: 'contracts',
        title: 'Договоры',
        caption: 'Договоры выбранной организации и контрагента',
        showsHierarchy: false,
        requiresScope: true,
    },
    currencies: {
        id: 'currencies',
        title: 'Валюты',
        caption: 'Справочник активных валют',
        showsHierarchy: false,
        requiresScope: false,
    },
};
export type View = {
    kind: 'staff-substitutions';
    documentId?: string;
} | {
    kind: 'section';
    sectionId: SectionId;
} | {
    kind: 'my-tasks';
} | {
    kind: 'help';
} | {
    kind: 'integration-monitor';
} | {
    kind: 'business-audit-monitor';
} | {
    kind: 'change-journal';
} | {
    kind: 'budget-register';
} | {
    kind: 'exchange-journal';
} | {
    kind: 'session-monitor';
} | {
    kind: 'budget-execution';
} | {
    kind: 'bdds-report';
} | {
    kind: 'bdds-drill';
    workspaceKey: string;
} | {
    kind: 'unbudgeted-articles';
} | {
    kind: 'budget-instances';
} | {
    kind: 'budget-adjustments';
    baseBudgetId?: string;
    adjustmentId?: string;
} | {
    kind: 'company-budget-versions';
} | {
    kind: 'budget-document';
    workspaceKey: string;
    instanceId: string | null;
    copyFromId?: string;
    number?: string;
} | {
    kind: 'budget-directory';
} | {
    kind: 'authorization-directory';
} | {
    kind: 'currency-rates';
} | {
    kind: 'payment-calendar';
} | {
    kind: 'goods-calendar';
} | {
    kind: 'dated-payment-calendar';
} | {
    kind: 'manual-deficits';
} | {
    kind: 'active-accruals';
} | {
    kind: 'opiu-report';
} | {
    kind: 'accrual-notifications';
} | {
    kind: 'approval-matrix';
} | {
    kind: 'expense-list';
    onlyMyTasks?: true;
} | {
    kind: 'expense-payment-register';
} | {
    kind: 'expense-form';
    requestId: string | null;
} | {
    kind: 'reference-directory';
    directoryId: ReferenceDirectoryId;
} | {
    kind: 'budget-owner-cost-center-assignment-directory';
} | {
    kind: 'finance-workspace';
    workspaceId: FinanceWorkspaceId;
} | {
    kind: 'placeholder';
    sectionId: SectionId;
    title: string;
};
export type NavLink = {
    title: string;
    view: View;
    primary?: boolean;
    emphasized?: boolean;
    /// Keeps a navigation alias hidden when its destination is a data-free
    /// section shell but the alias itself promises access to business data.
    businessDataOnly?: true;
};
export type NavGroup = {
    title: string;
    links: NavLink[];
};
export type NavigationAccess = {
    canUseStaffSubstitutions471?: boolean;
    canViewMyTasks?: boolean;
    canCreateExpenseRequest: boolean;
    canAccessBusinessData: boolean;
    canViewIntegrationMonitor: boolean;
    canViewBusinessAuditMonitor: boolean;
    canViewBudgetExecution: boolean;
    canViewAdministration?: boolean;
    canViewAdministrationReports?: boolean;
    canViewExchangeJournal?: boolean;
};
export type NavigationSearchItem = {
    key: string;
    title: string;
    context: string;
    view: View;
};
export type SectionDefinition = {
    id: SectionId;
    label: string;
    lead?: NavLink;
    leadExtra?: NavLink[];
    columns: NavGroup[][];
};
/// Classifies views that expose business data or business workflows. The same
/// exhaustive decision is shared by route mounting and link visibility so a
/// newly added page cannot be protected in one place but advertised in another.
export function requiresBusinessData(view: View): boolean {
    switch (view.kind) {
        case 'my-tasks':
        case 'staff-substitutions':
        case 'expense-form':
        case 'expense-list':
        case 'expense-payment-register':
        case 'reference-directory':
        case 'budget-owner-cost-center-assignment-directory':
        case 'budget-execution':
        case 'budget-register':
        case 'bdds-report':
        case 'bdds-drill':
        case 'unbudgeted-articles':
        case 'budget-instances':
        case 'budget-adjustments':
        case 'company-budget-versions':
        case 'budget-document':
        case 'budget-directory':
        case 'authorization-directory':
        case 'currency-rates':
        case 'payment-calendar':
        case 'goods-calendar':
        case 'dated-payment-calendar':
        case 'manual-deficits':
        case 'active-accruals':
        case 'opiu-report':
        case 'accrual-notifications':
        case 'approval-matrix':
        case 'finance-workspace':
        case 'placeholder':
            return true;
        case 'section':
        case 'help':
        case 'integration-monitor':
        case 'business-audit-monitor':
        case 'change-journal':
        case 'exchange-journal':
        case 'session-monitor':
            return false;
        default: {
            const unreachable: never = view;
            return unreachable;
        }
    }
}
/// Workspace tabs represent concrete lists, reports and documents. Section
/// landing pages live in the sidebar only. Filters never change a page's key.
export function isBookmarkableView(view: View): boolean {
    switch (view.kind) {
        case 'section':
        case 'placeholder':
            return false;
        default:
            return true;
    }
}
const finance = (workspaceId: FinanceWorkspaceId): View => ({ kind: 'finance-workspace', workspaceId });
const directory = (directoryId: ReferenceDirectoryId): View => ({
    kind: 'reference-directory',
    directoryId,
});
const budgetOwnerCostCenterAssignments = (): View => ({
    kind: 'budget-owner-cost-center-assignment-directory',
});
export const SECTIONS: SectionDefinition[] = [
    {
        id: 'home',
        label: 'Главное',
        columns: [[{
                    title: 'Работа с заявками',
                    links: [
                        { title: 'Мои задачи', view: { kind: 'my-tasks' }, primary: true },
                        { title: 'Создать расходную заявку', view: { kind: 'expense-form', requestId: null }, emphasized: true },
                        { title: 'Расходные заявки', view: { kind: 'expense-list' }, primary: true },
                        { title: 'Мои уведомления', view: { kind: 'accrual-notifications' } },
                        { title: 'Сервисный монитор', view: { kind: 'integration-monitor' } },
                        { title: 'Специализированный бизнес-аудит', view: { kind: 'business-audit-monitor' } },
                    ],
                }], [{
                    title: 'Работа с бюджетами',
                    links: [
                        { title: 'Создать экземпляр бюджета', view: { kind: 'budget-document', workspaceKey: 'create', instanceId: null }, emphasized: true },
                        { title: 'Экземпляры бюджетов', view: { kind: 'budget-instances' } },
                        { title: 'БДДС', view: { kind: 'bdds-report' } },
                    ],
                }]],
    },
    {
        id: 'budgeting',
        label: 'Бюджетирование',
        columns: [
            [{
                    title: 'Планирование и контроль бюджета',
                    links: [
                        { title: 'Экземпляры бюджетов', view: { kind: 'budget-instances' }, primary: true },
                        { title: 'Корректировка экземпляра бюджета', view: { kind: 'budget-adjustments' }, primary: true },
                        { title: 'Версии бюджетов', view: { kind: 'company-budget-versions' }, primary: true },
                    ],
                }],
            [{
                    title: 'Отчеты',
                    links: [
                        { title: 'БДДС', view: { kind: 'bdds-report' }, primary: true },
                        { title: 'Незабюджетированные статьи по ЦФО', view: { kind: 'unbudgeted-articles' } },
                        { title: 'Исполнение бюджета', view: { kind: 'budget-execution' } },
                        { title: 'Отчет должников', view: finance('debtors') },
                    ],
                }],
            [{
                    title: 'Создать',
                    links: [
                        { title: 'Экземпляр бюджета', view: { kind: 'budget-document', workspaceKey: 'create', instanceId: null } },
                    ],
                }],
        ],
    },
    {
        id: 'treasury',
        label: 'Казначейство',
        columns: [
            [{
                    title: 'Планирование и контроль денежных средств',
                    links: [
                        { title: 'Расходные заявки', view: { kind: 'expense-list' }, primary: true },
                        { title: 'Календарь по датам', view: { kind: 'dated-payment-calendar' }, primary: true },
                        { title: 'Статьи движения денежных средств', view: directory('cash-flow-items') },
                        { title: 'Приоритеты оплаты', view: finance('payment-priorities') },
                    ],
                }, {
                    title: 'Банк',
                    links: [
                        { title: 'Безналичные платежи', view: finance('cashless-payments'), primary: true },
                    ],
                }],
            [{
                    title: 'Отчеты',
                    links: [
                        { title: 'БДДС', view: { kind: 'bdds-report' }, primary: true },
                        { title: 'Товарный календарь', view: { kind: 'goods-calendar' }, primary: true },
                        { title: 'Платежный календарь', view: { kind: 'payment-calendar' }, primary: true },
                    ],
                }, {
                    title: 'Взаимозачеты и списание задолженности',
                    links: [
                        { title: 'Корректировки дефицита переплаты', view: { kind: 'manual-deficits' }, primary: true },
                        { title: 'Сверки взаиморасчетов', view: finance('reconciliations') },
                        { title: 'Взаимозачеты задолженности', view: finance('debt-offsets') },
                        { title: 'Списания задолженностей', view: finance('debt-writeoffs') },
                    ],
                }],
            [{
                    title: 'Создать',
                    links: [
                        { title: 'Расходная заявка', view: { kind: 'expense-form', requestId: null } },
                        { title: 'Поступление безналичных ДС', view: finance('cashless-receipts') },
                        { title: 'Списание безналичных ДС', view: finance('cashless-withdrawals') },
                    ],
                }],
        ],
    },
    {
        id: 'management',
        label: 'Управленческий учет',
        columns: [
            [{
                    title: 'Управленческие начисления',
                    links: [
                        { title: 'Активные управленческие начисления', view: { kind: 'active-accruals' }, primary: true },
                    ],
                }, {
                    title: 'Отчеты',
                    links: [{ title: 'ОПиУ', view: { kind: 'opiu-report' } }],
                }],
            [{
                    title: 'Справочники',
                    links: [
                        { title: 'Статьи затрат', view: directory('expense-items') },
                    ],
                }],
            [],
        ],
    },
    {
        id: 'directories',
        label: 'Справочники',
        columns: [
            [{
                    title: 'Аналитика бюджета',
                    links: [
                        { title: 'Статьи движения денежных средств', view: directory('cash-flow-items'), primary: true },
                        { title: 'Статьи затрат', view: directory('expense-items') },
                        { title: 'Подразделения', view: directory('cost-centers') },
                        { title: 'Организации', view: directory('organizations') },
                        { title: 'Настройки бюджетов по ЦФО', view: { kind: 'budget-directory' } },
                        { title: 'Сотрудники и их роли', view: { kind: 'authorization-directory' } },
                        { title: 'Матрица согласования', view: { kind: 'approval-matrix' } },
                    ],
                }],
            [{
                    title: 'Основные справочники',
                    links: [
                        { title: 'Контрагенты', view: directory('counterparties') },
                        { title: 'Договоры', view: directory('contracts') },
                        { title: 'Валюты', view: directory('currencies') },
                        { title: 'Курсы валют', view: { kind: 'currency-rates' } },
                    ],
                }],
            [],
        ],
    },
    {
        id: 'staff', label: 'Кадры', columns: [[{ title: 'Кадровые документы', links: [{ title: 'Замещения сотрудников', view: { kind: 'staff-substitutions' }, primary: true }] }]],
    },
    {
        id: 'administration',
        label: 'Администрирование',
        columns: [[{
                    title: 'Журналы',
                    links: [{ title: 'Журнал обмена', view: { kind: 'exchange-journal' } }, { title: 'Журнал изменений', view: { kind: 'change-journal' } }],
                }, { title: 'Мониторинг', links: [{ title: 'Монитор состояний', view: { kind: 'session-monitor' } }] },
                { title: 'Регистры', links: [{ title: 'Исполнение бюджета', view: { kind: 'budget-register' } }] }]],
    },
];
/// Applies the complete session capability contract to one view. Both the
/// navigation inventory and App render gate call this function.
export function isViewAvailable(view: View, access: NavigationAccess): boolean {
    if (view.kind === 'staff-substitutions' || view.kind === 'section' && view.sectionId === 'staff')
        return access.canAccessBusinessData && access.canUseStaffSubstitutions471 === true;
    if (view.kind === 'my-tasks')
        return access.canAccessBusinessData && access.canViewMyTasks !== false;
    const canReadAdministrationReports = access.canAccessBusinessData
        && (access.canViewAdministration === true || access.canViewAdministrationReports === true);
    if (view.kind === 'budget-register')
        return canReadAdministrationReports;
    if (view.kind === 'exchange-journal')
        return access.canViewExchangeJournal === true && access.canAccessBusinessData;
    if (view.kind === 'section' && view.sectionId === 'administration')
        return canReadAdministrationReports || (access.canAccessBusinessData && access.canViewExchangeJournal === true);
    if (view.kind === 'change-journal' || view.kind === 'session-monitor')
        return canReadAdministrationReports;
    if (requiresBusinessData(view) && !access.canAccessBusinessData)
        return false;
    if (view.kind === 'integration-monitor')
        return access.canViewIntegrationMonitor;
    if (view.kind === 'business-audit-monitor')
        return access.canViewBusinessAuditMonitor;
    if (view.kind === 'budget-execution')
        return access.canViewBudgetExecution;
    if (view.kind === 'bdds-report')
        return access.canAccessBusinessData;
    if (view.kind === 'unbudgeted-articles')
        return access.canAccessBusinessData;
    // The page resolves budget membership through its bounded server APIs.
    // Expense-request creation is an independent authority.
    if (view.kind === 'budget-instances' || view.kind === 'budget-document')
        return access.canAccessBusinessData;
    if (view.kind === 'company-budget-versions')
        return access.canAccessBusinessData;
    if (view.kind === 'budget-adjustments')
        return access.canAccessBusinessData;
    return access.canCreateExpenseRequest
        || view.kind !== 'expense-form'
        || view.requestId !== null;
}
export function isNavLinkAvailable(link: NavLink, access: NavigationAccess): boolean {
    if (link.businessDataOnly === true && !access.canAccessBusinessData)
        return false;
    return isViewAvailable(link.view, access);
}
/// Returns only implemented, session-authorized navigation targets. Placeholder
/// pages are deliberately absent: header search must not advertise unfinished
/// product areas or masquerade as a business-data search.
export function navigationSearchItems(access: NavigationAccess): NavigationSearchItem[] {
    const items = new Map<string, NavigationSearchItem>();
    const add = (title: string, context: string, view: View) => {
        const key = keyForView(view);
        if (!items.has(key))
            items.set(key, { key, title, context, view });
    };
    for (const section of SECTIONS) {
        if (!isViewAvailable({ kind: 'section', sectionId: section.id }, access))
            continue;
        add(section.label, 'Раздел BMS', { kind: 'section', sectionId: section.id });
        const addLink = (link: NavLink, context: string) => {
            if (link.view.kind === 'placeholder'
                || link.view.kind === 'section'
                || !isNavLinkAvailable(link, access))
                return;
            add(link.title, context, link.view);
        };
        // Concrete links name their destination more precisely than a section lead
        // such as "Reports". Add them first so aliases that open the same view do
        // not replace the searchable business-page title.
        section.columns.forEach((column) => column.forEach((group) => {
            group.links.forEach((link) => addLink(link, `${section.label} · ${group.title}`));
        }));
        if (section.lead)
            addLink(section.lead, section.label);
        section.leadExtra?.forEach((link) => addLink(link, section.label));
    }
    add('Помощь', 'Вопросы и ответы о BMS', { kind: 'help' });
    return [...items.values()];
}
export function sectionForView(view: View): SectionId {
    if (view.kind === 'staff-substitutions')
        return 'staff';
    if (view.kind === 'my-tasks')
        return 'home';
    if (view.kind === 'budget-register')
        return 'administration';
    if (view.kind === 'help')
        return 'home';
    if (view.kind === 'change-journal')
        return 'administration';
    if (view.kind === 'exchange-journal')
        return 'administration';
    if (view.kind === 'session-monitor')
        return 'administration';
    if (view.kind === 'finance-workspace')
        return FINANCE_WORKSPACES[view.workspaceId].section;
    if (view.kind === 'accrual-notifications')
        return 'home';
    if (view.kind === 'integration-monitor')
        return 'home';
    if (view.kind === 'business-audit-monitor')
        return 'home';
    if (view.kind === 'budget-execution')
        return 'budgeting';
    if (view.kind === 'bdds-report')
        return 'budgeting';
    if (view.kind === 'bdds-drill')
        return 'budgeting';
    if (view.kind === 'unbudgeted-articles')
        return 'budgeting';
    if (view.kind === 'budget-instances')
        return 'budgeting';
    if (view.kind === 'budget-adjustments')
        return 'budgeting';
    if (view.kind === 'company-budget-versions')
        return 'budgeting';
    if (view.kind === 'budget-document')
        return 'budgeting';
    if (view.kind === 'budget-directory')
        return 'directories';
    if (view.kind === 'authorization-directory')
        return 'directories';
    if (view.kind === 'currency-rates')
        return 'directories';
    if (view.kind === 'approval-matrix')
        return 'directories';
    if (view.kind === 'active-accruals')
        return 'management';
    if (view.kind === 'opiu-report')
        return 'management';
    if (view.kind === 'reference-directory')
        return 'directories';
    if (view.kind === 'budget-owner-cost-center-assignment-directory')
        return 'directories';
    if (view.kind === 'section' || view.kind === 'placeholder')
        return view.sectionId;
    return 'treasury';
}
export function titleForView(view: View): string {
    if (view.kind === 'staff-substitutions')
        return view.documentId ? 'Замещение сотрудника' : 'Замещения сотрудников';
    if (view.kind === 'my-tasks')
        return 'Мои задачи';
    if (view.kind === 'budget-register')
        return 'Исполнение бюджета · регистр';
    if (view.kind === 'expense-payment-register')
        return 'Реестр к оплате';
    if (view.kind === 'help')
        return 'Помощь';
    if (view.kind === 'change-journal')
        return 'Журнал изменений';
    if (view.kind === 'exchange-journal')
        return 'Журнал обмена';
    if (view.kind === 'session-monitor')
        return 'Монитор состояний';
    if (view.kind === 'finance-workspace')
        return FINANCE_WORKSPACES[view.workspaceId].title;
    if (view.kind === 'accrual-notifications')
        return 'Мои уведомления';
    if (view.kind === 'integration-monitor')
        return 'Сервисный монитор';
    if (view.kind === 'business-audit-monitor')
        return 'Специализированный бизнес-аудит';
    if (view.kind === 'budget-execution')
        return 'Исполнение бюджета';
    if (view.kind === 'bdds-report')
        return 'БДДС';
    if (view.kind === 'bdds-drill')
        return 'Расшифровка';
    if (view.kind === 'unbudgeted-articles')
        return 'Незабюджетированные статьи по ЦФО';
    if (view.kind === 'budget-instances')
        return 'Экземпляры бюджетов';
    if (view.kind === 'budget-adjustments')
        return 'Корректировки экземпляров бюджетов';
    if (view.kind === 'company-budget-versions')
        return 'Версии бюджетов';
    if (view.kind === 'budget-document')
        return view.instanceId !== null
            ? `Бюджет ${view.number ?? '— загрузка'}`
            : view.copyFromId ? `Копия бюджета ${view.number ?? ''}`.trim() : 'Новый экземпляр бюджета';
    if (view.kind === 'budget-directory')
        return 'Настройки бюджетов по ЦФО';
    if (view.kind === 'authorization-directory')
        return 'Сотрудники и их роли';
    if (view.kind === 'currency-rates')
        return 'Курсы валют';
    if (view.kind === 'payment-calendar')
        return 'Платежный календарь';
    if (view.kind === 'goods-calendar')
        return 'Товарный календарь';
    if (view.kind === 'dated-payment-calendar')
        return 'Календарь по датам';
    if (view.kind === 'manual-deficits')
        return 'Корректировки дефицита переплаты';
    if (view.kind === 'approval-matrix')
        return 'Матрица согласования';
    if (view.kind === 'active-accruals')
        return 'Активные управленческие начисления';
    if (view.kind === 'opiu-report')
        return 'ОПиУ';
    if (view.kind === 'expense-list') {
        return 'Расходные заявки';
    }
    if (view.kind === 'expense-form')
        return view.requestId ? 'Расходная заявка' : 'Расходная заявка (создание)';
    if (view.kind === 'reference-directory')
        return REFERENCE_DIRECTORIES[view.directoryId].title;
    if (view.kind === 'budget-owner-cost-center-assignment-directory')
        return 'Сотрудники и их роли';
    if (view.kind === 'placeholder')
        return view.title;
    return SECTIONS.find((section) => section.id === view.sectionId)?.label ?? 'BMS';
}
export function keyForView(view: View): string {
    if (view.kind === 'staff-substitutions')
        return 'staff-substitutions:' + (view.documentId ?? 'all');
    if (view.kind === 'my-tasks')
        return view.kind;
    if (view.kind === 'budget-register')
        return view.kind;
    if (view.kind === 'expense-payment-register')
        return view.kind;
    if (view.kind === 'help')
        return view.kind;
    if (view.kind === 'change-journal')
        return view.kind;
    if (view.kind === 'exchange-journal')
        return view.kind;
    if (view.kind === 'session-monitor')
        return view.kind;
    if (view.kind === 'finance-workspace')
        return `finance-workspace:${view.workspaceId}`;
    if (view.kind === 'accrual-notifications')
        return view.kind;
    if (view.kind === 'integration-monitor')
        return view.kind;
    if (view.kind === 'business-audit-monitor')
        return view.kind;
    if (view.kind === 'budget-execution')
        return view.kind;
    if (view.kind === 'bdds-report')
        return view.kind;
    if (view.kind === 'bdds-drill')
        return `bdds-drill:${view.workspaceKey}`;
    if (view.kind === 'unbudgeted-articles')
        return view.kind;
    if (view.kind === 'budget-instances')
        return view.kind;
    if (view.kind === 'budget-adjustments')
        return view.kind + ':' + (view.adjustmentId ?? view.baseBudgetId ?? 'all');
    if (view.kind === 'company-budget-versions')
        return view.kind;
    if (view.kind === 'budget-document')
        return `budget-document:${view.workspaceKey}`;
    if (view.kind === 'budget-directory')
        return view.kind;
    if (view.kind === 'authorization-directory')
        return view.kind;
    if (view.kind === 'currency-rates')
        return view.kind;
    if (view.kind === 'payment-calendar')
        return view.kind;
    if (view.kind === 'goods-calendar')
        return view.kind;
    if (view.kind === 'dated-payment-calendar')
        return view.kind;
    if (view.kind === 'manual-deficits')
        return view.kind;
    if (view.kind === 'active-accruals')
        return view.kind;
    if (view.kind === 'opiu-report')
        return view.kind;
    if (view.kind === 'approval-matrix')
        return view.kind;
    if (view.kind === 'expense-list')
        return 'expense-list:all';
    if (view.kind === 'expense-form')
        return `expense-form:${view.requestId ?? 'new'}`;
    if (view.kind === 'reference-directory')
        return `reference-directory:${view.directoryId}`;
    if (view.kind === 'budget-owner-cost-center-assignment-directory')
        return 'authorization-directory';
    if (view.kind === 'section')
        return `section:${view.sectionId}`;
    if (view.kind === 'placeholder')
        return `placeholder:${view.sectionId}:${view.title}`;
    const unreachable: never = view;
    return unreachable;
}
