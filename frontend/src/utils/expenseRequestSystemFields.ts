import type { SavedExpenseRequest } from '../types';
import { expenseRequestStatusLabel, isExpenseRequestStatus } from '../expenseRequestStatus';
export type ExpenseRequestSystemFields = {
    numberLabel?: string;
    createdDateLabel: string;
    initiatorLabel: string;
    statusLabel: string;
};
const createdDateFormatter = new Intl.DateTimeFormat('ru-RU', { timeZone: 'Etc/GMT-5', day: '2-digit', month: '2-digit', year: 'numeric' });
export function safeActorLabel(value: unknown): string {
    return typeof value === 'string'
        && value.length > 0
        && value.length <= 256
        && value.trim() === value
        && !/^S-1-/iu.test(value)
        && !/[\u0000-\u001f\u007f-\u009f]/u.test(value)
        ? value
        : 'Инициатор недоступен';
}
function safeCreatedDateLabel(value: unknown): string {
    if (typeof value !== 'string' || value.length === 0 || value.length > 64
        || /[\u0000-\u001f\u007f-\u009f]/u.test(value)) {
        return 'Дата недоступна';
    }
    const parsed = new Date(value);
    return Number.isNaN(parsed.valueOf()) ? 'Дата недоступна' : createdDateFormatter.format(parsed);
}
function safeStatusLabel(value: unknown): string {
    return isExpenseRequestStatus(value) ? expenseRequestStatusLabel(value) : 'Статус недоступен';
}
export function newExpenseRequestSystemFields(currentActorExternalId: string | null | undefined, currentActorDisplayName?: string | null): ExpenseRequestSystemFields {
    return {
        createdDateLabel: 'Будет определена при сохранении',
        initiatorLabel: safeActorLabel(currentActorDisplayName ?? currentActorExternalId),
        statusLabel: 'Черновик',
    };
}
export function loadingExpenseRequestSystemFields(): ExpenseRequestSystemFields {
    return {
        createdDateLabel: 'Загрузка…',
        initiatorLabel: 'Загрузка…',
        statusLabel: 'Загрузка…',
    };
}
export function unavailableExpenseRequestSystemFields(): ExpenseRequestSystemFields {
    return {
        createdDateLabel: 'Дата недоступна',
        initiatorLabel: 'Инициатор недоступен',
        statusLabel: 'Статус недоступен',
    };
}
export function expenseRequestSystemFieldsFromSaved(saved: SavedExpenseRequest, currentActorExternalId?: string | null, currentActorDisplayName?: string | null): ExpenseRequestSystemFields {
    const isCurrentActor = currentActorExternalId !== null
        && currentActorExternalId !== undefined
        && saved.initiatorExternalId === currentActorExternalId;
    return {
        ...(saved.requestNumber === undefined ? {} : { numberLabel: saved.requestNumber }),
        createdDateLabel: safeCreatedDateLabel(saved.createdAtUtc),
        initiatorLabel: saved.initiatorDisplayName !== undefined
            ? safeActorLabel(saved.initiatorDisplayName)
            : isCurrentActor && currentActorDisplayName
                ? safeActorLabel(currentActorDisplayName)
                : safeActorLabel(saved.initiatorExternalId),
        statusLabel: safeStatusLabel(saved.status),
    };
}
/** Command/replay DTOs deliberately retain their immutable identity-only shape.
 * Keep a GET-verified label only for the exact same document and initiator. */
export function createExpenseInitiatorPresentation() {
    let cached: {
        id: string;
        actor: string | undefined;
        name: string | null;
    } | null = null;
    return {
        clear() { cached = null; },
        fromSaved(saved: SavedExpenseRequest, actor?: string | null, displayName?: string | null) {
            if (saved.initiatorDisplayName !== undefined) {
                cached = { id: saved.id, actor: saved.initiatorExternalId, name: saved.initiatorDisplayName };
            }
            else if (cached && (cached.id !== saved.id || cached.actor !== saved.initiatorExternalId)) {
                cached = null;
            }
            const enriched = cached && saved.initiatorDisplayName === undefined
                ? { ...saved, initiatorDisplayName: cached.name } : saved;
            return expenseRequestSystemFieldsFromSaved(enriched, actor, displayName);
        },
    };
}
