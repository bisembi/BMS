import type { SubmissionPreflight, SubmissionPreflightBlocker, SubmissionReadiness } from '../types';
import { isPublicExpenseRequestMoney, isPublicExpenseRequestText, isPublicExpenseRequestTimestamp, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
import { safeSubmissionReadinessRequirementList } from './submissionReadinessRequirements';
const invalidPreflightResponseMessage = 'Некорректный ответ API проверки готовности.';
const maximumCollectionSize = 1000;
const allowedCategories = new Set([
    'command',
    'budget',
    'attachment',
    'accrual',
    'data',
    'workflow',
    'roleAssignments',
]);
const allowedBlockerCodesByCategory: ReadonlyMap<string, ReadonlySet<string>> = new Map([
    ['command', new Set([
            'Command.AdministratorReadOnly',
            'Command.BudgetOwnerRequired',
        ])],
    ['budget', new Set([
            'BudgetProjection.Incomplete',
            'BudgetProjection.Drift',
            'BudgetSnapshot.Unavailable',
            'BudgetLimit.Unavailable',
            'ApprovalRoute.DeficitScaleUnsupported',
        ])],
    ['attachment', new Set([
            'AttachmentFacts.Unavailable',
        ])],
    ['accrual', new Set([
            'AccrualSnapshotSource.Unavailable',
        ])],
    ['data', new Set([
            'AccrualSnapshotSource.CashFlowItemMissing',
            'AccrualSnapshotSource.CashFlowItemInactive',
            'AccrualSnapshotSource.CashFlowItemGroup',
            'ExpenseRequestLine.AccrualPeriodMissing',
            'ExpenseRequestLine.AccrualPeriodNotMonthStart',
        ])],
    ['workflow', new Set([
            'WorkflowDefinition.Unavailable',
            'WorkflowSubmitCommand.Unavailable',
        ])],
    ['roleAssignments', new Set([
            'ApprovalRoleAssignments.Unavailable',
        ])],
]);
const nullDeficitBudgetBlockers = new Set([
    'BudgetProjection.Incomplete',
    'BudgetProjection.Drift',
    'BudgetSnapshot.Unavailable',
    'BudgetLimit.Unavailable',
]);
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function hasExactKeys(value: Record<string, unknown>, expectedKeys: readonly string[]): boolean {
    const actualKeys = Object.keys(value);
    return actualKeys.length === expectedKeys.length
        && expectedKeys.every((key) => Object.prototype.hasOwnProperty.call(value, key));
}
function safeCodeList(value: unknown): string[] | null {
    if (!Array.isArray(value) || value.length > maximumCollectionSize)
        return null;
    const codes = value.filter((item): item is string => isPublicExpenseRequestText(item, 256));
    if (codes.length !== value.length)
        return null;
    return new Set(codes).size === codes.length ? [...codes] : null;
}
function isLineNumber(value: unknown): value is number {
    return typeof value === 'number'
        && Number.isSafeInteger(value)
        && value > 0
        && value <= 10000;
}
function publicReadiness(value: unknown): SubmissionReadiness | null {
    if (!isRecord(value)
        || !hasExactKeys(value, ['isReady', 'missingRequirements'])
        || typeof value.isReady !== 'boolean')
        return null;
    const missingRequirements = safeSubmissionReadinessRequirementList(value.missingRequirements);
    if (!missingRequirements || value.isReady !== (missingRequirements.length === 0))
        return null;
    return { isReady: value.isReady, missingRequirements };
}
function publicBlocker(value: unknown): SubmissionPreflightBlocker | null {
    const lineNumber = isRecord(value) ? value.lineNumber : undefined;
    const normalizedLineNumber: number | null = lineNumber === null
        ? null
        : isLineNumber(lineNumber) ? lineNumber : null;
    const lineNumberMatchesCategory = isRecord(value)
        && (value.category === 'data' ? isLineNumber(lineNumber) : lineNumber === null);
    if (!isRecord(value)
        || !hasExactKeys(value, ['category', 'code', 'lineNumber'])
        || typeof value.category !== 'string'
        || !allowedCategories.has(value.category)
        || !isPublicExpenseRequestText(value.code, 256)
        || !allowedBlockerCodesByCategory.get(value.category)?.has(value.code)
        || !lineNumberMatchesCategory) {
        return null;
    }
    return {
        category: value.category,
        code: value.code,
        lineNumber: normalizedLineNumber,
    };
}
function requiredRoles(positiveDeficitKzt: number, ownerFirst = false): string[] {
    return [
        ...(ownerFirst ? ['BUDGET_OWNER'] : []),
        'FIN_CONTROLLER',
        ...(positiveDeficitKzt > 0 ? ['FIN_DIRECTOR'] : []),
        ...(positiveDeficitKzt > 100000 ? ['GENERAL_DIRECTOR'] : []),
    ];
}
function budgetBlockersMatchDeficit(blockers: readonly string[], positiveDeficitKzt: number | null): boolean {
    if (blockers.length === 0)
        return positiveDeficitKzt !== null;
    if (blockers.length !== 1)
        return false;
    const [blocker] = blockers;
    if (nullDeficitBudgetBlockers.has(blocker))
        return positiveDeficitKzt === null;
    return blocker === 'ApprovalRoute.DeficitScaleUnsupported'
        && positiveDeficitKzt === null;
}
export function publicSubmissionPreflight(payload: unknown, expectedRequestId?: string): SubmissionPreflight {
    if (!isRecord(payload)
        || !hasExactKeys(payload, [
            'requestId',
            'requestVersion',
            'evaluatedAtUtc',
            'submissionReadiness',
            'budgetReadiness',
            'capabilities',
            'routePreview',
            'canSubmit',
            'blockers',
            ...(Object.prototype.hasOwnProperty.call(payload, 'operationKind') ? ['operationKind'] : []),
        ])
        || (payload.operationKind !== undefined && payload.operationKind !== 'PayrollAdvance')
        || !isPublicExpenseRequestUuid(payload.requestId)
        || (expectedRequestId !== undefined
            && (!isPublicExpenseRequestUuid(expectedRequestId)
                || payload.requestId.toLowerCase() !== expectedRequestId.toLowerCase()))
        || !isPublicExpenseRequestUuid(payload.requestVersion)
        || !isPublicExpenseRequestTimestamp(payload.evaluatedAtUtc)
        || !isRecord(payload.budgetReadiness)
        || !isRecord(payload.capabilities)
        || typeof payload.canSubmit !== 'boolean'
        || !Array.isArray(payload.blockers)
        || payload.blockers.length > maximumCollectionSize) {
        throw new Error(invalidPreflightResponseMessage);
    }
    const submissionReadiness = publicReadiness(payload.submissionReadiness);
    if (!hasExactKeys(payload.budgetReadiness, [
        'isReady',
        'positiveDeficitKzt',
        'blockers',
    ]) || !hasExactKeys(payload.capabilities, [
        'attachmentFactsReady',
        'accrualSnapshotSourceReady',
        'workflowSeedReady',
        'roleAssignmentsReady',
    ])) {
        throw new Error(invalidPreflightResponseMessage);
    }
    const budgetBlockers = safeCodeList(payload.budgetReadiness.blockers);
    const positiveDeficitKzt = payload.budgetReadiness.positiveDeficitKzt;
    const budgetIsReady = payload.budgetReadiness.isReady;
    if (!submissionReadiness
        || typeof budgetIsReady !== 'boolean'
        || !(positiveDeficitKzt === null || isPublicExpenseRequestMoney(positiveDeficitKzt))
        || !budgetBlockers
        || !budgetBlockersMatchDeficit(budgetBlockers, positiveDeficitKzt)
        || (budgetIsReady && (positiveDeficitKzt === null || budgetBlockers.length !== 0))
        || (!budgetIsReady && budgetBlockers.length === 0)) {
        throw new Error(invalidPreflightResponseMessage);
    }
    const capabilities = payload.capabilities;
    if (typeof capabilities.attachmentFactsReady !== 'boolean'
        || typeof capabilities.accrualSnapshotSourceReady !== 'boolean'
        || typeof capabilities.workflowSeedReady !== 'boolean'
        || typeof capabilities.roleAssignmentsReady !== 'boolean') {
        throw new Error(invalidPreflightResponseMessage);
    }
    const blockers = payload.blockers.map(publicBlocker);
    const publicBlockers = blockers.filter((blocker): blocker is SubmissionPreflightBlocker => blocker !== null);
    if (publicBlockers.length !== blockers.length) {
        throw new Error(invalidPreflightResponseMessage);
    }
    const blockerKeys = publicBlockers.map((blocker) => (`${blocker.category}:${blocker.code}:${blocker.lineNumber ?? ''}`));
    const hasBlocker = (category: string, code: string) => publicBlockers.some((blocker) => (blocker.category === category && blocker.code === code && blocker.lineNumber === null));
    const hasAccrualSourceBlocker = publicBlockers.some((blocker) => (blocker.category === 'accrual' || blocker.category === 'data'));
    const hasAttachmentFactsUnavailable = hasBlocker('attachment', 'AttachmentFacts.Unavailable');
    const hasWorkflowDefinitionUnavailable = hasBlocker('workflow', 'WorkflowDefinition.Unavailable');
    const hasWorkflowSubmitCommandUnavailable = hasBlocker('workflow', 'WorkflowSubmitCommand.Unavailable');
    const hasRoleAssignmentsUnavailable = hasBlocker('roleAssignments', 'ApprovalRoleAssignments.Unavailable');
    if (new Set(blockerKeys).size !== blockerKeys.length
        || capabilities.attachmentFactsReady === hasAttachmentFactsUnavailable
        || capabilities.accrualSnapshotSourceReady === hasAccrualSourceBlocker
        || hasWorkflowDefinitionUnavailable !== hasWorkflowSubmitCommandUnavailable
        || capabilities.workflowSeedReady === hasWorkflowDefinitionUnavailable
        || capabilities.roleAssignmentsReady === hasRoleAssignmentsUnavailable) {
        throw new Error(invalidPreflightResponseMessage);
    }
    let routePreview: SubmissionPreflight['routePreview'] = null;
    if (budgetIsReady) {
        if (!isPublicExpenseRequestMoney(positiveDeficitKzt)) {
            throw new Error(invalidPreflightResponseMessage);
        }
        const readyDeficit = positiveDeficitKzt;
        if (!isRecord(payload.routePreview)
            || !hasExactKeys(payload.routePreview, [
                'previewOnly',
                'positiveDeficitKzt',
                'requiredRoleCodes',
                'assigneesResolved',
            ])
            || payload.routePreview.previewOnly !== true
            || payload.routePreview.assigneesResolved !== capabilities.roleAssignmentsReady
            || payload.routePreview.positiveDeficitKzt !== positiveDeficitKzt) {
            throw new Error(invalidPreflightResponseMessage);
        }
        const roleCodes = safeCodeList(payload.routePreview.requiredRoleCodes);
        const seniorRoles = readyDeficit > 100000 ? ['GENERAL_DIRECTOR'] : [];
        const directorAuthorRoles = readyDeficit > 100000
            ? ['FIN_CONTROLLER', 'GENERAL_DIRECTOR'] : ['BUDGET_OWNER', 'FIN_CONTROLLER'];
        const additionalAuthorRoutes = readyDeficit > 100000
            ? [['BUDGET_OWNER', 'FIN_CONTROLLER', 'GENERAL_DIRECTOR'],
                ['FIN_CONTROLLER', 'FIN_DIRECTOR'], ['BUDGET_OWNER', 'FIN_CONTROLLER', 'FIN_DIRECTOR']]
            : [];
        // Assigned controller authors satisfy their own control on submission.
        // Keep the independent owner and the same deficit-driven FD/GD thresholds.
        const controllerAuthorRoles = readyDeficit > 100000
            ? ['FIN_DIRECTOR', 'GENERAL_DIRECTOR'] : readyDeficit > 0 ? ['FIN_DIRECTOR'] : [];
        const controllerAuthorRoutes = [controllerAuthorRoles, ['BUDGET_OWNER', ...controllerAuthorRoles]];
        const advanceRoutes = [[], ['BUDGET_OWNER'], ['FIN_DIRECTOR'], ['BUDGET_OWNER', 'FIN_DIRECTOR'],
            ['PAYROLL_CALCULATOR'], ['BUDGET_OWNER', 'PAYROLL_CALCULATOR'], ['FIN_DIRECTOR', 'PAYROLL_CALCULATOR'],
            ['BUDGET_OWNER', 'FIN_DIRECTOR', 'PAYROLL_CALCULATOR']];
        const advance = payload.operationKind === 'PayrollAdvance';
        if (!roleCodes
            || (advance ? readyDeficit !== 0 || !advanceRoutes.some(roles => JSON.stringify(roles) === JSON.stringify(roleCodes))
                : (JSON.stringify(roleCodes) !== JSON.stringify(requiredRoles(readyDeficit, roleCodes[0] === 'BUDGET_OWNER'))
                    && JSON.stringify(roleCodes) !== JSON.stringify(seniorRoles)
                    && JSON.stringify(roleCodes) !== JSON.stringify(directorAuthorRoles)
                    && !additionalAuthorRoutes.some(roles => JSON.stringify(roles) === JSON.stringify(roleCodes))
                    && !controllerAuthorRoutes.some(roles => JSON.stringify(roles) === JSON.stringify(roleCodes))))) {
            throw new Error(invalidPreflightResponseMessage);
        }
        routePreview = {
            previewOnly: true,
            positiveDeficitKzt: readyDeficit,
            requiredRoleCodes: roleCodes,
            assigneesResolved: capabilities.roleAssignmentsReady,
        };
    }
    else if (payload.routePreview !== null) {
        throw new Error(invalidPreflightResponseMessage);
    }
    const publicBudgetBlockers = publicBlockers.filter((blocker) => blocker.category === 'budget');
    if (new Set(blockerKeys).size !== blockerKeys.length
        || publicBudgetBlockers.length !== budgetBlockers.length
        || budgetBlockers.some((code) => (!publicBudgetBlockers.some((blocker) => (blocker.code === code && blocker.lineNumber === null))))) {
        throw new Error(invalidPreflightResponseMessage);
    }
    const hasCommandBlocker = publicBlockers.some((blocker) => blocker.category === 'command');
    if (payload.canSubmit && (!submissionReadiness.isReady
        || !budgetIsReady
        || !capabilities.attachmentFactsReady
        || !capabilities.accrualSnapshotSourceReady
        || !capabilities.workflowSeedReady
        || !capabilities.roleAssignmentsReady
        || hasCommandBlocker)) {
        throw new Error(invalidPreflightResponseMessage);
    }
    return {
        ...(payload.operationKind === 'PayrollAdvance' ? { operationKind: 'PayrollAdvance' as const } : {}),
        requestId: payload.requestId,
        requestVersion: payload.requestVersion,
        evaluatedAtUtc: payload.evaluatedAtUtc,
        submissionReadiness,
        budgetReadiness: {
            isReady: budgetIsReady,
            positiveDeficitKzt,
            blockers: budgetBlockers,
        },
        capabilities: {
            attachmentFactsReady: capabilities.attachmentFactsReady,
            accrualSnapshotSourceReady: capabilities.accrualSnapshotSourceReady,
            workflowSeedReady: capabilities.workflowSeedReady,
            roleAssignmentsReady: capabilities.roleAssignmentsReady,
        },
        routePreview,
        canSubmit: payload.canSubmit,
        blockers: publicBlockers,
    };
}
