import type { CurrentSession } from '../types';
import { isPublicExpenseRequestText, isPublicExpenseRequestUuid as uuid } from './expenseRequestPublicResponse';
import { isBmsRoleCode, profileForRole } from '../utils/activeRole';
const invalidSessionResponseMessage = 'Некорректный ответ API текущего сеанса.';
const maximumCsrfTokenLength = 1024;
const accessProfileCodes = ['BUSINESS', 'SYSTEM_ADMIN', 'INTEGRATION_ADMIN'] as const;
function isAccessProfileCode(value: unknown): value is CurrentSession['activeProfileCode'] {
    return typeof value === 'string'
        && accessProfileCodes.some((code) => code === value);
}
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
export function publicCurrentSession(payload: unknown): CurrentSession {
    if (!isRecord(payload)
        || !isPublicExpenseRequestText(payload.actorExternalId, 256)
        || (payload.actorDisplayName !== undefined
            && !isPublicExpenseRequestText(payload.actorDisplayName, 256))
        || !isAccessProfileCode(payload.activeProfileCode)
        || !Array.isArray(payload.availableProfileCodes)
        || payload.availableProfileCodes.length < 1
        || payload.availableProfileCodes.length > accessProfileCodes.length
        || !payload.availableProfileCodes.every(isAccessProfileCode)
        || new Set(payload.availableProfileCodes).size !== payload.availableProfileCodes.length
        || !payload.availableProfileCodes.includes(payload.activeProfileCode)
        || typeof payload.isBmsAdministrator !== 'boolean'
        || typeof payload.isBmsIntegrationAdministrator !== 'boolean'
        || typeof payload.canAccessBusinessData !== 'boolean'
        || typeof payload.hasFullBusinessDataAccess !== 'boolean'
        || (payload.canManageReferenceData !== undefined
            && typeof payload.canManageReferenceData !== 'boolean')
        || (payload.canReadBudgetReports !== undefined && typeof payload.canReadBudgetReports !== 'boolean')
        || (payload.canReadAllBusinessData !== undefined && typeof payload.canReadAllBusinessData !== 'boolean')
        || ((payload.canReadBudgetReports || payload.canReadAllBusinessData) && !payload.canAccessBusinessData)
        || (payload.canReadAllBusinessData === true && payload.canReadBudgetReports === false)
        || typeof payload.canCreateExpenseRequest !== 'boolean'
        || typeof payload.canViewBusinessAuditMonitor !== 'boolean'
        || payload.canAccessBusinessData !== !payload.isBmsIntegrationAdministrator
        || (payload.hasFullBusinessDataAccess && !payload.canAccessBusinessData)
        || (payload.isBmsAdministrator
            && payload.canAccessBusinessData
            && !Object.hasOwn(payload, 'availableRoleCodes')
            && !payload.hasFullBusinessDataAccess)
        || (payload.isBmsAdministrator && payload.canCreateExpenseRequest)
        || (payload.isBmsIntegrationAdministrator && payload.canCreateExpenseRequest)
        || (payload.canViewBusinessAuditMonitor && !payload.canAccessBusinessData)
        || ((payload.activeProfileCode === 'INTEGRATION_ADMIN') !== payload.isBmsIntegrationAdministrator)
        || (payload.isBmsIntegrationAdministrator
            ? payload.availableProfileCodes.length !== 1
                || payload.availableProfileCodes[0] !== 'INTEGRATION_ADMIN'
            : !payload.availableProfileCodes.includes('BUSINESS')
                || payload.availableProfileCodes.includes('INTEGRATION_ADMIN'))
        || ((payload.activeProfileCode === 'SYSTEM_ADMIN') !== payload.isBmsAdministrator
            && !payload.isBmsIntegrationAdministrator)
        || (payload.activeProfileCode === 'SYSTEM_ADMIN'
            && !payload.availableProfileCodes.includes('SYSTEM_ADMIN'))
        || (payload.isBmsAdministrator
            && !payload.isBmsIntegrationAdministrator
            && !payload.canViewBusinessAuditMonitor)) {
        throw new Error(invalidSessionResponseMessage);
    }
    // Absent means the server has the protection switched off, which is the
    // default and a normal state. Present means a real token: null, an empty
    // string, padding or a non-string is a broken contract and is refused here
    // rather than sent back to the server as a header.
    const csrfToken = payload.csrfToken;
    if (csrfToken !== undefined && !isPublicExpenseRequestText(csrfToken, maximumCsrfTokenLength)) {
        throw new Error(invalidSessionResponseMessage);
    }
    const hasRole = Object.hasOwn(payload, 'activeRoleCode');
    const hasChoices = Object.hasOwn(payload, 'availableRoleCodes');
    const profiles = payload.availableProfileCodes as CurrentSession['availableProfileCodes'];
    if (hasRole !== hasChoices || (hasRole && (!Array.isArray(payload.availableRoleCodes) || payload.availableRoleCodes.length > 13
        || !payload.availableRoleCodes.every(isBmsRoleCode)
        || new Set(payload.availableRoleCodes).size !== payload.availableRoleCodes.length
        || (payload.activeRoleCode === null
            ? payload.availableRoleCodes.some(role => role !== 'SYSTEM_ADMIN') || payload.canCreateExpenseRequest || payload.canManageReferenceData
                || payload.hasFullBusinessDataAccess || payload.canReadBudgetReports || payload.canViewBusinessAuditMonitor
            : !isBmsRoleCode(payload.activeRoleCode) || !payload.availableRoleCodes.includes(payload.activeRoleCode)
                || profileForRole(payload.activeRoleCode) !== payload.activeProfileCode)
        || payload.availableRoleCodes.some(role => !profiles.includes(profileForRole(role))))))
        throw new Error(invalidSessionResponseMessage);
    const staff = payload.staffSubstitutionRoles471;
    if (staff !== undefined) {
        if (!Array.isArray(staff) || staff.length > 128 || !Array.isArray(payload.ownRoleCodes471)
            || payload.ownRoleCodes471.length > 14 || !payload.ownRoleCodes471.every(isBmsRoleCode)
            || !payload.ownRoleCodes471.every(c => (payload.availableRoleCodes as unknown[]).includes(c))
            || new Set(payload.ownRoleCodes471).size !== payload.ownRoleCodes471.length
            || staff.some(d => !isRecord(d) || !uuid(d.documentId) || !isBmsRoleCode(d.roleCode)
                || profileForRole(d.roleCode) !== 'BUSINESS' || !isPublicExpenseRequestText(d.principalName, 256)
                || typeof d.from !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(d.from)
                || (d.through != null && (typeof d.through !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(d.through))))
            || new Set(staff.map(d => d.documentId)).size !== staff.length
            || (payload.activeSubstitutionId471 !== undefined && (!uuid(payload.activeSubstitutionId471)
                || !staff.some(d => d.documentId === payload.activeSubstitutionId471 && d.roleCode === payload.activeRoleCode))))
            throw new Error(invalidSessionResponseMessage);
    }
    else if (payload.activeSubstitutionId471 !== undefined || payload.ownRoleCodes471 !== undefined)
        throw new Error(invalidSessionResponseMessage);
    return {
        ...(staff === undefined ? {} : { staffSubstitutionRoles471: staff as CurrentSession['staffSubstitutionRoles471'],
            ownRoleCodes471: payload.ownRoleCodes471 as readonly string[],
            ...(payload.activeSubstitutionId471 === undefined ? {} : { activeSubstitutionId471: payload.activeSubstitutionId471 as string }) }),
        actorExternalId: payload.actorExternalId,
        ...(hasRole ? { activeRoleCode: payload.activeRoleCode as string | null, availableRoleCodes: payload.availableRoleCodes as string[] } : {}),
        ...(payload.actorDisplayName === undefined ? {} : { actorDisplayName: payload.actorDisplayName }),
        activeProfileCode: payload.activeProfileCode,
        availableProfileCodes: payload.availableProfileCodes,
        isBmsAdministrator: payload.isBmsAdministrator,
        isBmsIntegrationAdministrator: payload.isBmsIntegrationAdministrator,
        canAccessBusinessData: payload.canAccessBusinessData,
        hasFullBusinessDataAccess: payload.hasFullBusinessDataAccess,
        ...(payload.canReadBudgetReports === undefined ? {} : { canReadBudgetReports: payload.canReadBudgetReports }),
        ...(payload.canReadAllBusinessData === undefined ? {} : { canReadAllBusinessData: payload.canReadAllBusinessData }),
        ...(payload.canManageReferenceData === undefined
            ? {}
            : { canManageReferenceData: payload.canManageReferenceData }),
        canCreateExpenseRequest: payload.canCreateExpenseRequest,
        canViewBusinessAuditMonitor: payload.canViewBusinessAuditMonitor,
        ...(csrfToken === undefined ? {} : { csrfToken }),
    };
}
