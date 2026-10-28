import type { BusinessAuditMonitorDetail, BusinessAuditMonitorItem, BusinessAuditMonitorPage, ExpenseRequestHistoryChange, ExpenseRequestHistoryChangeKind, ExpenseRequestHistoryDetailsStatus } from '../types';
import { isPublicExpenseRequestText, isPublicExpenseRequestMultilineText, isPublicExpenseRequestTimestamp, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
import { isHistoryKind, isHistoryTitle } from './expenseRequestHistoryEventNames';
const invalidMonitorResponseMessage = 'Некорректный ответ API бизнес-аудита.';
const invalidSearchResponseMessage = 'Некорректный ответ API поиска бизнес-аудита.';
const invalidDetailResponseMessage = 'Некорректный ответ API детализации бизнес-аудита.';
const maximumInt32 = 2147483647;
const historyStatuses: ReadonlySet<string> = new Set([
    'complete',
    'partial',
    'unavailable',
]);
const changeKinds: ReadonlySet<string> = new Set([
    'added',
    'changed',
    'removed',
]);
const actorLabels: ReadonlySet<string> = new Set([
    'Инициатор',
    'Пользователь BMS',
]);
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function isHistoryDetailsStatus(value: unknown): value is ExpenseRequestHistoryDetailsStatus {
    return typeof value === 'string' && historyStatuses.has(value);
}
function isHistoryChangeKind(value: unknown): value is ExpenseRequestHistoryChangeKind {
    return typeof value === 'string' && changeKinds.has(value);
}
function isNonNegativeInt32(value: unknown): value is number {
    return typeof value === 'number'
        && Number.isSafeInteger(value)
        && value >= 0
        && value <= maximumInt32;
}
function publicBusinessAuditItem(value: unknown): BusinessAuditMonitorItem | null {
    if (!isRecord(value)
        || 'beforeJson' in value
        || 'afterJson' in value
        || !isPublicExpenseRequestUuid(value.auditId)
        || !isPublicExpenseRequestTimestamp(value.occurredAtUtc)
        || !isPublicExpenseRequestText(value.actorExternalId, 256)
        || !isPublicExpenseRequestText(value.objectType, 100)
        || !isPublicExpenseRequestText(value.objectId, 100)
        || !isPublicExpenseRequestText(value.action, 100)
        || !isPublicExpenseRequestText(value.correlationId, 64)
        || typeof value.hasBefore !== 'boolean'
        || typeof value.hasAfter !== 'boolean') {
        return null;
    }
    return {
        auditId: value.auditId,
        occurredAtUtc: value.occurredAtUtc,
        actorExternalId: value.actorExternalId,
        objectType: value.objectType,
        objectId: value.objectId,
        action: value.action,
        correlationId: value.correlationId,
        hasBefore: value.hasBefore,
        hasAfter: value.hasAfter,
    };
}
function publicBusinessAuditPage(payload: unknown, expectedSkip: number, expectedTake: number, invalidMessage: string): BusinessAuditMonitorPage {
    if (!isRecord(payload)
        || !Number.isSafeInteger(expectedSkip) || expectedSkip < 0 || expectedSkip > maximumInt32
        || !Number.isSafeInteger(expectedTake) || expectedTake < 1 || expectedTake > 100
        || payload.skip !== expectedSkip
        || payload.take !== expectedTake
        || typeof payload.hasMore !== 'boolean'
        || !Array.isArray(payload.items)
        || payload.items.length > expectedTake
        || (payload.hasMore && payload.items.length !== expectedTake)) {
        throw new Error(invalidMessage);
    }
    const items = payload.items.map(publicBusinessAuditItem);
    const publicItems = items.filter((item): item is BusinessAuditMonitorItem => item !== null);
    if (publicItems.length !== items.length)
        throw new Error(invalidMessage);
    if (new Set(publicItems.map((item) => item.auditId.toLowerCase())).size
        !== publicItems.length) {
        throw new Error(invalidMessage);
    }
    for (let index = 1; index < publicItems.length; index += 1) {
        if (Date.parse(publicItems[index - 1].occurredAtUtc)
            < Date.parse(publicItems[index].occurredAtUtc)) {
            throw new Error(invalidMessage);
        }
    }
    return {
        items: publicItems,
        skip: expectedSkip,
        take: expectedTake,
        hasMore: payload.hasMore,
    };
}
function publicChange(value: unknown): ExpenseRequestHistoryChange | null {
    if (!isRecord(value)
        || !isPublicExpenseRequestText(value.section, 200)
        || !isPublicExpenseRequestText(value.field, 300)
        || !isHistoryChangeKind(value.changeKind)
        || !(value.before === null || isPublicExpenseRequestMultilineText(value.before, 4000))
        || !(value.after === null || isPublicExpenseRequestMultilineText(value.after, 4000))) {
        return null;
    }
    const changeKind = value.changeKind;
    if ((changeKind === 'added' && (value.before !== null || value.after === null))
        || (changeKind === 'removed' && (value.before === null || value.after !== null))
        || (changeKind === 'changed' && (value.before === null || value.after === null))) {
        return null;
    }
    return {
        section: value.section,
        field: value.field,
        changeKind,
        before: value.before,
        after: value.after,
    };
}
export function publicBusinessAuditMonitorPage(payload: unknown, expectedSkip: number, expectedTake: number): BusinessAuditMonitorPage {
    return publicBusinessAuditPage(payload, expectedSkip, expectedTake, invalidMonitorResponseMessage);
}
export function publicBusinessAuditSearchPage(payload: unknown, expectedSkip: number, expectedTake: number): BusinessAuditMonitorPage {
    return publicBusinessAuditPage(payload, expectedSkip, expectedTake, invalidSearchResponseMessage);
}
export function publicBusinessAuditDetail(payload: unknown, expectedAuditId: string): BusinessAuditMonitorDetail {
    if (!isPublicExpenseRequestUuid(expectedAuditId)
        || !isRecord(payload)
        || 'beforeJson' in payload
        || 'afterJson' in payload
        || !isHistoryKind(payload.kind)
        || typeof payload.title !== 'string'
        || !isHistoryTitle(payload.kind, payload.title, true)
        || typeof payload.actorLabel !== 'string'
        || !actorLabels.has(payload.actorLabel)
        || !isHistoryDetailsStatus(payload.detailsStatus)
        || !Array.isArray(payload.changes)
        || payload.changes.length > 200
        || !isNonNegativeInt32(payload.omittedChanges)) {
        throw new Error(invalidDetailResponseMessage);
    }
    const metadata = publicBusinessAuditItem(payload.metadata);
    const changes = payload.changes.map(publicChange);
    const publicChanges = changes.filter((change): change is ExpenseRequestHistoryChange => change !== null);
    if (metadata === null
        || metadata.auditId.toLowerCase() !== expectedAuditId.toLowerCase()
        || publicChanges.length !== changes.length) {
        throw new Error(invalidDetailResponseMessage);
    }
    const kind = payload.kind;
    const detailsStatus = payload.detailsStatus;
    if ((detailsStatus === 'unavailable'
        && (changes.length !== 0 || payload.omittedChanges !== 0))
        || (detailsStatus === 'complete' && payload.omittedChanges !== 0)
        || (payload.omittedChanges > 0 && changes.length !== 200)
        || (payload.omittedChanges > 0 && detailsStatus !== 'partial')
        || (kind === 'unsupported' && detailsStatus !== 'unavailable')
        || (kind === 'unsupported'
            && payload.title !== 'Событие заявки'
            && payload.title !== 'Событие аудита')) {
        throw new Error(invalidDetailResponseMessage);
    }
    return {
        metadata,
        kind,
        title: payload.title,
        actorLabel: payload.actorLabel,
        detailsStatus,
        changes: publicChanges,
        omittedChanges: payload.omittedChanges,
    };
}
export { isPublicExpenseRequestUuid as isPublicBusinessAuditUuid };
