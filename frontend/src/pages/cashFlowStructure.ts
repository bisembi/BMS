import type { CashFlowOrderItem } from '../api/cashFlowOrder';
import type { BddsReport, BddsReportRow } from '../api/bddsReport';
import { CASH_FLOW_ACTIVITY_TYPE_LABELS } from '../types';
import { projectBddsReport } from './bddsDisplayProjection';
import { filterCashFlowDirectory } from '../hooks/cashFlowDirectoryFilters';
/** Filter after projecting the full catalogue, so hidden articles cannot merge
 * otherwise separate catalogue runs or change the retained article's ancestry. */
export function filteredCashFlowStructure(items: readonly CashFlowOrderItem[], query = '', center = '', group = ''): BddsReportRow[] {
    const rows = cashFlowStructure(items);
    const matches = new Set(filterCashFlowDirectory(items, query, center, group).map(item => item.id));
    const byKey = new Map(rows.map(row => [row.key, row]));
    const retained = new Set<string>();
    for (const row of rows) {
        if (row.kind !== 'Article' || !row.cashFlowItemId || !matches.has(row.cashFlowItemId))
            continue;
        let current: BddsReportRow | undefined = row;
        while (current && !retained.has(current.key)) {
            retained.add(current.key);
            current = current.parentKey === null ? undefined : byKey.get(current.parentKey);
        }
    }
    return rows.filter(row => retained.has(row.key));
}
/** Read-only catalogue adapter. Empty measures are internal projection scaffolding,
 * never a budget, financial estimate or displayed amount. The actual BDDS projector
 * owns repeated runs, mixed directions, channel captions and wrapper suppression. */
export function cashFlowStructure(items: readonly CashFlowOrderItem[]): BddsReportRow[] {
    const rows: BddsReportRow[] = [], seen = new Set<string>();
    const articleIds = new Set<string>();
    let previousFamily: string | null = null, block = '';
    const add = (key: string, parentKey: string | null, kind: BddsReportRow['kind'], name: string) => {
        if (!seen.has(key)) {
            rows.push({ key, parentKey, kind, name, amounts: [], total: 0 });
            seen.add(key);
        }
    };
    items.forEach((item, position) => {
        if (articleIds.has(item.id))
            throw new Error('duplicate-catalogue-article');
        articleIds.add(item.id);
        const activity = item.activityType ?? 'Unknown';
        const activityKey = 'activity|' + activity;
        const activityName = item.activityType ? CASH_FLOW_ACTIVITY_TYPE_LABELS[item.activityType] : 'Вид деятельности не задан';
        add(activityKey, null, 'Activity', activityName.toUpperCase());
        const direction = item.flowDirection ?? 'Unknown';
        const sectionKey = activityKey + '|section|' + direction;
        const activityWord = activity === 'Operating' ? 'ОПЕРАЦИОННОЙ' : activity === 'Investing' ? 'ИНВЕСТИЦИОННОЙ'
            : activity === 'Financing' ? 'ФИНАНСОВОЙ' : null;
        const directionName = direction === 'Receipt' ? 'ПОСТУПЛЕНИЯ' : direction === 'Payment' ? 'ВЫПЛАТЫ' : 'НАПРАВЛЕНИЕ НЕ ЗАДАНО';
        add(sectionKey, activityKey, 'Section', activityWord && direction !== 'Unknown'
            ? `${directionName} ПО ${activityWord} ДЕЯТЕЛЬНОСТИ` : directionName);
        const groupKey = item.parentId ? sectionKey + '|group|' + item.parentId : null;
        const groupName = item.parentName?.trim().replace(/^\d+(?:\.\d+)*\.\s*/, '') || 'Группа не задана';
        if (groupKey)
            add(groupKey, sectionKey, 'Group', groupName);
        const family = activityKey + '|group|' + (item.parentId ?? '') + (direction === 'Unknown' ? '|unknown-direction' : '');
        // Block identity depends on adjacency in the FULL catalogue, not direction.
        if (family !== previousFamily || !item.parentId)
            block = 'preview-run|' + item.id;
        previousFamily = family;
        const key = (groupKey ?? sectionKey) + '|article|' + item.id;
        if (seen.has(key))
            throw new Error('duplicate-catalogue-article');
        add(key, groupKey ?? sectionKey, 'Article', item.name);
        Object.assign(rows[rows.length - 1], { cashFlowItemId: item.id, displayGroupId: item.parentId ?? null,
            displayGroupName: item.parentId ? groupName : null,
            displayBlockId: item.parentId ? block : null, cataloguePosition: position, displayOrder: item.displayOrder ?? null });
    });
    const input: BddsReport = { snapshotId: '', sourceVersionDate: '', scenario: 'catalogue-structure-only',
        budgetYear: 0, costCenterId: null, costCenters: [], approvedAtUtc: '', receiptsAmountKzt: 0,
        paymentsAmountKzt: 0, netCashFlowAmountKzt: 0, rows };
    return projectBddsReport(input).rows;
}
