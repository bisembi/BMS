import type { ExpenseRequestCurrentStage, ExpenseRequestListItem, ExpenseRequestPage } from '../types';
import { isExpenseRequestStatus } from '../expenseRequestStatus';
import { publicListBusinessColumns } from './expenseRequestListBusinessColumns';
import { maximumBusinessTripEmployees } from '../utils/expensePaymentOperation';
import { isPublicExpenseRequestDate, isPublicExpenseRequestMoney, isPublicExpenseRequestMultilineText, isPublicExpenseRequestText, isOptionalPublicExpenseRequestNumber, isPublicExpenseRequestTimestamp, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
const invalidListResponseMessage = 'Некорректный ответ API списка расходных заявок.';
const currencyPattern = /^[A-Z]{3}$/u;
const maximumInt32 = 2147483647;
const listItemFieldSet = 'canOpen|candidateCounterpartyName|counterpartyId|createdAtUtc|currencyCode|currentStage|defaultCostCenterId|id|initiatorExternalId|isNewCounterparty|isOverBudget|isUrgent|organizationId|plannedPaymentDate|purpose|status|totalAmount|totalAmountKzt';
const listItemFieldSetWithRequestNumber = [listItemFieldSet, 'requestNumber'].join('|').split('|').sort().join('|');
const pageFieldSet = 'items|skip|take|total';
const currentStages: readonly string[] = [
    'Расчетчик зарплаты',
    'Владелец ЦФО',
    'Финансовый контролёр',
    'Финансовый директор',
    'Генеральный директор',
    'Специалист НСИ',
    'Казначей',
];
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function isNullableUuid(value: unknown): value is string | null {
    return value === null || isPublicExpenseRequestUuid(value);
}
function isNullableText(value: unknown, maximumLength: number): value is string | null {
    return value === null || isPublicExpenseRequestText(value, maximumLength);
}
function isListMoney(value: unknown, exactColumns: boolean): value is number {
    return exactColumns ? isNumericMoneyCompanion(value) : isPublicExpenseRequestMoney(value);
}
function isNullableMoney(value: unknown, exactColumns: boolean): value is number | null {
    return value === null || isListMoney(value, exactColumns);
}
function isNullableCurrentStage(value: unknown): value is ExpenseRequestCurrentStage | null {
    return value === null
        || (typeof value === 'string'
            && currentStages.includes(value));
}
function isPageSkip(value: unknown): value is number {
    return typeof value === 'number'
        && Number.isSafeInteger(value)
        && value >= 0
        && value <= maximumInt32;
}
function isPageTake(value: unknown): value is number {
    return typeof value === 'number'
        && Number.isSafeInteger(value)
        && value >= 1
        && value <= 200;
}
function isPageTotal(value: unknown): value is number {
    return typeof value === 'number'
        && Number.isSafeInteger(value)
        && value >= 0
        && value <= maximumInt32;
}
function publicListItem(value: unknown, includeBusinessColumns: boolean): ExpenseRequestListItem | null {
    const exactColumns = includeBusinessColumns && isRecord(value) && value.canOpen === true;
    const hasRequestNumber = isRecord(value) && Object.prototype.hasOwnProperty.call(value, 'requestNumber');
    const hasName = isRecord(value) && Object.prototype.hasOwnProperty.call(value, 'initiatorDisplayName');
    const hasDate = isRecord(value) && Object.prototype.hasOwnProperty.call(value, 'requestDateUtc');
    const hasEmployees = isRecord(value) && Object.prototype.hasOwnProperty.call(value, 'employeeNames');
    const baseFieldSet = [(hasRequestNumber ? listItemFieldSetWithRequestNumber : listItemFieldSet), ...(hasName ? ['initiatorDisplayName'] : []), ...(hasDate ? ['requestDateUtc'] : []), ...(hasEmployees ? ['employeeNames'] : [])].join('|').split('|').sort().join('|');
    if (!isRecord(value)
        || Object.keys(value).sort().join('|') !== (includeBusinessColumns && value.canOpen === true
            ? `businessColumns|${baseFieldSet}` : baseFieldSet)
        || !isPublicExpenseRequestUuid(value.id)
        || !isExpenseRequestStatus(value.status)
        || !isPublicExpenseRequestText(value.initiatorExternalId, 256)
        || (hasName && value.initiatorDisplayName !== null && (!isPublicExpenseRequestText(value.initiatorDisplayName, 256)
            || value.initiatorDisplayName.length === 0 || value.initiatorDisplayName.trim() !== value.initiatorDisplayName || /^S-1-/iu.test(value.initiatorDisplayName)))
        || !isNullableUuid(value.organizationId)
        || !isNullableUuid(value.defaultCostCenterId)
        || !(value.plannedPaymentDate === null
            || isPublicExpenseRequestDate(value.plannedPaymentDate))
        || typeof value.currencyCode !== 'string'
        || !currencyPattern.test(value.currencyCode)
        || !isNullableUuid(value.counterpartyId)
        || !(value.purpose === null || isPublicExpenseRequestMultilineText(value.purpose, 1000))
        || typeof value.isUrgent !== 'boolean'
        || typeof value.isNewCounterparty !== 'boolean'
        || !isNullableText(value.candidateCounterpartyName, 300)
        || (hasEmployees && (value.canOpen !== true || value.counterpartyId !== null || value.isNewCounterparty !== false
            || !Array.isArray(value.employeeNames) || value.employeeNames.length > maximumBusinessTripEmployees
            || !value.employeeNames.every(name => isPublicExpenseRequestText(name, 150))))
        || !isListMoney(value.totalAmount, exactColumns)
        || !isNullableMoney(value.totalAmountKzt, exactColumns)
        || !isPublicExpenseRequestTimestamp(value.createdAtUtc)
        || (hasDate && !isPublicExpenseRequestTimestamp(value.requestDateUtc))
        || !isNullableCurrentStage(value.currentStage)
        || !(value.isOverBudget === null || typeof value.isOverBudget === 'boolean')
        || !isOptionalPublicExpenseRequestNumber(value.requestNumber)
        || typeof value.canOpen !== 'boolean') {
        return null;
    }
    const businessColumns = includeBusinessColumns && value.canOpen
        ? publicListBusinessColumns(value.businessColumns, value.totalAmount, value.totalAmountKzt) : undefined;
    if (businessColumns === null)
        return null;
    return {
        id: value.id,
        status: value.status,
        ...(value.requestNumber === undefined ? {} : { requestNumber: value.requestNumber }),
        initiatorExternalId: value.initiatorExternalId,
        ...(hasName ? { initiatorDisplayName: value.initiatorDisplayName as string | null } : {}),
        ...(hasEmployees ? { employeeNames: value.employeeNames as string[] } : {}),
        organizationId: value.organizationId,
        defaultCostCenterId: value.defaultCostCenterId,
        plannedPaymentDate: value.plannedPaymentDate,
        currencyCode: value.currencyCode,
        counterpartyId: value.counterpartyId,
        purpose: value.purpose,
        isUrgent: value.isUrgent,
        isNewCounterparty: value.isNewCounterparty,
        candidateCounterpartyName: value.candidateCounterpartyName,
        totalAmount: value.totalAmount,
        totalAmountKzt: value.totalAmountKzt,
        createdAtUtc: value.createdAtUtc,
        ...(hasDate ? { requestDateUtc: value.requestDateUtc as string } : {}),
        currentStage: value.currentStage,
        isOverBudget: value.isOverBudget,
        canOpen: value.canOpen,
        ...(businessColumns === undefined ? {} : { businessColumns }),
    };
}
// In the opt-in contract the canonical decimal strings are authoritative and
// rendered directly. Approximate legacy JSON companions must agree with them;
// they are never used to format these rows or to check the cent invariant.
function isNumericMoneyCompanion(value: unknown): value is number {
    return typeof value === 'number' && Number.isFinite(value) && value >= 0;
}
export function publicExpenseRequestPage(payload: unknown, includeBusinessColumns = false): ExpenseRequestPage {
    const unavailable = isRecord(payload) && Object.hasOwn(payload, 'budgetIndicatorsUnavailable');
    const expectedFields = [pageFieldSet, ...(includeBusinessColumns ? ['businessColumnsIncluded'] : []),
        ...(unavailable ? ['budgetIndicatorsUnavailable'] : [])].join('|').split('|').sort().join('|');
    if (!isRecord(payload)
        || Object.keys(payload).sort().join('|') !== expectedFields
        || (unavailable && payload.budgetIndicatorsUnavailable !== true)
        || (includeBusinessColumns && payload.businessColumnsIncluded !== true)
        || !isPageSkip(payload.skip)
        || !isPageTake(payload.take)
        || !isPageTotal(payload.total)
        || !Array.isArray(payload.items)
        || payload.items.length > payload.take) {
        throw new Error(invalidListResponseMessage);
    }
    const items = payload.items.map((item) => publicListItem(item, includeBusinessColumns));
    const publicItems = items.filter((item): item is ExpenseRequestListItem => item !== null);
    if (publicItems.length !== items.length)
        throw new Error(invalidListResponseMessage);
    if (unavailable && publicItems.some(item => item.isOverBudget !== null
        && item.status !== 'PAID' && item.status !== 'CLOSED'))
        throw new Error(invalidListResponseMessage);
    const uniqueIds = new Set(publicItems.map((item) => item.id.toLocaleLowerCase('en-US')));
    if (uniqueIds.size !== publicItems.length
        || (publicItems.length > 0
            && payload.total < payload.skip + publicItems.length)) {
        throw new Error(invalidListResponseMessage);
    }
    return {
        items: publicItems,
        skip: payload.skip,
        take: payload.take,
        total: payload.total,
        ...(includeBusinessColumns ? { businessColumnsIncluded: true as const } : {}),
        ...(unavailable ? { budgetIndicatorsUnavailable: true as const } : {}),
    };
}
