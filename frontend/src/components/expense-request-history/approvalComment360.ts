import type { ExpenseRequestHistoryEvent } from '../../types';
export function approvalComment360(event: ExpenseRequestHistoryEvent): string | null {
    if (event.kind !== 'workflow' || event.requiresClientUpdate || event.detailsStatus === 'unavailable')
        return null;
    const text = event.changes.find(change => change.section === 'Согласование'
        && change.field === 'Причина' && change.changeKind === 'added')?.after;
    return typeof text === 'string' && text.trim() !== '' ? text.trim() : null;
}
