import type { ReferenceDataItem } from '../types';
export const missingDirectoryValue = '__unassigned__';
export const directoryGroupKey = (item: ReferenceDataItem) => item.parentName?.trim().toLocaleUpperCase('ru-RU') || missingDirectoryValue;
export function filterCashFlowDirectory(items: readonly ReferenceDataItem[], query: string, center: string, group: string) {
    const term = query.trim().toLocaleLowerCase('ru-RU');
    return items.filter(item => (!term || `${item.code ?? ''} ${item.name}`.toLocaleLowerCase('ru-RU').includes(term))
        && (!center || (item.costCenterId?.toLowerCase() || missingDirectoryValue) === center.toLowerCase())
        && (!group || directoryGroupKey(item) === group));
}
