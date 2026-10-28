import type { ExpenseRequestStatus, ExpenseRequestCurrentStage } from './types';
export const expenseRequestStatusCodes = [
    'DRAFT',
    'RETURNED',
    'IN_APPROVAL',
    'APPROVED',
    'AWAITING_NSI',
    'TO_PAY',
    'PARTIALLY_PAID',
    'PAID',
    'CLOSED',
    'REJECTED',
    'CANCELLED',
] as const satisfies readonly ExpenseRequestStatus[];
const labels: Record<ExpenseRequestStatus, string> = {
    DRAFT: 'Черновик',
    RETURNED: 'На доработке',
    IN_APPROVAL: 'На согласовании',
    APPROVED: 'У казначея',
    AWAITING_NSI: 'У специалиста НСИ',
    TO_PAY: 'В оплате',
    PARTIALLY_PAID: 'Частично оплачено',
    PAID: 'Оплачено',
    CLOSED: 'Закрыта',
    REJECTED: 'Отклонена',
    CANCELLED: 'Отменена',
};
export function isExpenseRequestStatus(value: unknown): value is ExpenseRequestStatus {
    return typeof value === 'string'
        && (expenseRequestStatusCodes as readonly string[]).includes(value);
}
const stageLabels: Record<ExpenseRequestCurrentStage, string> = {
    'Расчетчик зарплаты': 'У расчетчика зарплаты',
    'Владелец ЦФО': 'У владельца ЦФО', 'Финансовый контролёр': 'У финансового контролера',
    'Финансовый директор': 'У финансового директора', 'Генеральный директор': 'У генерального директора',
    'Специалист НСИ': 'У специалиста НСИ', 'Казначей': 'У казначея',
};
const roleStages: Record<string, ExpenseRequestCurrentStage> = {
    PAYROLL_CALCULATOR: 'Расчетчик зарплаты',
    BUDGET_OWNER: 'Владелец ЦФО', FIN_CONTROLLER: 'Финансовый контролёр', FIN_DIRECTOR: 'Финансовый директор',
    GENERAL_DIRECTOR: 'Генеральный директор', NSI_SPECIALIST: 'Специалист НСИ', TREASURER: 'Казначей',
};
export function expenseRequestRoleStage(role: string): ExpenseRequestCurrentStage | undefined {
    return Object.prototype.hasOwnProperty.call(roleStages, role) ? roleStages[role] : undefined;
}
export function expenseRequestStatusLabel(status: ExpenseRequestStatus): string {
    return labels[status];
}
export function expenseRequestStatusAtStageLabel(status: ExpenseRequestStatus, currentStage?: string | null): string {
    const stage = currentStage?.trim();
    if (status === 'IN_APPROVAL' && stage && Object.prototype.hasOwnProperty.call(stageLabels, stage))
        return stageLabels[stage as ExpenseRequestCurrentStage];
    return expenseRequestStatusLabel(status);
}
/** Presentation only. Never infer the current approval stage from the viewer. */
export function expenseRequestStatusTone(status: ExpenseRequestStatus | null, currentStage?: string | null): 'cfo-owner' | undefined {
    if (status !== 'IN_APPROVAL')
        return undefined;
    const stage = currentStage?.trim();
    return stage === 'Владелец ЦФО' || stage === stageLabels['Владелец ЦФО'] ? 'cfo-owner' : undefined;
}
/** Display-only bridge for safe historical status fields; never rewrite audit data. */
export function expenseRequestHistoryStatusLabel(value: string | null): string {
    if (value === null)
        return '—';
    const legacy: Record<string, ExpenseRequestStatus> = { 'Возвращена': 'RETURNED', 'Утверждено': 'APPROVED', 'Утверждена': 'APPROVED', 'Ожидает НСИ': 'AWAITING_NSI', 'К оплате': 'TO_PAY', 'В оплату': 'TO_PAY', 'Оплачена частично': 'PARTIALLY_PAID', 'Оплачена': 'PAID' };
    if (isExpenseRequestStatus(value))
        return expenseRequestStatusLabel(value);
    return Object.prototype.hasOwnProperty.call(legacy, value) ? expenseRequestStatusLabel(legacy[value]) : value;
}
