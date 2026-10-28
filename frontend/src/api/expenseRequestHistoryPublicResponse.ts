import type { ExpenseRequestHistoryChange, ExpenseRequestHistoryChangeKind, ExpenseRequestHistoryDetailsStatus, ExpenseRequestHistoryEvent, ExpenseRequestHistoryPage } from '../types';
import { isPublicExpenseRequestText, isPublicExpenseRequestMultilineText, isPublicExpenseRequestTimestamp } from './expenseRequestPublicResponse';
import { isHistoryKind, isHistoryTitle, isKnownHistoryTitle } from './expenseRequestHistoryEventNames';
const invalidHistoryResponseMessage = 'Некорректный ответ API истории заявки.';
const maximumInt32 = 2147483647;
const maximumChangesPerEvent = 200;
function rawJsonField(prefix: string): string {
    return `${prefix}Json`;
}
const rawBeforeField = rawJsonField('before');
const rawAfterField = rawJsonField('after');
const rawPayloadField = rawJsonField('payload');
const actorLabels = new Set([
    'Инициатор',
    'Пользователь BMS',
]);
function isHistoryActorLabel(value: unknown): value is string {
    if (typeof value !== 'string')
        return false;
    // Retain compatibility with older open clients, but accept the employee's
    // display name. Do not accept a SID, login, email, raw diagnostic or markup.
    return actorLabels.has(value) || (isPublicExpenseRequestText(value, 500)
        && /^[\p{L} .'’\-]+$/u.test(value)
        && value.split(' ').filter(Boolean).length >= 2
        && value.split(' ').filter(Boolean).every(part => /\p{L}/u.test(part)));
}
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
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function isHistoryDetailsStatus(value: unknown): value is ExpenseRequestHistoryDetailsStatus {
    return typeof value === 'string' && historyStatuses.has(value);
}
function isHistoryChangeKind(value: unknown): value is ExpenseRequestHistoryChangeKind {
    return typeof value === 'string' && changeKinds.has(value);
}
function hasExactKeys(value: Record<string, unknown>, expectedKeys: readonly string[]): boolean {
    const actualKeys = Object.keys(value);
    return actualKeys.length === expectedKeys.length
        && expectedKeys.every((key) => Object.prototype.hasOwnProperty.call(value, key));
}
function isNonNegativeInt32(value: unknown): value is number {
    return typeof value === 'number'
        && Number.isSafeInteger(value)
        && value >= 0
        && value <= maximumInt32;
}
function hasNoRawHistoryFields(value: Record<string, unknown>): boolean {
    return !(rawBeforeField in value)
        && !(rawAfterField in value)
        && !(rawPayloadField in value)
        && !('metadata' in value);
}
function publicHistoryChange(value: unknown): ExpenseRequestHistoryChange | null {
    if (!isRecord(value)
        || !hasExactKeys(value, ['section', 'field', 'changeKind', 'before', 'after'])
        || !hasNoRawHistoryFields(value)
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
function publicHistoryEvent(value: unknown): ExpenseRequestHistoryEvent | null {
    if (!isRecord(value)
        || !hasExactKeys(value, [
            'occurredAtUtc',
            'kind',
            'title',
            'actorLabel',
            'detailsStatus',
            'changes',
            'omittedChanges',
        ])
        || !hasNoRawHistoryFields(value)
        || !isPublicExpenseRequestTimestamp(value.occurredAtUtc)
        || typeof value.kind !== 'string'
        || !/^[a-z][a-z0-9_-]{0,63}$/u.test(value.kind)
        || ['constructor', 'prototype', 'tostring'].includes(value.kind)
        || !isPublicExpenseRequestText(value.title, 300)
        || !isHistoryActorLabel(value.actorLabel)
        || !isHistoryDetailsStatus(value.detailsStatus)
        || !Array.isArray(value.changes)
        || value.changes.length > maximumChangesPerEvent
        || !isNonNegativeInt32(value.omittedChanges)) {
        return null;
    }
    const changes = value.changes.map(publicHistoryChange);
    const publicChanges = changes.filter((change): change is ExpenseRequestHistoryChange => change !== null);
    if (publicChanges.length !== changes.length)
        return null;
    const kind = isHistoryKind(value.kind) ? value.kind : null;
    const knownEvent = kind !== null && isHistoryTitle(kind, value.title);
    const detailsStatus = value.detailsStatus;
    if ((detailsStatus === 'unavailable'
        && (publicChanges.length !== 0 || value.omittedChanges !== 0))
        || (detailsStatus === 'complete' && value.omittedChanges !== 0)
        || (value.omittedChanges > 0 && publicChanges.length !== maximumChangesPerEvent)
        || (value.omittedChanges > 0 && detailsStatus !== 'partial')
        || (knownEvent && kind === 'unsupported' && detailsStatus !== 'unavailable')
        || (!knownEvent && isKnownHistoryTitle(value.title))) {
        return null;
    }
    if (!knownEvent || kind === null) {
        // A well-shaped future event must not hide the rest of the journal. Keep
        // its position/date/validated author, but publish neither an unknown title
        // nor its details. Never accept extra/raw fields or malformed changes.
        return {
            occurredAtUtc: value.occurredAtUtc, kind: 'unsupported', title: 'Событие заявки',
            actorLabel: value.actorLabel, detailsStatus: 'unavailable', changes: [],
            omittedChanges: 0, requiresClientUpdate: true,
        };
    }
    return {
        occurredAtUtc: value.occurredAtUtc,
        kind,
        title: value.title,
        actorLabel: value.actorLabel,
        detailsStatus,
        changes: publicChanges,
        omittedChanges: value.omittedChanges,
    };
}
export function publicExpenseRequestHistoryPage(payload: unknown, expectedSkip: number, expectedTake: number): ExpenseRequestHistoryPage {
    if (!isRecord(payload)
        || !hasExactKeys(payload, ['items', 'skip', 'take', 'total'])
        || !Number.isSafeInteger(expectedSkip) || expectedSkip < 0
        || !Number.isSafeInteger(expectedTake) || expectedTake < 1 || expectedTake > 50
        || payload.skip !== expectedSkip
        || payload.take !== expectedTake
        || !isNonNegativeInt32(payload.total)
        || !Array.isArray(payload.items)
        || payload.items.length > expectedTake
        || payload.total < expectedSkip + payload.items.length) {
        throw new Error(invalidHistoryResponseMessage);
    }
    const items = payload.items.map(publicHistoryEvent);
    const publicItems = items.filter((item): item is ExpenseRequestHistoryEvent => item !== null);
    if (publicItems.length !== items.length) {
        throw new Error(invalidHistoryResponseMessage);
    }
    return {
        items: publicItems,
        skip: expectedSkip,
        take: expectedTake,
        total: payload.total,
    };
}
