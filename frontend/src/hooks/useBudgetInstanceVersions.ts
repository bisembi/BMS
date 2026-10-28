import { useEffect, useState } from 'react';
import { listCompanyBudgetVersions, type CompanyBudgetVersionItem } from '../api/companyBudgetVersions';
/** Read the existing, authorized version directory. A failed read never widens
 * the selected filter or substitutes an implicit active version. */
export function useBudgetInstanceVersions(enabled: boolean, refresh: number) {
    const [items, setItems] = useState<CompanyBudgetVersionItem[]>([]);
    const [loading, setLoading] = useState(false), [error, setError] = useState(false), [retry, setRetry] = useState(0);
    useEffect(() => {
        const controller = new AbortController();
        if (!enabled) {
            setLoading(false);
            return () => controller.abort();
        }
        setLoading(true);
        setError(false);
        void (async () => {
            const result: CompanyBudgetVersionItem[] = [], ids = new Set<string>();
            let expectedTotal: number | undefined, activeStamp: string | undefined;
            for (let skip = 0;; skip += 100) {
                const page = await listCompanyBudgetVersions(skip, 100, controller.signal);
                if (controller.signal.aborted)
                    return;
                if (!Number.isSafeInteger(page.total) || page.total < 0 || page.total > 10000
                    || page.skip !== skip || page.take !== 100 || page.items.length !== Math.min(100, Math.max(0, page.total - skip))
                    || (expectedTotal !== undefined && page.total !== expectedTotal)
                    || (activeStamp !== undefined && page.activeConcurrencyStamp !== activeStamp))
                    throw new Error('Version catalogue changed.');
                expectedTotal = page.total;
                activeStamp = page.activeConcurrencyStamp;
                for (const item of page.items) {
                    const id = item.id.toLowerCase();
                    if (ids.has(id))
                        throw new Error('Duplicate version.');
                    ids.add(id);
                    result.push(item);
                }
                if (result.length === page.total)
                    return result;
            }
        })().then(result => {
            if (!controller.signal.aborted && result)
                setItems(result);
        })
            .catch(() => {
            if (!controller.signal.aborted)
                setError(true);
        })
            .finally(() => {
            if (!controller.signal.aborted)
                setLoading(false);
        });
        return () => controller.abort();
    }, [enabled, refresh, retry]);
    return { items, loading, error, retry: () => setRetry(value => value + 1) };
}
