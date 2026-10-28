import type { BddsReport, BddsReportRow } from '../api/bddsReport';
import { catalogueRank, minimumCatalogueRank } from '../utils/catalogueDisplayOrder';
// Read-only presentation policy verified against VM212's hierarchy CSV
// 80CFB646B090F4EB2CAB193A9482384EE97F16A507BEFF156461C84E728B1B00.
// Neither permanent article IDs, source classifications, nor money are written.
const label = (name: string) => name.normalize('NFC').trim().replace(/\s+/g, ' ').toUpperCase();
const minor = (value: number) => {
    const result = Math.round(value * 100);
    if (!Number.isFinite(value) || !Number.isSafeInteger(result))
        throw new Error('invalid-bdds-projection');
    return result;
};
const sum = (values: number[]) => {
    const result = values.reduce((a, b) => a + BigInt(minor(b)), 0n);
    if (result > BigInt(Number.MAX_SAFE_INTEGER) || result < -BigInt(Number.MAX_SAFE_INTEGER))
        throw new Error('invalid-bdds-projection');
    const amount = Number(result) / 100;
    if (minor(amount) !== Number(result))
        throw new Error('invalid-bdds-projection');
    return amount;
};
const percentage = (delta: number, basis: number) => {
    const numerator = BigInt(Math.abs(minor(delta))) * 1000n;
    const denominator = BigInt(Math.abs(minor(basis)));
    if (denominator === 0n)
        return null;
    const tenths = (numerator * 2n + denominator) / (denominator * 2n);
    return tenths <= BigInt(Number.MAX_SAFE_INTEGER) ? Math.sign(delta) * Number(tenths) / 10 : null;
};
function aggregate(head: BddsReportRow, parts: BddsReportRow[]): BddsReportRow {
    const n = head.amounts.length;
    const comparing = head.compareAmounts !== undefined;
    const fact = head.actualAmounts !== undefined;
    if (parts.some(p => p.amounts.length !== n || (comparing ? p.compareAmounts?.length !== n : p.compareAmounts !== undefined)
        || (fact ? p.actualAmounts?.length !== n : p.actualAmounts !== undefined)))
        throw new Error('invalid-bdds-projection');
    const amounts = Array.from({ length: n }, (_, i) => sum(parts.map(p => p.amounts[i])));
    const total = sum(amounts);
    const result: BddsReportRow = { ...head, amounts, total, costCenterCode: null };
    if (comparing) {
        const compareAmounts = amounts.map((_, i) => sum(parts.map(p => p.compareAmounts![i])));
        const compareTotal = sum(compareAmounts);
        const deltas = compareAmounts.map((v, i) => sum([v, -amounts[i]]));
        Object.assign(result, { compareAmounts, compareTotal, versionVarianceAmounts: deltas,
            versionVarianceTotal: sum([compareTotal, -total]), versionVariancePercentages: deltas.map((v, i) => percentage(v, amounts[i])),
            versionVariancePercentageTotal: percentage(sum([compareTotal, -total]), total) });
    }
    if (fact) {
        const actualAmounts = amounts.map((_, i) => {
            const values = parts.map(p => p.actualAmounts![i]).filter((v): v is number => v !== null);
            return values.length ? sum(values) : null;
        });
        const knownActuals = actualAmounts.filter((v): v is number => v !== null);
        const actualTotal = knownActuals.length ? sum(knownActuals) : null;
        const deltas = actualAmounts.map((v, i) => v === null ? null : sum([v, -amounts[i]]));
        Object.assign(result, { actualAmounts, actualTotal, varianceAmounts: deltas,
            varianceTotal: actualTotal === null ? null : sum([actualTotal, -total]),
            variancePercentages: deltas.map((v, i) => v === null ? null : percentage(v, amounts[i])),
            variancePercentageTotal: actualTotal === null ? null : percentage(sum([actualTotal, -total]), total) });
    }
    return result;
}
function channel(name: string): {
    name: string;
    key: string;
    order: number;
} | null {
    const match = /^(\d{6})\s+(.+)$/.exec(name.trim());
    if (!match)
        return null;
    const [_, code, caption] = match;
    if (['111111', '111112', '111129'].includes(code) || (code === '111120' && /мелкооптовая/i.test(caption)))
        return { name: 'Розничная выручка', key: 'retail', order: 1 };
    if (['111131', '111132', '111133'].includes(code))
        return { name: 'Выручка e-com', key: 'ecom', order: 2 };
    if (code === '111120' && /^Выручка оптовая(?:\s*\([^)]*\))?$/i.test(caption))
        return { name: 'Оптовая выручка', key: 'wholesale', order: 3 };
    // 111113 is NOT the modern Kaspi channel. Same code alone is not an alias.
    return null;
}
type Leaf = {
    article: BddsReportRow;
    activity: BddsReportRow;
    section: BddsReportRow;
    head: BddsReportRow;
    family: string;
    groupName: string;
};
function runs<T>(items: T[], key: (item: T) => string): T[][] {
    const result: T[][] = [];
    for (const item of items) {
        const prior = result.at(-1);
        if (prior && key(prior[0]) === key(item))
            prior.push(item);
        else
            result.push([item]);
    }
    return result;
}
// Re-projecting an already accepted presentation object is a no-op. Raw API
// objects (including clones) always pass structural/financial validation below.
const projected = new WeakSet<BddsReport>();
function catalogueProjection(report: BddsReport, byKey: Map<string, BddsReportRow>): BddsReport {
    const leaves: Leaf[] = report.rows.filter(r => r.kind === 'Article').map(article => {
        if (!article.cashFlowItemId)
            throw new Error('invalid-bdds-projection');
        let parent = byKey.get(article.parentKey!), head = parent;
        const visited = new Set<string>();
        while (parent?.kind === 'Group') {
            if (visited.has(parent.key))
                throw new Error('invalid-bdds-projection');
            visited.add(parent.key);
            head = parent;
            parent = byKey.get(parent.parentKey!);
        }
        const section = parent, activity = section && byKey.get(section.parentKey!);
        if (!head || section?.kind !== 'Section' || activity?.kind !== 'Activity')
            throw new Error('invalid-bdds-projection');
        const legacyGroup = (article.displayGroupId == null || article.displayGroupName == null)
            && article.cataloguePosition == null && !article.key.includes('|no-group|article|');
        const sales = legacyGroup && section.key === 'activity|Operating|section|Receipt' && channel(article.name) !== null;
        let groupName = article.displayGroupName ?? head.name;
        if (legacyGroup && section.key === 'activity|Operating|section|Receipt' && ['ПРОЧИЕ ПОСТУПЛЕНИЯ', 'ПРОЧИЕ ОПЕРАЦИОННЫЕ ПОСТУПЛЕНИЯ'].includes(label(groupName)))
            groupName = 'ПРОЧИЕ ОПЕРАЦИОННЫЕ ПОСТУПЛЕНИЯ';
        if (sales)
            groupName = 'ПОСТУПЛЕНИЯ ОТ РЕАЛИЗАЦИИ';
        const family = (article.displayGroupId
            ? activity.key + '|uuid|' + article.displayGroupId + '|run|' + (article.displayBlockId ?? '')
            // Unverified historic branches must not be reclassified by matching a caption.
            : section.key + '|legacy|' + head.key);
        return { article, activity, section, head, family, groupName };
    }).sort((a, b) => Number(a.article.cataloguePosition == null) - Number(b.article.cataloguePosition == null)
        || (a.article.cataloguePosition ?? catalogueRank(a.article)) - (b.article.cataloguePosition ?? catalogueRank(b.article)));
    const output: BddsReportRow[] = [], emittedActivities = new Set<string>();
    const key = (kind: string, anchor: Leaf) => '|catalogue-display|' + kind + '|' + anchor.article.key;
    const heading = (row: BddsReportRow, rowKey: string, parentKey: string | null, kind: BddsReportRow['kind']) => ({ ...row, key: rowKey, parentKey, kind, cashFlowItemId: null, displayGroupId: null, displayGroupName: null, displayBlockId: null, cataloguePosition: null, displayOrder: null });
    for (const activityRun of runs(leaves, leaf => leaf.activity.key)) {
        const first = activityRun[0], activityKey = key('activity', first);
        emittedActivities.add(first.activity.key);
        output.push(aggregate(heading(first.activity, activityKey, null, 'Activity'), activityRun.map(l => l.article)));
        const blocks = runs(activityRun, leaf => leaf.family).flatMap(block => new Set(block.map(l => l.section.key)).size > 1 ? [block] : runs(block, leaf => leaf.groupName));
        const sectionKind = (block: Leaf[]) => new Set(block.map(l => l.section.key)).size === 1 ? block[0].section.key : 'Mixed';
        for (const sectionRun of runs(blocks, sectionKind)) {
            const start = sectionRun[0][0], sectionKey = key('section', start), mixed = sectionKind(sectionRun[0]) === 'Mixed';
            const sectionHead = heading(start.section, sectionKey, activityKey, 'Section');
            if (mixed) {
                const activityName = label(start.activity.name);
                const type = activityName === 'ОПЕРАЦИОННАЯ ДЕЯТЕЛЬНОСТЬ' ? 'ОПЕРАЦИОННОЙ'
                    : activityName === 'ИНВЕСТИЦИОННАЯ ДЕЯТЕЛЬНОСТЬ' ? 'ИНВЕСТИЦИОННОЙ'
                        : activityName === 'ФИНАНСОВАЯ ДЕЯТЕЛЬНОСТЬ' ? 'ФИНАНСОВОЙ' : null;
                sectionHead.name = type ? `ПОСТУПЛЕНИЯ И ВЫПЛАТЫ ПО ${type} ДЕЯТЕЛЬНОСТИ` : 'ПОСТУПЛЕНИЯ И ВЫПЛАТЫ';
            }
            output.push(aggregate(sectionHead, sectionRun.flat().map(l => l.article)));
            for (const block of sectionRun) {
                const start = block[0], groupKey = key('group', start);
                const groupName = mixed ? start.article.displayGroupName ?? block.find(l => l.head.kind === 'Group')?.head.name ?? start.groupName : start.groupName;
                const wrapper = (start.head.kind === 'Group' || start.article.displayGroupId != null) && (mixed || label(groupName) !== label(sectionHead.name));
                const parentKey = wrapper ? groupKey : sectionKey;
                if (wrapper)
                    output.push(aggregate({ ...heading(start.head, groupKey, sectionKey, 'Group'), name: groupName }, block.map(l => l.article)));
                // Channels are contiguous subruns too: never reshuffle leaves into buckets.
                const channelOf = (leaf: Leaf) => leaf.section.key === 'activity|Operating|section|Receipt'
                    && label(leaf.groupName) === 'ПОСТУПЛЕНИЯ ОТ РЕАЛИЗАЦИИ' ? channel(leaf.article.name) : null;
                for (const channelRun of runs(block, leaf => channelOf(leaf)?.key ?? 'direct')) {
                    const channelInfo = channelOf(channelRun[0]);
                    const channelKey = parentKey + '|channel|' + channelRun[0].article.key;
                    if (channelInfo)
                        output.push(aggregate({ ...heading(channelRun[0].article, channelKey, parentKey, 'Group'), name: channelInfo.name }, channelRun.map(l => l.article)));
                    output.push(...channelRun.map(l => ({ ...l.article, parentKey: channelInfo ? channelKey : parentKey })));
                }
            }
        }
    }
    // Empty zero branches are harmless; do not discard the original activity row.
    output.push(...report.rows.filter(r => r.kind === 'Activity' && !emittedActivities.has(r.key)));
    output.push(...report.rows.filter(r => r.kind === 'Net'));
    if (new Set(output.map(r => r.key)).size !== output.length)
        throw new Error('invalid-bdds-projection');
    const result = { ...report, rows: output };
    projected.add(result);
    return result;
}
export function projectBddsReport(report: BddsReport): BddsReport {
    const byKey = new Map(report.rows.map(r => [r.key, r]));
    if (byKey.size !== report.rows.length)
        throw new Error('invalid-bdds-projection');
    const children = new Map<string, BddsReportRow[]>();
    for (const row of report.rows) {
        if (row.parentKey !== null) {
            if (!byKey.has(row.parentKey))
                throw new Error('invalid-bdds-projection');
            children.set(row.parentKey, [...(children.get(row.parentKey) ?? []), row]);
        }
    }
    // Reject incoherent source aggregates rather than hiding them by rebuilding.
    for (const row of report.rows.filter(r => r.kind !== 'Article' && r.kind !== 'Net')) {
        const parts = children.get(row.key) ?? [];
        const computed = aggregate(row, parts);
        if (row.amounts.some((v, i) => minor(v) !== minor(computed.amounts[i])) || minor(row.total) !== minor(computed.total)
            || row.compareAmounts?.some((v, i) => minor(v) !== minor(computed.compareAmounts![i]))
            || row.actualAmounts?.some((v, i) => v === null ? computed.actualAmounts![i] !== null
                : computed.actualAmounts![i] === null || minor(v) !== minor(computed.actualAmounts![i]!)))
            throw new Error('invalid-bdds-projection');
    }
    if (projected.has(report))
        return report;
    if (report.rows.some(r => r.kind === 'Article' && r.cashFlowItemId != null))
        return catalogueProjection(report, byKey);
    const groups = new Map<string, BddsReportRow>();
    const leaves = new Map<string, BddsReportRow[]>();
    const order = new Map<string, number>();
    function group(section: BddsReportRow, name: string, head: BddsReportRow) {
        const key = section.key + '|display-group|' + encodeURIComponent(label(name));
        if (!groups.has(key)) {
            groups.set(key, { ...head, key, parentKey: section.key, kind: 'Group', name });
            order.set(key, report.rows.indexOf(head));
        }
        return key;
    }
    for (const article of report.rows.filter(r => r.kind === 'Article')) {
        let parent = article.parentKey === null ? undefined : byKey.get(article.parentKey);
        let head = parent;
        const visited = new Set<string>();
        while (parent?.kind === 'Group') {
            if (visited.has(parent.key))
                throw new Error('invalid-bdds-projection');
            visited.add(parent.key);
            head = parent;
            parent = parent.parentKey === null ? undefined : byKey.get(parent.parentKey);
        }
        if (parent?.kind !== 'Section' || head === undefined)
            throw new Error('invalid-bdds-projection');
        const section = parent;
        const operatingReceipt = section.key === 'activity|Operating|section|Receipt';
        const salesChannel = operatingReceipt ? channel(article.name) : null;
        let name = head.kind === 'Section' ? section.name : head.name;
        if (operatingReceipt && ['ПРОЧИЕ ПОСТУПЛЕНИЯ', 'ПРОЧИЕ ОПЕРАЦИОННЫЕ ПОСТУПЛЕНИЯ'].includes(label(name)))
            name = 'ПРОЧИЕ ОПЕРАЦИОННЫЕ ПОСТУПЛЕНИЯ';
        if (salesChannel)
            name = 'ПОСТУПЛЕНИЯ ОТ РЕАЛИЗАЦИИ';
        let destination = label(name) === label(section.name) ? section.key : group(section, name, head);
        if (salesChannel) {
            const key = destination + '|channel|' + salesChannel.key;
            if (!groups.has(key)) {
                groups.set(key, { ...article, key, parentKey: destination, kind: 'Group', name: salesChannel.name });
                order.set(key, salesChannel.order);
            }
            destination = key;
        }
        leaves.set(destination, [...(leaves.get(destination) ?? []), { ...article, parentKey: destination }]);
    }
    const output: BddsReportRow[] = [];
    const seen = new Set<string>();
    const groupRanks = new Map<string, number>();
    function groupRank(row: BddsReportRow): number {
        const cached = groupRanks.get(row.key);
        if (cached !== undefined)
            return cached;
        const rank = Math.min(minimumCatalogueRank(leaves.get(row.key) ?? []), ...[...groups.values()].filter(g => g.parentKey === row.key).map(groupRank));
        groupRanks.set(row.key, rank);
        return rank;
    }
    function emit(row: BddsReportRow) {
        if (seen.has(row.key))
            throw new Error('invalid-bdds-projection');
        seen.add(row.key);
        const subgroups = [...groups.values()].filter(g => g.parentKey === row.key).sort((a, b) => groupRank(a) - groupRank(b) || (order.get(a.key) ?? 0) - (order.get(b.key) ?? 0));
        const articles = [...(leaves.get(row.key) ?? [])].sort((a, b) => catalogueRank(a) - catalogueRank(b));
        const all = [...subgroups.map(g => build(g)), ...articles];
        output.push(row.kind === 'Group' ? aggregate(row, all) : row);
        for (const g of subgroups)
            emit(g);
        output.push(...articles);
    }
    function build(row: BddsReportRow): BddsReportRow {
        const parts = [...groups.values()].filter(g => g.parentKey === row.key).map(build).concat(leaves.get(row.key) ?? []);
        return aggregate(row, parts);
    }
    for (const activity of report.rows.filter(r => r.kind === 'Activity')) {
        output.push(activity);
        for (const section of report.rows.filter(r => r.kind === 'Section' && r.parentKey === activity.key))
            emit(section);
    }
    output.push(...report.rows.filter(r => r.kind === 'Net'));
    if (output.filter(r => r.kind === 'Article').length !== report.rows.filter(r => r.kind === 'Article').length)
        throw new Error('invalid-bdds-projection');
    return { ...report, rows: output };
}
