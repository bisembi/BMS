import { queryCashFlowItems } from '../api';
import type { ReferenceDataItem } from '../types';
export type CashFlowItemPageSize = 50 | 100 | 500 | 'all';
/** A large UI page uses the existing bounded, server-filtered projection. */
export async function loadCashFlowItemCatalogueWindow(query: string, skip: number, pageSize: CashFlowItemPageSize, isCurrent: () => boolean): Promise<{
    items: ReferenceDataItem[];
    total: number;
}> {
    const firstTake = pageSize === 'all' ? 100 : Math.min(pageSize, 100);
    let batch = await queryCashFlowItems({ query, skip, take: firstTake });
    if (!isCurrent() || (pageSize !== 'all' && pageSize <= 100))
        return batch;
    const total = batch.total;
    const remaining = Math.max(0, total - skip);
    const length = pageSize === 'all' ? remaining : Math.min(pageSize, remaining);
    const items: ReferenceDataItem[] = [];
    const ids = new Set<string>();
    while (isCurrent()) {
        if (batch.total !== total || batch.items.length !== Math.min(batch.take, Math.max(0, total - batch.skip)))
            throw new Error('Каталог изменился во время чтения. Повторите попытку.');
        for (const item of batch.items) {
            const id = item.id.toLocaleLowerCase('en-US');
            if (ids.has(id))
                throw new Error('Получены повторяющиеся статьи. Повторите попытку.');
            ids.add(id);
            items.push(item);
        }
        if (items.length === length)
            return { items, total };
        if (items.length > length)
            throw new Error('Некорректный размер страницы.');
        if (!isCurrent())
            break;
        batch = await queryCashFlowItems({ query, skip: skip + items.length, take: Math.min(100, length - items.length) });
    }
    // The caller's request sequence discards this result. Do not start another GET.
    return { items: [], total };
}
