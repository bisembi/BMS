import type { BmsAccessProfileCode, SwitchableBmsAccessProfileCode } from '../types';
export const roleLabels: Readonly<Record<string, string>> = {
    PAYROLL_CALCULATOR: 'Расчетчик зарплаты',
    BUDGET_OWNER: 'Владелец бюджета', CFO_INITIATOR: 'Инициатор ЦФО', FIN_CONTROLLER: 'Финансовый контролёр',
    FIN_DIRECTOR: 'Финансовый директор', GENERAL_DIRECTOR: 'Генеральный директор', TREASURER: 'Казначей',
    ACCOUNTANT: 'Бухгалтер', RESPONSIBLE_ACCOUNTANT: 'Ответственный бухгалтер', NSI_SPECIALIST: 'Специалист НСИ',
    CHIEF_ACCOUNTANT: 'Главный бухгалтер',
    AUDITOR: 'Аудитор', SYSTEM_ADMIN: 'Системный администратор', INTEGRATION_ADMIN: 'Администратор интеграции',
};
export function isBmsRoleCode(value: unknown): value is string {
    return typeof value === 'string' && Object.hasOwn(roleLabels, value);
}
export function showsHideDrafts(role: string | null | undefined): boolean {
    return isBmsRoleCode(role) && role !== 'BUDGET_OWNER' && role !== 'CFO_INITIATOR';
}
export function showsExpenseRequestActions(role: string | null | undefined): boolean {
    return role === 'BUDGET_OWNER' || role === 'CFO_INITIATOR';
}
export function profileForRole(role: string): BmsAccessProfileCode {
    return role === 'SYSTEM_ADMIN' ? 'SYSTEM_ADMIN' : role === 'INTEGRATION_ADMIN' ? 'INTEGRATION_ADMIN' : 'BUSINESS';
}
export type RoleSelection = {
    profileCode: SwitchableBmsAccessProfileCode;
    roleCode: string;
    substitutionId?: string;
};
export type StaffRoleChoice471 = {
    documentId: string;
    roleCode: string;
    principalName: string;
    from: string;
    through?: string | null;
};
export function roleSelectionValue471(role: string | null | undefined, document?: string | null) { return role ? (document ? `${role}@${document}` : role) : ''; }
