import { apiUrl, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
import { isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
export type CfoAssignment = {
    employeeId: string;
    costCenterId: string;
    costCenterCode: string;
    costCenterName: string;
    roleCode: 'BUDGET_OWNER' | 'CFO_INITIATOR';
    isActive: boolean;
};
export type CfoState = {
    assignments: CfoAssignment[];
    costCenters: {
        id: string;
        code: string;
        name: string;
    }[];
    concurrencyStamp: string;
};
export type CfoCommand = {
    costCenterId: string;
    roleCode: CfoAssignment['roleCode'];
    isActive: boolean;
    expectedConcurrencyStamp: string;
};
export type BudgetReview = {
    budgetInstanceId: string;
    reviewId: string | null;
    concurrencyStamp: string | null;
    state: 'Draft' | 'Pending' | 'Returned' | 'Rejected' | 'Approved';
    currentRoleCode: string | null;
    positiveExcessKzt: number;
    canSubmit: boolean;
    canDecide: boolean;
    baselineChanged?: boolean;
    canReturn?: boolean;
    canReject?: boolean;
    stages: {
        stageNumber: number;
        roleCode: string;
        employeeIds: string[];
    }[];
    decisions: {
        stageNumber: number;
        roleCode: string;
        employeeId: string;
        actorExternalId: string;
        decision: number;
        decidedAtUtc: string;
        comment: string | null;
        actingRoleCode?: string | null;
    }[];
};
export type BudgetReviewCommand = {
    expectedConcurrencyStamp: string;
} | {
    reviewId: string;
    expectedConcurrencyStamp: string;
    decision: 'Approve' | 'Return' | 'Reject';
    comment: string | null;
};
export class CfoWorkflowError extends Error {
    constructor(public status: number, public code?: string) {
        super(status === 409 && code === 'budget-baseline-changed'
            ? 'Состояние изменилось. Обновите документ и повторите действие.'
            : status === 409 ? 'Состояние изменилось. Обновите документ.'
                : status === 403 ? 'Недостаточно прав для этой операции.'
                    : status === 400 ? 'Операция недоступна в текущем состоянии. Проверьте назначения и маршрут.'
                        : 'Результат операции не подтверждён. Повторите ту же попытку.');
    }
}
export function isUncertainWorkflowError(error: unknown) {
    return !(error instanceof CfoWorkflowError) || ![400, 401, 403, 404, 409].includes(error.status);
}
function record(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function invalid(): never { throw new CfoWorkflowError(502); }
function text(value: unknown): value is string { return typeof value === 'string' && value.length <= 2000 && !/[\x00-\x08]/.test(value); }
export function publicCfoAssignments(value: unknown): CfoAssignment[] {
    if (!Array.isArray(value) || value.length > 20000)
        return invalid();
    const keys = new Set<string>();
    for (const a of value) {
        if (!record(a) || !isPublicExpenseRequestUuid(a.employeeId) || !isPublicExpenseRequestUuid(a.costCenterId)
            || !text(a.costCenterCode) || !text(a.costCenterName) || !['BUDGET_OWNER', 'CFO_INITIATOR'].includes(String(a.roleCode))
            || typeof a.isActive !== 'boolean')
            return invalid();
        const key = a.employeeId + ':' + a.costCenterId + ':' + a.roleCode;
        if (keys.has(key))
            return invalid();
        keys.add(key);
    }
    return value as CfoAssignment[];
}
export function publicCfoState(value: unknown): CfoState {
    if (!record(value) || typeof value.concurrencyStamp !== 'string' || !/^[A-F0-9]{64}$/.test(value.concurrencyStamp)
        || !Array.isArray(value.costCenters) || value.costCenters.length > 2000)
        return invalid();
    const assignments = publicCfoAssignments(value.assignments);
    for (const c of value.costCenters)
        if (!record(c) || !isPublicExpenseRequestUuid(c.id) || !text(c.code) || !text(c.name))
            return invalid();
    if (new Set(value.costCenters.map(c => c.id)).size !== value.costCenters.length)
        return invalid();
    return { assignments, costCenters: value.costCenters, concurrencyStamp: value.concurrencyStamp } as CfoState;
}
export function publicBudgetReview(value: unknown, id: string): BudgetReview {
    const roles = ['BUDGET_OWNER', 'FIN_CONTROLLER', 'FIN_DIRECTOR', 'GENERAL_DIRECTOR'];
    if (!record(value) || value.budgetInstanceId !== id || !['Draft', 'Pending', 'Returned', 'Rejected', 'Approved'].includes(String(value.state))
        || typeof value.canSubmit !== 'boolean' || typeof value.canDecide !== 'boolean'
        || (value.canReturn !== undefined && typeof value.canReturn !== 'boolean')
        || (value.canReject !== undefined && typeof value.canReject !== 'boolean')
        || ((value.canReturn === true || value.canReject === true) && value.state !== 'Pending')
        || (!value.canDecide && (value.canReturn === true || value.canReject === true) && value.currentRoleCode !== 'FIN_DIRECTOR')
        || (value.baselineChanged !== undefined && typeof value.baselineChanged !== 'boolean')
        || (value.baselineChanged === true && value.state !== 'Pending')
        || typeof value.positiveExcessKzt !== 'number' || !Number.isFinite(value.positiveExcessKzt) || value.positiveExcessKzt < 0
        || !Array.isArray(value.stages) || value.stages.length > 4 || !Array.isArray(value.decisions) || value.decisions.length > 4)
        return invalid();
    const legacyApproved = value.state === 'Approved' && value.reviewId === null;
    if (value.state === 'Draft' || legacyApproved) {
        if (value.reviewId !== null || value.concurrencyStamp !== null || value.currentRoleCode !== null
            || value.canDecide || value.stages.length || value.decisions.length || (legacyApproved && value.canSubmit))
            return invalid();
    }
    else if (!isPublicExpenseRequestUuid(value.reviewId) || !isPublicExpenseRequestUuid(value.concurrencyStamp))
        return invalid();
    const firstOwner = value.stages[0]?.roleCode === 'BUDGET_OWNER';
    const legacyRoles = [
        ...(firstOwner ? ['BUDGET_OWNER'] : []), 'FIN_CONTROLLER',
        ...(value.positiveExcessKzt > 0 ? ['FIN_DIRECTOR'] : []),
        ...(value.positiveExcessKzt > 100000 ? ['GENERAL_DIRECTOR'] : []),
    ];
    // Current budgets always require both directors. Frozen Main1 and legacy
    // histories retain their exact captured routes; never accept a hybrid.
    const currentRoles = ['FIN_DIRECTOR', 'GENERAL_DIRECTOR'];
    const main1Roles = ['FIN_DIRECTOR', ...(value.positiveExcessKzt > 100000 ? ['GENERAL_DIRECTOR'] : [])];
    const capturedStages = value.stages;
    const matches = (expected: string[]) => capturedStages.length === expected.length
        && capturedStages.every((stage, index) => record(stage) && stage.roleCode === expected[index]);
    const expectedRoles = matches(currentRoles) ? currentRoles : matches(main1Roles) ? main1Roles : legacyRoles;
    if (value.state !== 'Draft' && !legacyApproved && (value.stages.length !== expectedRoles.length || value.stages.some((s, i) => !record(s) || s.stageNumber !== i + 1 || s.roleCode !== expectedRoles[i] || !Array.isArray(s.employeeIds)
        || s.employeeIds.length === 0 || s.employeeIds.some(e => !isPublicExpenseRequestUuid(e))
        || new Set(s.employeeIds).size !== s.employeeIds.length)))
        return invalid();
    for (const [i, d] of value.decisions.entries())
        if (!record(d) || d.stageNumber !== i + 1
            || d.roleCode !== expectedRoles[i] || !isPublicExpenseRequestUuid(d.employeeId) || !text(d.actorExternalId)
            || typeof d.decision !== 'number' || ![0, 1, 2].includes(d.decision)
            || (d.actingRoleCode !== undefined && d.actingRoleCode !== null
                && (d.actingRoleCode !== 'FIN_CONTROLLER' || d.roleCode !== 'FIN_DIRECTOR' || ![1, 2].includes(d.decision)))
            || (d.actingRoleCode !== 'FIN_CONTROLLER' && !value.stages[i]?.employeeIds.includes(d.employeeId))
            || typeof d.decidedAtUtc !== 'string' || !Number.isFinite(Date.parse(d.decidedAtUtc))
            || (d.comment !== null && !text(d.comment)))
            return invalid();
    if (value.state === 'Pending') {
        if (!roles.includes(String(value.currentRoleCode)) || value.currentRoleCode !== expectedRoles[value.decisions.length]
            || value.canSubmit || value.decisions.some(d => d.decision !== 0))
            return invalid();
    }
    else if (value.currentRoleCode !== null || value.canDecide)
        return invalid();
    if (value.state === 'Approved' && !legacyApproved && (value.canSubmit || value.decisions.length !== value.stages.length
        || value.decisions.some(d => d.decision !== 0)))
        return invalid();
    if (value.state === 'Returned' || value.state === 'Rejected') {
        if (value.decisions.length === 0 || value.decisions.at(-1)?.decision !== (value.state === 'Returned' ? 1 : 2)
            || value.decisions.slice(0, -1).some(d => d.decision !== 0))
            return invalid();
    }
    return value as BudgetReview;
}
async function request(path: string, command?: object, key?: string, method: 'POST' | 'PUT' = 'POST', signal?: AbortSignal) {
    const init = command ? secureWriteInit(method, { 'Content-Type': 'application/json', 'Idempotency-Key': key ?? '' }, JSON.stringify(command)) : secureReadInit();
    const response = await fetchWithSafeTransport(apiUrl(path), { ...init, signal });
    if (response.status !== 200) {
        let code: string | undefined;
        if (response.status === 409) {
            try {
                const body: unknown = await response.json();
                if (record(body) && body.code === 'budget-baseline-changed')
                    code = body.code;
            }
            catch { /* Untrusted or absent error bodies retain the generic conflict. */ }
        }
        throw new CfoWorkflowError(response.status, code);
    }
    try {
        return await response.json() as unknown;
    }
    catch {
        return invalid();
    }
}
const employeePath = (id: string) => '/api/v1/reference-data/authorization-directory/employees/' + encodeURIComponent(id) + '/cost-centers';
export async function listCfoAssignments(signal?: AbortSignal) {
    return publicCfoAssignments(await request('/api/v1/reference-data/authorization-directory/cfo-assignments', undefined, undefined, 'POST', signal));
}
function stateForEmployee(value: unknown, id: string) {
    const state = publicCfoState(value);
    if (state.assignments.some(a => a.employeeId !== id))
        return invalid();
    return state;
}
export async function getCfoState(id: string, signal?: AbortSignal) { return stateForEmployee(await request(employeePath(id), undefined, undefined, 'POST', signal), id); }
export async function writeCfoState(id: string, command: CfoCommand, key: string) {
    return stateForEmployee(await request(employeePath(id), command, key, 'PUT'), id);
}
export async function getBudgetReview(id: string, signal?: AbortSignal) {
    return publicBudgetReview(await request('/api/v1/budget/instances/' + encodeURIComponent(id) + '/approval', undefined, undefined, 'POST', signal), id);
}
export async function writeBudgetReview(id: string, command: BudgetReviewCommand, key: string) {
    return publicBudgetReview(await request('/api/v1/budget/instances/' + encodeURIComponent(id)
        + ('reviewId' in command ? '/approval-decisions' : '/submit'), command, key), id);
}
