import { expenseRequestStatusCodes, expenseRequestStatusLabel, isExpenseRequestStatus } from '../expenseRequestStatus';
import type { ExpenseRequestStatus } from '../types';
export const approvalMatrixRoleCodes = [
    'INITIATOR', 'FIN_CONTROLLER', 'FIN_DIRECTOR', 'GENERAL_DIRECTOR', 'NSI_SPECIALIST', 'TREASURER', 'BUDGET_OWNER',
] as const;
export type ApprovalMatrixRole = typeof approvalMatrixRoleCodes[number];
export const approvalMatrixCommandCodes = [
    'SUBMIT', 'APPROVE', 'RETURN', 'REJECT', 'CANCEL', 'CONFIRM_NSI', 'SEND_TO_PAYMENT',
] as const;
export type ApprovalMatrixCommand = typeof approvalMatrixCommandCodes[number];
const conditionCodes = [
    '', 'SUBMISSION_READY', 'ROUTE_HAS_NEXT_STAGE', 'ROUTE_LAST_NO_NSI',
    'ROUTE_LAST_REQUIRES_NSI', 'NSI_CANDIDATE_LINKED',
] as const;
export type ApprovalMatrixCondition = typeof conditionCodes[number];
export type ApprovalMatrixTransition = {
    statusCode: ExpenseRequestStatus;
    stageNumber: number;
    roleCode: ApprovalMatrixRole;
    commandCode: ApprovalMatrixCommand;
    targetStatusCode: ExpenseRequestStatus;
    targetStageNumber: number;
    conditionCode: ApprovalMatrixCondition;
    requiresReason: boolean;
};
export type ApprovalMatrixRoute = {
    deficitBand: 'noDeficit' | 'withinThreshold' | 'aboveThreshold';
    stages: {
        number: number;
        roleCode: ApprovalMatrixRole;
    }[];
};
export type ApprovalMatrix = {
    definitionVersion: number;
    generalDirectorThresholdKzt: number;
    statuses: {
        code: ExpenseRequestStatus;
        name: string;
    }[];
    routes: ApprovalMatrixRoute[];
    transitions: ApprovalMatrixTransition[];
};
const malformedMessage = 'Некорректный ответ API матрицы согласования.';
const approvalRoles = ['FIN_CONTROLLER', 'FIN_DIRECTOR', 'GENERAL_DIRECTOR'] as const;
const routeBands = ['noDeficit', 'withinThreshold', 'aboveThreshold'] as const;
// SQL workflow registry names are immutable machine-contract evidence, not
// presentation labels. A UI wording change must not invalidate a verified
// matrix. Accept only the finite registry names or our own current labels;
// render only local labels, never arbitrary provider text.
const registryStatusNames: Record<ExpenseRequestStatus, string> = {
    DRAFT: 'Черновик', RETURNED: 'Возвращена', IN_APPROVAL: 'На согласовании',
    APPROVED: 'Утверждено', AWAITING_NSI: 'Ожидает НСИ', TO_PAY: 'К оплате',
    PARTIALLY_PAID: 'Оплачена частично', PAID: 'Оплачена', CLOSED: 'Закрыта',
    REJECTED: 'Отклонена', CANCELLED: 'Отменена',
};
function record(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function exact(value: Record<string, unknown>, keys: readonly string[]): boolean {
    const actual = Object.keys(value);
    return actual.length === keys.length && actual.every((key) => keys.includes(key));
}
function member<T extends string>(value: unknown, options: readonly T[]): value is T {
    return typeof value === 'string' && options.some((option) => option === value);
}
function stage(value: unknown): value is number {
    return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 4;
}
function permittedTransition(row: ApprovalMatrixTransition): boolean {
    const target = (code: ExpenseRequestStatus, number = 0, condition: ApprovalMatrixCondition = '') => row.targetStatusCode === code && row.targetStageNumber === number && row.conditionCode === condition;
    const cancel = row.roleCode === 'INITIATOR' && row.commandCode === 'CANCEL' && target('CANCELLED');
    if (row.statusCode === 'IN_APPROVAL') {
        if (row.stageNumber < 1 || row.stageNumber > 3)
            return false;
        if (cancel)
            return true;
        if (row.roleCode !== approvalRoles[row.stageNumber - 1])
            return false;
        if (row.commandCode === 'RETURN')
            return target('RETURNED');
        if (row.commandCode === 'REJECT')
            return target('REJECTED');
        return row.commandCode === 'APPROVE'
            && (target('APPROVED', 0, 'ROUTE_LAST_NO_NSI')
                || target('AWAITING_NSI', 0, 'ROUTE_LAST_REQUIRES_NSI')
                || (row.stageNumber < 3 && target('IN_APPROVAL', row.stageNumber + 1, 'ROUTE_HAS_NEXT_STAGE')));
    }
    if (row.stageNumber !== 0)
        return false;
    if (row.statusCode === 'DRAFT' || row.statusCode === 'RETURNED') {
        return cancel || (row.roleCode === 'INITIATOR' && row.commandCode === 'SUBMIT'
            && target('IN_APPROVAL', 1, 'SUBMISSION_READY'));
    }
    if (row.statusCode === 'AWAITING_NSI') {
        return cancel || (row.roleCode === 'NSI_SPECIALIST' && row.commandCode === 'CONFIRM_NSI'
            && target('APPROVED', 0, 'NSI_CANDIDATE_LINKED'));
    }
    if (row.statusCode === 'APPROVED') {
        return cancel || (row.roleCode === 'TREASURER' && row.commandCode === 'SEND_TO_PAYMENT'
            && target('TO_PAY'));
    }
    return row.statusCode === 'TO_PAY' && cancel;
}
function transition(value: unknown, version: number): ApprovalMatrixTransition {
    if (!record(value) || !exact(value, [
        'statusCode', 'stageNumber', 'roleCode', 'commandCode', 'targetStatusCode',
        'targetStageNumber', 'conditionCode', 'requiresReason',
    ]) || !isExpenseRequestStatus(value.statusCode) || !stage(value.stageNumber)
        || !member(value.roleCode, approvalMatrixRoleCodes)
        || !member(value.commandCode, approvalMatrixCommandCodes)
        || !isExpenseRequestStatus(value.targetStatusCode) || !stage(value.targetStageNumber)
        || !member(value.conditionCode, conditionCodes) || typeof value.requiresReason !== 'boolean') {
        throw new Error(malformedMessage);
    }
    const result: ApprovalMatrixTransition = {
        statusCode: value.statusCode, stageNumber: value.stageNumber, roleCode: value.roleCode,
        commandCode: value.commandCode, targetStatusCode: value.targetStatusCode,
        targetStageNumber: value.targetStageNumber, conditionCode: value.conditionCode,
        requiresReason: value.requiresReason,
    };
    const requiresReason = ['RETURN', 'REJECT', 'CANCEL'].includes(result.commandCode);
    let permitted = false;
    if (version === 2 && result.statusCode === 'IN_APPROVAL' && result.stageNumber === 1) {
        permitted = (result.roleCode === 'INITIATOR' && result.commandCode === 'CANCEL' && result.targetStatusCode === 'CANCELLED' && result.targetStageNumber === 0 && result.conditionCode === '')
            || (result.roleCode === 'BUDGET_OWNER' && ((result.commandCode === 'APPROVE' && result.targetStatusCode === 'IN_APPROVAL' && result.targetStageNumber === 2 && result.conditionCode === 'ROUTE_HAS_NEXT_STAGE')
                || (result.commandCode === 'RETURN' && result.targetStatusCode === 'RETURNED' && result.targetStageNumber === 0 && result.conditionCode === '')
                || (result.commandCode === 'REJECT' && result.targetStatusCode === 'REJECTED' && result.targetStageNumber === 0 && result.conditionCode === '')));
    }
    else {
        const financial = version === 2 && result.statusCode === 'IN_APPROVAL';
        permitted = permittedTransition(financial ? { ...result, stageNumber: result.stageNumber - 1,
            targetStageNumber: result.targetStageNumber > 0 ? result.targetStageNumber - 1 : 0 } : result);
    }
    if (result.requiresReason !== requiresReason || !permitted) {
        throw new Error(malformedMessage);
    }
    return result;
}
export function publicApprovalMatrix(value: unknown): ApprovalMatrix {
    if (!record(value) || !exact(value, [
        'definitionVersion', 'generalDirectorThresholdKzt', 'statuses', 'routes', 'transitions',
    ]) || typeof value.definitionVersion !== 'number' || ![1, 2].includes(value.definitionVersion)
        || typeof value.generalDirectorThresholdKzt !== 'number'
        || !Number.isSafeInteger(value.generalDirectorThresholdKzt)
        || value.generalDirectorThresholdKzt <= 0
        || !Array.isArray(value.statuses) || value.statuses.length !== expenseRequestStatusCodes.length
        || !Array.isArray(value.routes) || value.routes.length !== 3
        || !Array.isArray(value.transitions) || value.transitions.length !== (value.definitionVersion === 2 ? 30 : 26)) {
        throw new Error(malformedMessage);
    }
    const statuses = value.statuses.map((item: unknown, index) => {
        if (!record(item) || !exact(item, ['code', 'name']) || !isExpenseRequestStatus(item.code)
            || item.code !== expenseRequestStatusCodes[index]
            || typeof item.name !== 'string'
            || (item.name !== registryStatusNames[item.code] && item.name !== expenseRequestStatusLabel(item.code)))
            throw new Error(malformedMessage);
        return { code: item.code, name: expenseRequestStatusLabel(item.code) };
    });
    const version = Number(value.definitionVersion);
    const routeRoles = version === 2 ? ['BUDGET_OWNER', ...approvalRoles] : approvalRoles;
    const routes = value.routes.map((item: unknown, index): ApprovalMatrixRoute => {
        if (!record(item) || !exact(item, ['deficitBand', 'stages'])
            || !member(item.deficitBand, routeBands) || item.deficitBand !== routeBands[index]
            || !Array.isArray(item.stages) || item.stages.length !== index + (version === 2 ? 2 : 1))
            throw new Error(malformedMessage);
        const stages = item.stages.map((entry: unknown, stageIndex) => {
            if (!record(entry) || !exact(entry, ['number', 'roleCode']) || entry.number !== stageIndex + 1
                || !member(entry.roleCode, approvalMatrixRoleCodes) || entry.roleCode !== routeRoles[stageIndex]) {
                throw new Error(malformedMessage);
            }
            return { number: stageIndex + 1, roleCode: entry.roleCode };
        });
        return { deficitBand: item.deficitBand, stages };
    });
    const transitions = value.transitions.map((item: unknown) => transition(item, version));
    if (new Set(transitions.map((row) => JSON.stringify(row))).size !== (version === 2 ? 30 : 26)) {
        throw new Error(malformedMessage);
    }
    return {
        definitionVersion: version, generalDirectorThresholdKzt: value.generalDirectorThresholdKzt,
        statuses, routes, transitions,
    };
}
