import type { ApprovalMatrixCommand, ApprovalMatrixCondition, ApprovalMatrixRole, ApprovalMatrixTransition } from '../../api/approvalMatrixPublicResponse';
export const approvalRoleLabels: Record<ApprovalMatrixRole, string> = {
    BUDGET_OWNER: 'Владелец ЦФО',
    INITIATOR: 'Инициатор — автор заявки',
    FIN_CONTROLLER: 'Финансовый контролёр',
    FIN_DIRECTOR: 'Финансовый директор',
    GENERAL_DIRECTOR: 'Генеральный директор',
    NSI_SPECIALIST: 'Специалист НСИ',
    TREASURER: 'Казначей',
};
const commandLabels: Record<ApprovalMatrixCommand, string> = {
    SUBMIT: 'Подать', APPROVE: 'Согласовать', RETURN: 'Вернуть', REJECT: 'Отклонить',
    CANCEL: 'Отменить', CONFIRM_NSI: 'Подтвердить НСИ', SEND_TO_PAYMENT: 'В оплату',
};
export const approvalConditionLabels: Record<ApprovalMatrixCondition, string> = {
    '': 'По правам и состоянию заявки',
    SUBMISSION_READY: 'Готовность к подаче подтверждена',
    ROUTE_HAS_NEXT_STAGE: 'В маршруте есть следующий этап',
    ROUTE_LAST_NO_NSI: 'Последний этап; подтверждение НСИ не требуется',
    ROUTE_LAST_REQUIRES_NSI: 'Последний этап; требуется подтверждение НСИ',
    NSI_CANDIDATE_LINKED: 'Кандидаты связаны с импортированными данными НСИ',
};
export function approvalCommandLabel(row: ApprovalMatrixTransition): string {
    return row.commandCode === 'APPROVE' && row.conditionCode !== 'ROUTE_HAS_NEXT_STAGE'
        ? 'Утвердить' : commandLabels[row.commandCode];
}
