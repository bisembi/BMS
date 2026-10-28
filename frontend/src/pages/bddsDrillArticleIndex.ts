import type { BddsReportRow } from '../api/bddsReport';
// Build membership once per report, retaining report order and the previous
// exact-ID Set semantics. All month/total fact cells share this same index.
export function bddsDrillArticleIndex(rows: readonly BddsReportRow[]): ReadonlyMap<string, readonly string[]> {
    const byKey = new Map(rows.map(row => [row.key, row]));
    const groups = new Map<string, Set<string>>();
    const all = new Set<string>();
    for (const article of rows) {
        if (article.kind !== 'Article' || !article.cashFlowItemId)
            continue;
        all.add(article.cashFlowItemId);
        let current: BddsReportRow | undefined = article;
        const seen = new Set<string>();
        while (current && !seen.has(current.key)) {
            const ids = groups.get(current.key) ?? new Set<string>();
            ids.add(article.cashFlowItemId);
            groups.set(current.key, ids);
            seen.add(current.key);
            current = byKey.get(current.parentKey ?? '');
        }
    }
    for (const row of rows)
        if (row.kind === 'Net')
            groups.set(row.key, all);
    return new Map([...groups].map(([key, ids]) => [key, [...ids]]));
}
