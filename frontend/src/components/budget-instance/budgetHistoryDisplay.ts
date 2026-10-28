import { BudgetHistoryError, getBudgetHistory, type BudgetHistoryEvent, type BudgetHistoryPage } from '../../api/budgetInstanceHistory';
export const historyPageSize = 20;
export const compactHistoryEventLimit = 1000;
export type HistoryDisplayGroup = {
    firstId: string;
    event: BudgetHistoryEvent;
    count: number;
};
export type HistoryDisplaySnapshot = {
    mode: 'compact' | 'raw';
    page: BudgetHistoryPage;
};
export function groupHistoryEvents(events: readonly BudgetHistoryEvent[]): HistoryDisplayGroup[] {
    const groups: HistoryDisplayGroup[] = [];
    for (const event of events) {
        const previous = groups.at(-1);
        const a = previous?.event;
        if (a && a.actorKey && a.actorKey === event.actorKey && a.eventCode !== 'other'
            && a.eventCode === event.eventCode && a.actorName !== 'Автор не определён' && a.actorName === event.actorName
            && a.roleName === event.roleName && a.comment === event.comment && a.detailsAvailable === event.detailsAvailable) {
            groups[groups.length - 1] = { firstId: previous.firstId, event, count: previous.count + 1 };
        }
        else
            groups.push({ firstId: event.id, event, count: 1 });
    }
    return groups;
}
/** Bounded complete read before grouping: a page boundary is not the end of a run. */
export async function loadHistoryDisplay(id: string, signal: AbortSignal): Promise<HistoryDisplaySnapshot> {
    const abort = new AbortController();
    const cancel = () => abort.abort();
    signal.addEventListener('abort', cancel, { once: true });
    if (signal.aborted)
        cancel();
    const deadline = setTimeout(cancel, 30000);
    try {
        abort.signal.throwIfAborted();
        const first = await getBudgetHistory(id, 0, historyPageSize, abort.signal);
        abort.signal.throwIfAborted();
        if (first.total > compactHistoryEventLimit)
            return { mode: 'raw', page: first };
        const all: BudgetHistoryEvent[] = [];
        const ids = new Set<string>();
        let previousTime = -Infinity;
        let page = first;
        for (let skip = 0;; skip += historyPageSize) {
            if (page.budgetInstanceId !== id || page.skip !== skip || page.take !== historyPageSize || page.total !== first.total
                || page.items.length !== Math.min(historyPageSize, Math.max(0, first.total - skip)))
                throw new BudgetHistoryError();
            for (const event of page.items) {
                const time = Date.parse(event.occurredAtUtc);
                if (ids.has(event.id.toLowerCase()) || !Number.isFinite(time) || time < previousTime)
                    throw new BudgetHistoryError();
                ids.add(event.id.toLowerCase());
                previousTime = time;
                all.push(event);
            }
            if (all.length === first.total)
                break;
            abort.signal.throwIfAborted();
            page = await getBudgetHistory(id, skip + historyPageSize, historyPageSize, abort.signal);
            abort.signal.throwIfAborted();
        }
        return { mode: 'compact', page: { ...first, items: all } };
    }
    finally {
        clearTimeout(deadline);
        signal.removeEventListener('abort', cancel);
    }
}
