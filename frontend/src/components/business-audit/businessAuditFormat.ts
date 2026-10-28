import { formatBusinessTimestamp } from '../../utils/businessTime';
/// Presentation constants of the business-audit monitor, moved out of the page
/// unchanged. No request and no state live here.
export const pageSize = 25;
export const changeKindLabels = {
    added: 'Добавлено',
    changed: 'Изменено',
    removed: 'Удалено',
} as const;
export const detailsStatusLabels = {
    complete: 'Полная детализация',
    partial: 'Частичная детализация',
    unavailable: 'Детализация недоступна',
} as const;
export type FilterDraft = {
    actorExternalId: string;
    objectType: string;
    objectId: string;
    action: string;
    correlationId: string;
    fromUtc: string;
    toUtc: string;
};
export const emptyFilterDraft: FilterDraft = {
    actorExternalId: '',
    objectType: '',
    objectId: '',
    action: '',
    correlationId: '',
    fromUtc: '',
    toUtc: '',
};
export function formatUtc(value: string): string {
    return `${formatBusinessTimestamp(value)} UTC+5`;
}
