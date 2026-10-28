import { useEffect, useState } from 'react';
import { queryCashFlowItems } from '../api/referenceData';
import { sameOpaqueIdentifier } from '../utils/expenseRequestDraft';
import { useWorkspaceRefreshRevision } from '../components/WorkspaceRefreshContext';
import type { ReferenceDataItem } from '../types';
// The compact full catalogue intentionally has no accrual settings. Read the
// reviewed directory window for selected articles only; never infer the flag
// from the article's name or treat a missing flag as a disabled accrual.
export function useExpenseAccrualSettings(selectedIds: readonly string[], references: readonly ReferenceDataItem[]) {
    const refresh = useWorkspaceRefreshRevision();
    const targets = [...new Set(selectedIds.filter(Boolean).map(id => id.toLowerCase()))].sort().map(id => {
        const reference = references.find(item => sameOpaqueIdentifier(item.id, id));
        return { id, code: reference?.code ?? '', known: reference?.formsManagerialAccrual };
    });
    const key = JSON.stringify([refresh, targets]);
    const [state, setState] = useState<{
        key: string;
        items: ReferenceDataItem[];
        loading: boolean;
        error: string | null;
    }>({ key: '', items: [], loading: false, error: null });
    useEffect(() => {
        let cancelled = false;
        const controller = new AbortController();
        const parsed = JSON.parse(key)[1] as typeof targets;
        if (parsed.length === 0) {
            setState({ key, items: [], loading: false, error: null });
            return;
        }
        setState({ key, items: [], loading: true, error: null });
        const items = new Array<ReferenceDataItem>(parsed.length);
        let next = 0;
        const readTarget = async (target: typeof parsed[number]) => {
            if (typeof target.known === 'boolean')
                return references.find(item => sameOpaqueIdentifier(item.id, target.id))!;
            if (!target.code)
                throw new Error('Missing article');
            const page = await queryCashFlowItems({ query: target.code, skip: 0, take: 100 }, controller.signal);
            const item = page.items.find(item => sameOpaqueIdentifier(item.id, target.id));
            if (!item || typeof item.formsManagerialAccrual !== 'boolean')
                throw new Error('Missing accrual flag');
            return item;
        };
        const worker = async () => {
            while (next < parsed.length && !controller.signal.aborted) {
                const index = next++;
                items[index] = await readTarget(parsed[index]);
            }
        };
        void Promise.all(Array.from({ length: Math.min(4, parsed.length) }, worker))
            .then(() => {
            if (!cancelled)
                setState({ key, items, loading: false, error: null });
        })
            .catch(() => {
            if (!cancelled) {
                controller.abort();
                setState({ key, items: [], loading: false, error: 'Не удалось проверить настройки акрула выбранной статьи. Обновите справочники.' });
            }
        });
        return () => { cancelled = true; controller.abort(); };
    }, [key]);
    return state.key === key ? state : { key, items: [], loading: targets.length > 0, error: null };
}
