import type { DatabaseReadinessMissingComponent, DatabaseReadinessMonitor, ReferenceProjectionCount, ReferenceProjectionInventory } from '../types';
import { isPublicExpenseRequestTimestamp } from './expenseRequestPublicResponse';
const invalidReadinessResponseMessage = 'Некорректный ответ API готовности БД.';
const invalidProjectionResponseMessage = 'Некорректный ответ API инвентаризации проекций.';
export const databaseReadinessRequiredMigrationCount = 53;
const databaseReadinessMissingComponentCodes: readonly DatabaseReadinessMissingComponent[] = [
    'schema-version-table',
    'migration-001',
    'migration-002',
    'migration-003',
    'migration-004',
    'migration-005',
    'migration-006',
    'migration-007',
    'migration-008',
    'migration-009',
    'migration-010',
    'migration-011',
    'migration-012',
    'migration-013',
    'migration-014',
    'migration-015',
    'migration-016',
    'migration-017',
    'migration-018',
    'migration-019',
    'migration-020',
    'migration-021',
    'migration-022',
    'migration-023',
    'migration-024',
    'migration-025',
    'migration-026',
    'migration-027',
    'migration-028',
    'migration-029',
    'migration-030',
    'migration-031',
    'migration-032',
    'migration-033',
    'migration-034',
    'migration-035',
    'migration-036',
    'migration-037',
    'migration-038',
    'migration-039',
    'migration-040',
    'migration-041',
    'migration-042',
    'migration-043',
    'migration-045',
    'migration-046',
    'migration-047',
    'migration-048',
    'migration-049',
    'migration-050',
    'migration-051',
    'migration-052',
    'migration-053',
    'migration-054',
    'cash-flow-display-order-compatibility',
    'identifier-semantics',
    'cash-flow-items',
    'expense-items',
    'expense-request-line-accrual-snapshot',
    'money-precision',
    'counterparties',
    'contracts',
    'budget-projection',
    'employee-functional-roles',
    'currencies-and-rates',
    'expense-request-currency-rate-snapshot',
    'expense-request-line-currency-conversion',
    'expense-request-urgency',
    'overpayment-deficit-storage',
    'treasury-overpayment-storage',
    'budget-owner-cost-center-assignments',
    'approval-route-snapshots',
    'employee-actor-mappings',
    'expense-request-workflow-activation',
    'accrual-closing-cycle-storage',
    'expense-request-closing-activation',
    'accrual-notifications',
    'expense-request-substitutions',
    'expense-request-task-scope-index',
    'expense-request-attachment-storage',
    'budget-instance-storage',
    'budget-instance-line-storage',
    'budget-directory-storage',
    'identity-reference-import-watermark-storage',
    'expense-request-numbering-storage',
];
const databaseReadinessMissingComponents: ReadonlySet<string> = new Set(databaseReadinessMissingComponentCodes);
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function isPublicUtcTimestamp(value: unknown): value is string {
    return isPublicExpenseRequestTimestamp(value)
        && (value.endsWith('Z') || value.endsWith('+00:00'));
}
function isNonNegativeSafeInteger(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}
function isDatabaseReadinessMissingComponent(value: unknown): value is DatabaseReadinessMissingComponent {
    return typeof value === 'string' && databaseReadinessMissingComponents.has(value);
}
function publicReferenceProjectionCount(value: unknown): ReferenceProjectionCount | null {
    if (!isRecord(value)
        || !isNonNegativeSafeInteger(value.totalCount)
        || !isNonNegativeSafeInteger(value.activeCount)
        || value.activeCount > value.totalCount) {
        return null;
    }
    return {
        totalCount: value.totalCount,
        activeCount: value.activeCount,
    };
}
export function publicDatabaseReadinessMonitor(payload: unknown): DatabaseReadinessMonitor {
    // Explicit legacy contract remains readable; unknown future counts fail closed.
    const expectedMigrationCount = isRecord(payload) && [41, 43, 44, 45, 46, 48, 49, 50, 51, 52].includes(payload.requiredMigrationCount as number)
        ? payload.requiredMigrationCount as number : databaseReadinessRequiredMigrationCount;
    if (!isRecord(payload)
        || typeof payload.databaseReady !== 'boolean'
        || typeof payload.appliedMigrationCount !== 'number'
        || !Number.isInteger(payload.appliedMigrationCount)
        || payload.appliedMigrationCount < 0
        || payload.appliedMigrationCount > expectedMigrationCount
        || payload.requiredMigrationCount !== expectedMigrationCount
        || !isPublicUtcTimestamp(payload.checkedAtUtc)
        || !Array.isArray(payload.missingComponents)
        || payload.missingComponents.length > databaseReadinessMissingComponents.size) {
        throw new Error(invalidReadinessResponseMessage);
    }
    const missingComponents: DatabaseReadinessMissingComponent[] = [];
    const seen = new Set<string>();
    for (const component of payload.missingComponents) {
        if (!isDatabaseReadinessMissingComponent(component)
            || seen.has(component)
            || (component === 'cash-flow-display-order-compatibility' && expectedMigrationCount < 50)
            || (component.startsWith('migration-') && (Number(component.slice(10)) === 44
                || Number(component.slice(10)) > (expectedMigrationCount >= 44 ? expectedMigrationCount + 1 : expectedMigrationCount)))) {
            throw new Error(invalidReadinessResponseMessage);
        }
        seen.add(component);
        missingComponents.push(component);
    }
    const missingMigrationCount = missingComponents.filter((component) => component.startsWith('migration-')).length;
    if (payload.appliedMigrationCount !== expectedMigrationCount - missingMigrationCount
        || payload.databaseReady !== (missingComponents.length === 0)) {
        throw new Error(invalidReadinessResponseMessage);
    }
    return {
        databaseReady: payload.databaseReady,
        appliedMigrationCount: payload.appliedMigrationCount,
        requiredMigrationCount: expectedMigrationCount,
        checkedAtUtc: payload.checkedAtUtc,
        missingComponents,
    };
}
export function publicReferenceProjectionInventory(payload: unknown): ReferenceProjectionInventory {
    if (!isRecord(payload)
        || !isPublicUtcTimestamp(payload.checkedAtUtc)
        || !isNonNegativeSafeInteger(payload.activeAssignmentsWithInactiveEmployeeCount)
        || !isNonNegativeSafeInteger(payload.activeAssignmentsWithInactiveFunctionalRoleCount)) {
        throw new Error(invalidProjectionResponseMessage);
    }
    const employees = publicReferenceProjectionCount(payload.employees);
    const functionalRoles = publicReferenceProjectionCount(payload.functionalRoles);
    const employeeFunctionalRoles = publicReferenceProjectionCount(payload.employeeFunctionalRoles);
    if (employees === null
        || functionalRoles === null
        || employeeFunctionalRoles === null
        || payload.activeAssignmentsWithInactiveEmployeeCount
            > employeeFunctionalRoles.activeCount
        || payload.activeAssignmentsWithInactiveFunctionalRoleCount
            > employeeFunctionalRoles.activeCount) {
        throw new Error(invalidProjectionResponseMessage);
    }
    return {
        checkedAtUtc: payload.checkedAtUtc,
        employees,
        functionalRoles,
        employeeFunctionalRoles,
        activeAssignmentsWithInactiveEmployeeCount: payload.activeAssignmentsWithInactiveEmployeeCount,
        activeAssignmentsWithInactiveFunctionalRoleCount: payload.activeAssignmentsWithInactiveFunctionalRoleCount,
    };
}
export function publicDatabaseReadinessMissingComponentsForMock(): DatabaseReadinessMissingComponent[] {
    return [...databaseReadinessMissingComponentCodes];
}
