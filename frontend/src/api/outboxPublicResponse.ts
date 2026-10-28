import type { OutboxBacklogSummary, OutboxMonitorItem, OutboxMonitorPage } from '../types';
import { isPublicExpenseRequestText, isPublicExpenseRequestTimestamp, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
const invalidMonitorResponseMessage = 'Некорректный ответ API сервисного монитора.';
const invalidSummaryResponseMessage = 'Некорректный ответ API сводки Outbox.';
const maximumInt32 = 2147483647;
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function isNonNegativeSafeInteger(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}
function publicOutboxItem(value: unknown): OutboxMonitorItem | null {
    if (!isRecord(value)
        || !isPublicExpenseRequestUuid(value.messageId)
        || !isPublicExpenseRequestTimestamp(value.occurredAtUtc)
        || !isPublicExpenseRequestText(value.messageType, 200)
        || !(value.processedAtUtc === null
            || isPublicExpenseRequestTimestamp(value.processedAtUtc))
        || !isNonNegativeSafeInteger(value.retryCount)
        || value.retryCount > maximumInt32
        || typeof value.hasLastError !== 'boolean') {
        return null;
    }
    return {
        messageId: value.messageId,
        occurredAtUtc: value.occurredAtUtc,
        messageType: value.messageType,
        processedAtUtc: value.processedAtUtc,
        retryCount: value.retryCount,
        hasLastError: value.hasLastError,
    };
}
export function publicOutboxMonitorPage(payload: unknown, expectedSkip: number, expectedTake: number): OutboxMonitorPage {
    if (!isRecord(payload)
        || !Number.isSafeInteger(expectedSkip) || expectedSkip < 0 || expectedSkip > maximumInt32
        || !Number.isSafeInteger(expectedTake) || expectedTake < 1 || expectedTake > 100
        || payload.skip !== expectedSkip
        || payload.take !== expectedTake
        || typeof payload.hasMore !== 'boolean'
        || !Array.isArray(payload.items)
        || payload.items.length > expectedTake
        || (payload.hasMore && payload.items.length !== expectedTake)) {
        throw new Error(invalidMonitorResponseMessage);
    }
    const items = payload.items.map(publicOutboxItem);
    const publicItems = items.filter((item): item is OutboxMonitorItem => item !== null);
    if (publicItems.length !== items.length)
        throw new Error(invalidMonitorResponseMessage);
    if (new Set(publicItems.map((item) => item.messageId.toLocaleLowerCase('en-US'))).size !== publicItems.length) {
        throw new Error(invalidMonitorResponseMessage);
    }
    for (let index = 1; index < publicItems.length; index += 1) {
        if (Date.parse(publicItems[index - 1].occurredAtUtc)
            < Date.parse(publicItems[index].occurredAtUtc)) {
            throw new Error(invalidMonitorResponseMessage);
        }
    }
    return {
        items: publicItems,
        skip: expectedSkip,
        take: expectedTake,
        hasMore: payload.hasMore,
    };
}
export function publicOutboxBacklogSummary(payload: unknown): OutboxBacklogSummary {
    if (!isRecord(payload)
        || !isPublicExpenseRequestTimestamp(payload.checkedAtUtc)
        || !isNonNegativeSafeInteger(payload.totalCount)
        || !isNonNegativeSafeInteger(payload.pendingCount)
        || !isNonNegativeSafeInteger(payload.processedCount)
        || !isNonNegativeSafeInteger(payload.retriedPendingCount)
        || !isNonNegativeSafeInteger(payload.pendingWithLastErrorCount)
        || !isNonNegativeSafeInteger(payload.maxPendingRetryCount)
        || payload.maxPendingRetryCount > maximumInt32
        || payload.pendingCount > payload.totalCount
        || payload.processedCount !== payload.totalCount - payload.pendingCount
        || payload.retriedPendingCount > payload.pendingCount
        || payload.pendingWithLastErrorCount > payload.pendingCount
        || (payload.retriedPendingCount === 0) !== (payload.maxPendingRetryCount === 0)
        || !(payload.oldestPendingOccurredAtUtc === null
            || isPublicExpenseRequestTimestamp(payload.oldestPendingOccurredAtUtc))) {
        throw new Error(invalidSummaryResponseMessage);
    }
    if ((payload.pendingCount === 0 && payload.oldestPendingOccurredAtUtc !== null)
        || (payload.pendingCount > 0 && payload.oldestPendingOccurredAtUtc === null)) {
        throw new Error(invalidSummaryResponseMessage);
    }
    return {
        checkedAtUtc: payload.checkedAtUtc,
        totalCount: payload.totalCount,
        pendingCount: payload.pendingCount,
        processedCount: payload.processedCount,
        retriedPendingCount: payload.retriedPendingCount,
        pendingWithLastErrorCount: payload.pendingWithLastErrorCount,
        maxPendingRetryCount: payload.maxPendingRetryCount,
        oldestPendingOccurredAtUtc: payload.oldestPendingOccurredAtUtc,
    };
}
