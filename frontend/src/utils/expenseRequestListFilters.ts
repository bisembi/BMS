import type { ExpenseRequestStatus, ReferenceDataItem } from '../types';
import type { ExpenseRequestInitiatorOption } from '../api/expenseRequestInitiators';
import { utcDayAfter, utcDayStart } from './expenseRequestListFormat';
import { invalidExpenseRequestDateMessage, isCanonicalExpenseRequestDate } from './expenseRequestListDate';
export type ExpenseRequestUrgencyFilter = 'all' | 'urgent' | 'normal';
export type ExpenseRequestStatusFilter = '' | ExpenseRequestStatus;
/// Immutable value object. The page owns two independent instances: editable
/// draft values and the applied values used by list, paging, refresh and Excel.
export type ExpenseRequestListFilterState = {
    readonly search?: string;
    readonly createdFromDate: string;
    readonly createdThroughDate: string;
    readonly urgency: ExpenseRequestUrgencyFilter;
    readonly organizationToken: string;
    readonly costCenterToken: string;
    readonly counterpartyToken: string;
    readonly initiator: string;
    readonly onlyMyTasks: boolean;
    readonly status: ExpenseRequestStatusFilter;
    readonly plannedPaymentDate: string;
};
export const emptyExpenseRequestListFilters: ExpenseRequestListFilterState = Object.freeze({
    search: '',
    createdFromDate: '',
    createdThroughDate: '',
    urgency: 'all',
    organizationToken: '',
    costCenterToken: '',
    counterpartyToken: '',
    initiator: '',
    onlyMyTasks: false,
    status: '',
    plannedPaymentDate: '',
});
export const invalidCreatedPeriodMessage = 'Дата начала периода не может быть позже даты окончания.';
export type ExpenseRequestListCatalogs = {
    readonly initiators?: readonly ExpenseRequestInitiatorOption[];
    readonly organizations: readonly ReferenceDataItem[];
    readonly costCenters: readonly ReferenceDataItem[];
    readonly counterparties: readonly ReferenceDataItem[];
};
export type ExpenseRequestListQuery = {
    readonly initiatorEmployeeId?: string;
    readonly search?: string;
    readonly isUrgent: boolean | undefined;
    readonly plannedPaymentDate: string | undefined;
    readonly organizationId: string | undefined;
    readonly status: ExpenseRequestStatus | undefined;
    readonly costCenterId: string | undefined;
    readonly counterpartyId: string | undefined;
    readonly initiatorExternalId: string | undefined;
    readonly onlyMyTasks: boolean | undefined;
    readonly createdFromUtc: string | undefined;
    readonly createdBeforeUtc: string | undefined;
};
const uuidLike = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/iu;
function stableHash(value: string): string {
    let hash = 2166136261;
    for (let index = 0; index < value.length; index += 1) {
        hash ^= value.charCodeAt(index);
        hash = Math.imul(hash, 16777619);
    }
    return (hash >>> 0).toString(36);
}
export function initiatorToken(employee: ExpenseRequestInitiatorOption): string {
    return `e${stableHash(employee.id.toLowerCase())}`;
}
export function resolveInitiatorId(token: string, employees: readonly ExpenseRequestInitiatorOption[]): string | undefined {
    if (token === '')
        return undefined;
    const matches = employees.filter(employee => initiatorToken(employee) === token);
    return matches.length === 1 ? matches[0].id : undefined;
}
/// Stable DOM value derived from public display data, never from the technical
/// identifier. UUID-like codes/names are reduced to an opaque one-way token.
export function referenceToken(item: ReferenceDataItem): string {
    const code = item.code.trim();
    const publicKey = code !== '' ? code : item.name.trim();
    if (publicKey === '' || uuidLike.test(publicKey)) {
        return `h${stableHash(`${item.code}|${item.name}`)}`;
    }
    return `k${publicKey}`;
}
/// Fail closed on stale, unknown or colliding tokens: a technical identifier is
/// returned only when the current in-memory catalogue has exactly one match.
export function resolveReferenceId(token: string, items: readonly ReferenceDataItem[]): string | undefined {
    if (token === '')
        return undefined;
    const matches = items.filter((item) => referenceToken(item) === token);
    return matches.length === 1 ? matches[0].id : undefined;
}
export function validateExpenseRequestListFilters(filters: ExpenseRequestListFilterState): string | null {
    if ((filters.search?.length ?? 0) > 200 || /[\u0000-\u001f\u007f]/u.test(filters.search ?? ''))
        return 'Поиск по номеру и назначению: не более 200 символов, без служебных знаков.';
    if ((filters.createdFromDate !== '' && !isCanonicalExpenseRequestDate(filters.createdFromDate))
        || (filters.createdThroughDate !== '' && !isCanonicalExpenseRequestDate(filters.createdThroughDate))
        || (filters.plannedPaymentDate !== '' && !isCanonicalExpenseRequestDate(filters.plannedPaymentDate))) {
        return invalidExpenseRequestDateMessage;
    }
    if (filters.createdFromDate !== ''
        && filters.createdThroughDate !== ''
        && filters.createdFromDate > filters.createdThroughDate) {
        return invalidCreatedPeriodMessage;
    }
    return null;
}
export function countActiveExpenseRequestListFilters(filters: ExpenseRequestListFilterState): number {
    let active = 0;
    if (filters.search?.trim())
        active += 1;
    if (filters.createdFromDate !== '')
        active += 1;
    if (filters.createdThroughDate !== '')
        active += 1;
    if (filters.urgency !== 'all')
        active += 1;
    if (filters.organizationToken !== '')
        active += 1;
    if (filters.costCenterToken !== '')
        active += 1;
    if (filters.counterpartyToken !== '')
        active += 1;
    if (filters.initiator !== '')
        active += 1;
    if (filters.onlyMyTasks)
        active += 1;
    if (filters.status !== '')
        active += 1;
    if (filters.plannedPaymentDate !== '')
        active += 1;
    return active;
}
export function toExpenseRequestListQuery(filters: ExpenseRequestListFilterState, catalogs: ExpenseRequestListCatalogs): ExpenseRequestListQuery {
    const validationMessage = validateExpenseRequestListFilters(filters);
    if (validationMessage !== null)
        throw new Error(validationMessage);
    const employeeId = catalogs.initiators === undefined ? undefined : resolveInitiatorId(filters.initiator, catalogs.initiators);
    if (catalogs.initiators !== undefined && filters.initiator && !employeeId)
        throw new Error('Выберите инициатора из списка сотрудников.');
    return {
        ...(filters.search?.trim() ? { search: filters.search.trim() } : {}),
        isUrgent: filters.urgency === 'all' ? undefined : filters.urgency === 'urgent',
        plannedPaymentDate: filters.plannedPaymentDate || undefined,
        organizationId: resolveReferenceId(filters.organizationToken, catalogs.organizations),
        status: filters.status || undefined,
        costCenterId: resolveReferenceId(filters.costCenterToken, catalogs.costCenters),
        counterpartyId: resolveReferenceId(filters.counterpartyToken, catalogs.counterparties),
        initiatorExternalId: catalogs.initiators === undefined ? filters.initiator || undefined : undefined,
        ...(catalogs.initiators === undefined || !filters.initiator ? {} : {
            initiatorEmployeeId: employeeId,
        }),
        onlyMyTasks: filters.onlyMyTasks || undefined,
        createdFromUtc: utcDayStart(filters.createdFromDate),
        createdBeforeUtc: utcDayAfter(filters.createdThroughDate),
    };
}
/// Preserve the established API arity: period arguments exist only when at
/// least one UTC boundary is present.
export function createdPeriodArguments(query: ExpenseRequestListQuery): [
] | [
    string | undefined,
    string | undefined
] {
    return query.createdFromUtc == null && query.createdBeforeUtc == null
        ? []
        : [query.createdFromUtc, query.createdBeforeUtc];
}
