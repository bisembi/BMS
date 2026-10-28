import type { ReferenceDirectoryId } from '../../navigation';
import type { ReferenceDataItem } from '../../types';
/// Pure text and matching helpers of the reference directory, moved out of
/// the page unchanged. No request and no state live here.
/// Fixed, safe wording. A raw response body or stack trace is never rendered.
export const loadErrorMessages: Record<ReferenceDirectoryId, string> = {
    organizations: 'Не удалось загрузить организации. Повторите попытку.',
    'cost-centers': 'Не удалось загрузить ЦФО. Повторите попытку.',
    'cash-flow-items': 'Не удалось загрузить статьи движения денежных средств. Повторите попытку.',
    'expense-items': 'Не удалось загрузить статьи затрат. Повторите попытку.',
    counterparties: 'Не удалось загрузить контрагентов. Повторите попытку.',
    contracts: 'Не удалось загрузить договоры. Повторите попытку.',
    currencies: 'Не удалось загрузить валюты. Повторите попытку.',
};
export const scopeErrorMessage = 'Не удалось загрузить организации и контрагентов. Повторите попытку.';
export const emptyMessages: Record<ReferenceDirectoryId, string> = {
    organizations: 'Организации не найдены.',
    'cost-centers': 'ЦФО не найдены.',
    'cash-flow-items': 'Статьи движения денежных средств не найдены.',
    'expense-items': 'Статьи затрат не найдены.',
    counterparties: 'Контрагенты не найдены.',
    contracts: 'Для выбранной организации и контрагента договоров нет.',
    currencies: 'Активные валюты не найдены.',
};
export function matchesQuery(item: ReferenceDataItem, query: string): boolean {
    if (query === '')
        return true;
    const needle = query.trim().toLocaleLowerCase('ru-RU');
    if (needle === '')
        return true;
    return item.code.toLocaleLowerCase('ru-RU').includes(needle)
        || item.name.toLocaleLowerCase('ru-RU').includes(needle)
        || (item.bin ?? '').includes(needle);
}
/// Renders the parent as a human-readable name. The parent identifier itself is
/// never written to the DOM.
export function parentLabel(item: ReferenceDataItem, byId: Map<string, ReferenceDataItem>, showsParentCode = true): string {
    if (!item.parentId)
        return '—';
    if (item.parentCode && item.parentName)
        return showsParentCode ? `${item.parentCode} · ${item.parentName}` : item.parentName;
    const exact = byId.get(item.parentId);
    const normalized = item.parentId.toLocaleLowerCase('en-US');
    const parent = exact ?? [...byId.entries()]
        .find(([key]) => key.toLocaleLowerCase('en-US') === normalized)?.[1];
    if (!parent)
        return 'Родитель не загружен';
    return showsParentCode ? `${parent.code} · ${parent.name}` : parent.name;
}
