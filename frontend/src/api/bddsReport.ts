import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
export type BddsCostCenter = {
    id: string;
    code: string;
    name: string;
};
export type BddsReportRowKind = 'Activity' | 'Section' | 'Group' | 'Article' | 'Net';
export type BddsReportRow = {
    displayOrder?: number | null;
    cashFlowItemId?: string | null;
    displayGroupId?: string | null;
    displayGroupName?: string | null;
    displayBlockId?: string | null;
    cataloguePosition?: number | null;
    key: string;
    parentKey: string | null;
    kind: BddsReportRowKind;
    name: string;
    costCenterCode?: string | null;
    amounts: number[];
    total: number;
    actualAmounts?: (number | null)[];
    actualTotal?: number | null;
    varianceAmounts?: (number | null)[];
    varianceTotal?: number | null;
    variancePercentages?: (number | null)[];
    variancePercentageTotal?: number | null;
    compareAmounts?: number[];
    compareTotal?: number;
    versionVarianceAmounts?: number[];
    versionVarianceTotal?: number;
    versionVariancePercentages?: (number | null)[];
    versionVariancePercentageTotal?: number | null;
};
export type BddsVersion = {
    id: string;
    name: string;
    concurrencyStamp: string;
    isActive: boolean;
    isPublished: boolean;
};
export type BddsReport = {
    snapshotId: string;
    sourceVersionDate: string;
    scenario: string;
    budgetYear: number;
    costCenterId: string | null;
    costCenters: BddsCostCenter[];
    rows: BddsReportRow[];
    receiptsAmountKzt: number;
    paymentsAmountKzt: number;
    netCashFlowAmountKzt: number;
    approvedAtUtc: string;
    months?: string[];
    canIncludeActuals?: boolean;
    includesActuals?: boolean;
    actualsYear?: number | null;
    actualsSourceYear?: number | null;
    actualsSnapshotId?: string | null;
    actualsCapturedAtUtc?: string | null;
    actualsAvailableMonths?: string[] | null;
    actualReceiptsAmountKzt?: number | null;
    actualPaymentsAmountKzt?: number | null;
    actualNetCashFlowAmountKzt?: number | null;
    planSource?: string;
    planVersion?: BddsVersion | null;
    compareVersion?: BddsVersion | null;
    compareReceiptsAmountKzt?: number;
    comparePaymentsAmountKzt?: number;
    compareNetCashFlowAmountKzt?: number;
};
export type BddsReportOptions = {
    fromMonth: string;
    toMonth: string;
    includeActuals: boolean;
    actualsYear?: number;
    versionId?: string;
    compareVersionId?: string;
};
export class BddsReportApiError extends Error {
    constructor(readonly status: number) {
        super('Не удалось загрузить БДДС.');
    }
}
const control = /[\u0000-\u001f\u007f-\u009f]/u;
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const text = (value: unknown, max: number): value is string => typeof value === 'string' && value.length > 0 && value.length <= max
    && value.trim() === value && !control.test(value);
const signedMoney = (value: unknown): value is number => {
    if (typeof value !== 'number' || !Number.isFinite(value))
        return false;
    const scaled = value * 100;
    const cents = Math.round(scaled);
    const tolerance = Math.max(1e-7, Math.abs(scaled) * Number.EPSILON * 4);
    return Number.isSafeInteger(cents) && Math.abs(scaled - cents) <= tolerance;
};
const calendarDate = (value: unknown): value is string => {
    if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/u.test(value))
        return false;
    const parsed = new Date(`${value}T00:00:00.000Z`);
    return !Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
};
const utcTimestamp = (value: unknown): value is string => typeof value === 'string'
    && (value.endsWith('Z') || value.endsWith('+00:00')) && !Number.isNaN(Date.parse(value));
const internalCashFlowCode = /(?:^|\s)CF-\d+(?:\s|$)/u;
function costCenter(value: unknown): BddsCostCenter {
    if (!record(value) || !isPublicExpenseRequestUuid(value.id)
        || !text(value.code, 100) || !text(value.name, 500))
        throw new Error('invalid-bdds-response');
    return { id: value.id, code: value.code, name: value.name };
}
function reportRow(value: unknown, count: number): BddsReportRow {
    if (record(value)) {
        if (value.displayGroupName != null && (value.kind !== 'Article' || !text(value.displayGroupName, 600)
            || !isPublicExpenseRequestUuid(value.displayGroupId)))
            throw new Error('invalid-bdds-response');
        if (value.cataloguePosition != null && (value.kind !== 'Article' || typeof value.cataloguePosition !== 'number'
            || !Number.isSafeInteger(value.cataloguePosition) || value.cataloguePosition < 0 || value.cataloguePosition >= 10000
            || !isPublicExpenseRequestUuid(value.cashFlowItemId)))
            throw new Error('invalid-bdds-response');
        for (const field of ['cashFlowItemId', 'displayGroupId'] as const)
            if (value[field] != null && (value.kind !== 'Article' || !isPublicExpenseRequestUuid(value[field])))
                throw new Error('invalid-bdds-response');
        if (value.displayBlockId != null && (value.kind !== 'Article' || typeof value.displayBlockId !== 'string'
            || !/^[A-F0-9]{64}$/.test(value.displayBlockId) || !isPublicExpenseRequestUuid(value.displayGroupId)))
            throw new Error('invalid-bdds-response');
        if (value.displayGroupId != null && !isPublicExpenseRequestUuid(value.cashFlowItemId))
            throw new Error('invalid-bdds-response');
    }
    if (record(value) && value.displayOrder != null && (typeof value.displayOrder !== 'number'
        || !Number.isSafeInteger(value.displayOrder) || value.displayOrder < 0 || value.displayOrder > 2147483647))
        throw new Error('invalid-bdds-response');
    if (!record(value) || !text(value.key, 2000)
        || (value.parentKey !== null && !text(value.parentKey, 2000))
        || !['Activity', 'Section', 'Group', 'Article', 'Net'].includes(String(value.kind))
        || !text(value.name, 600) || internalCashFlowCode.test(value.name)
        || !Array.isArray(value.amounts) || value.amounts.length !== count
        || !value.amounts.every(signedMoney) || !signedMoney(value.total)) {
        throw new Error('invalid-bdds-response');
    }
    const cents = value.amounts.reduce((sum, amount) => sum + Math.round((amount as number) * 100), 0);
    if (!Number.isSafeInteger(cents) || cents !== Math.round((value.total as number) * 100)) {
        throw new Error('invalid-bdds-response');
    }
    if (value.costCenterCode !== undefined && value.costCenterCode !== null
        && (value.kind !== 'Article' || !text(value.costCenterCode, 100)))
        throw new Error('invalid-bdds-response');
    return {
        key: value.key,
        parentKey: value.parentKey as string | null,
        kind: value.kind as BddsReportRowKind,
        name: value.name,
        ...(value.displayGroupName === undefined ? {} : { displayGroupName: value.displayGroupName as string | null }),
        ...(value.cataloguePosition === undefined ? {} : { cataloguePosition: value.cataloguePosition as number | null }),
        ...(value.cashFlowItemId === undefined ? {} : { cashFlowItemId: value.cashFlowItemId as string | null }),
        ...(value.displayGroupId === undefined ? {} : { displayGroupId: value.displayGroupId as string | null }),
        ...(value.displayBlockId === undefined ? {} : { displayBlockId: value.displayBlockId as string | null }),
        ...(value.displayOrder === undefined ? {} : { displayOrder: value.displayOrder as number | null }),
        ...(value.costCenterCode === undefined ? {} : { costCenterCode: value.costCenterCode as string | null }),
        amounts: value.amounts as number[],
        total: value.total as number,
    };
}
function selectedMonths(year: number, options?: BddsReportOptions): string[] {
    const from = options?.fromMonth ?? `${year}-01`;
    const to = options?.toMonth ?? `${year}-12`;
    const valid = (month: string) => /^\d{4}-(0[1-9]|1[0-2])$/u.test(month) && month.startsWith(`${year}-`);
    if (!valid(from) || !valid(to) || from > to)
        throw new BddsReportApiError(400);
    return Array.from({ length: Number(to.slice(5)) - Number(from.slice(5)) + 1 }, (_, i) => `${year}-${String(Number(from.slice(5)) + i).padStart(2, '0')}-01`);
}
const sameMoney = (a: number, b: number) => Math.round(a * 100) === Math.round(b * 100);
const nullableMoney = (value: unknown): value is number | null => value === null || signedMoney(value);
function percentage(delta: number | null, budget: number): number | null {
    if (delta === null || budget === 0)
        return null;
    // Rational integer cents match the server's decimal midpoint rounding, including 0.55%.
    const numerator = BigInt(Math.abs(Math.round(delta * 100))) * 1000n;
    const denominator = BigInt(Math.abs(Math.round(budget * 100)));
    const tenths = (2n * numerator + denominator) / (2n * denominator);
    return tenths <= BigInt(Number.MAX_SAFE_INTEGER) ? Math.sign(delta) * Number(tenths) / 10 : null;
}
function comparisonRow(raw: Record<string, unknown>, row: BddsReportRow, months: string[], available: string[]): BddsReportRow {
    if (!Array.isArray(raw.actualAmounts) || !Array.isArray(raw.varianceAmounts) || !Array.isArray(raw.variancePercentages)
        || raw.actualAmounts.length !== months.length || raw.varianceAmounts.length !== months.length
        || raw.variancePercentages.length !== months.length || !raw.actualAmounts.every(nullableMoney)
        || !raw.varianceAmounts.every(nullableMoney) || !nullableMoney(raw.actualTotal)
        || !nullableMoney(raw.varianceTotal))
        throw new Error('invalid-bdds-response');
    const facts = raw.actualAmounts as (number | null)[];
    const deltas = raw.varianceAmounts as (number | null)[];
    const percentages = raw.variancePercentages as (number | null)[];
    for (let i = 0; i < months.length; i++) {
        if (!available.includes(months[i]) && facts[i] !== null)
            throw new Error('invalid-bdds-response');
        const delta = facts[i] === null ? null : (Math.round(facts[i]! * 100) - Math.round(row.amounts[i] * 100)) / 100;
        if (delta === null ? deltas[i] !== null : deltas[i] === null || !sameMoney(deltas[i]!, delta))
            throw new Error('invalid-bdds-response');
        const expected = percentage(delta, row.amounts[i]);
        if (expected === null ? percentages[i] !== null
            : typeof percentages[i] !== 'number' || !Number.isFinite(percentages[i]) || Math.abs(percentages[i]! - expected) > 0.000001)
            throw new Error('invalid-bdds-response');
    }
    const total = facts.some(cell => cell !== null) ? facts.reduce<number>((sum, cell) => sum + Math.round((cell ?? 0) * 100), 0) / 100 : null;
    const deltaTotal = total === null ? null : (Math.round(total * 100) - Math.round(row.total * 100)) / 100;
    if (total === null ? raw.actualTotal !== null : !sameMoney(raw.actualTotal as number, total))
        throw new Error('invalid-bdds-response');
    if (deltaTotal === null ? raw.varianceTotal !== null : !sameMoney(raw.varianceTotal as number, deltaTotal))
        throw new Error('invalid-bdds-response');
    const pctTotal = percentage(deltaTotal, row.total);
    if (pctTotal === null ? raw.variancePercentageTotal !== null
        : typeof raw.variancePercentageTotal !== 'number' || !Number.isFinite(raw.variancePercentageTotal)
            || Math.abs(raw.variancePercentageTotal - pctTotal) > 0.000001)
        throw new Error('invalid-bdds-response');
    return { ...row, actualAmounts: facts, actualTotal: raw.actualTotal as number | null,
        varianceAmounts: deltas, varianceTotal: raw.varianceTotal as number | null,
        variancePercentages: percentages, variancePercentageTotal: raw.variancePercentageTotal as number | null };
}
function version(value: unknown): BddsVersion {
    if (!record(value) || !isPublicExpenseRequestUuid(value.id) || !text(value.name, 200)
        || !isPublicExpenseRequestUuid(value.concurrencyStamp) || typeof value.isActive !== 'boolean'
        || typeof value.isPublished !== 'boolean' || (value.isActive && !value.isPublished))
        throw new Error('invalid-bdds-response');
    return { id: value.id, name: value.name, concurrencyStamp: value.concurrencyStamp, isActive: value.isActive, isPublished: value.isPublished };
}
function versionComparisonRow(raw: Record<string, unknown>, row: BddsReportRow, months: string[]): BddsReportRow {
    if (!Array.isArray(raw.compareAmounts) || raw.compareAmounts.length !== months.length || !raw.compareAmounts.every(signedMoney)
        || !signedMoney(raw.compareTotal) || !Array.isArray(raw.versionVarianceAmounts) || raw.versionVarianceAmounts.length !== months.length
        || !raw.versionVarianceAmounts.every(signedMoney) || !signedMoney(raw.versionVarianceTotal)
        || !Array.isArray(raw.versionVariancePercentages) || raw.versionVariancePercentages.length !== months.length)
        throw new Error('invalid-bdds-response');
    const compared = raw.compareAmounts as number[];
    const deltas = raw.versionVarianceAmounts as number[];
    const percentages = raw.versionVariancePercentages as (number | null)[];
    const cents = compared.reduce((sum, value) => sum + Math.round(value * 100), 0);
    if (!Number.isSafeInteger(cents) || cents !== Math.round(raw.compareTotal * 100)
        || !sameMoney(raw.versionVarianceTotal, (Math.round(raw.compareTotal * 100) - Math.round(row.total * 100)) / 100))
        throw new Error('invalid-bdds-response');
    const validPercentage = (actual: unknown, expected: number | null) => expected === null ? actual === null
        : typeof actual === 'number' && Number.isFinite(actual) && Math.abs(actual - expected) < 0.000001;
    for (let index = 0; index < months.length; index++) {
        const delta = (Math.round(compared[index] * 100) - Math.round(row.amounts[index] * 100)) / 100;
        if (!sameMoney(deltas[index], delta) || !validPercentage(percentages[index], percentage(delta, row.amounts[index])))
            throw new Error('invalid-bdds-response');
    }
    if (!validPercentage(raw.versionVariancePercentageTotal, percentage(raw.versionVarianceTotal, row.total)))
        throw new Error('invalid-bdds-response');
    return { ...row, compareAmounts: compared, compareTotal: raw.compareTotal, versionVarianceAmounts: deltas,
        versionVarianceTotal: raw.versionVarianceTotal, versionVariancePercentages: percentages,
        versionVariancePercentageTotal: raw.versionVariancePercentageTotal as number | null };
}
export function publicBddsReport(value: unknown, year: number, requestedCostCenterId?: string, options?: BddsReportOptions): BddsReport {
    const months = selectedMonths(year, options);
    if (!record(value) || !isPublicExpenseRequestUuid(value.snapshotId)
        || !calendarDate(value.sourceVersionDate)
        || !text(value.scenario, 100) || value.budgetYear !== year
        || (value.costCenterId !== null && !isPublicExpenseRequestUuid(value.costCenterId))
        || !Array.isArray(value.costCenters) || !Array.isArray(value.rows)
        || !signedMoney(value.receiptsAmountKzt) || (value.receiptsAmountKzt as number) < 0
        || !signedMoney(value.paymentsAmountKzt) || (value.paymentsAmountKzt as number) < 0
        || !signedMoney(value.netCashFlowAmountKzt)
        || !utcTimestamp(value.approvedAtUtc)) {
        throw new Error('invalid-bdds-response');
    }
    const centers = value.costCenters.map(costCenter);
    if (new Set(centers.map(center => center.id.toLowerCase())).size !== centers.length
        || (requestedCostCenterId === undefined ? value.costCenterId !== null
            : String(value.costCenterId).toLowerCase() !== requestedCostCenterId.toLowerCase())
        || (value.costCenterId !== null
            && !centers.some(center => center.id.toLowerCase() === String(value.costCenterId).toLowerCase()))
        || Math.round(((value.receiptsAmountKzt as number) - (value.paymentsAmountKzt as number)) * 100)
            !== Math.round((value.netCashFlowAmountKzt as number) * 100)) {
        throw new Error('invalid-bdds-response');
    }
    if (value.months !== undefined && (!Array.isArray(value.months) || value.months.length !== months.length
        || value.months.some((month, i) => month !== months[i])))
        throw new Error('invalid-bdds-response');
    if (options && value.months === undefined)
        throw new Error('invalid-bdds-response');
    const includeActuals = options?.includeActuals ?? false;
    const planVersion = value.planVersion == null ? null : version(value.planVersion);
    const compareVersion = value.compareVersion == null ? null : version(value.compareVersion);
    const comparingVersions = options?.compareVersionId !== undefined;
    if (value.planSource !== undefined && value.planSource !== 'COMPANY_BUDGET_VERSION'
        && value.planSource !== 'LATEST_APPROVED_BUDGET_VERSIONS')
        throw new Error('invalid-bdds-response');
    if ((options?.versionId || comparingVersions) && value.planSource !== 'COMPANY_BUDGET_VERSION')
        throw new Error('invalid-bdds-response');
    if (((options?.versionId || comparingVersions || value.planSource === 'COMPANY_BUDGET_VERSION') && planVersion === null)
        || (options?.versionId && planVersion?.id.toLowerCase() !== options.versionId.toLowerCase())
        || (comparingVersions ? compareVersion?.id.toLowerCase() !== options!.compareVersionId!.toLowerCase() : compareVersion !== null)
        || (compareVersion !== null && planVersion?.id.toLowerCase() === compareVersion.id.toLowerCase()))
        throw new Error('invalid-bdds-response');
    if (comparingVersions && (!signedMoney(value.compareReceiptsAmountKzt) || value.compareReceiptsAmountKzt < 0
        || !signedMoney(value.comparePaymentsAmountKzt) || value.comparePaymentsAmountKzt < 0 || !signedMoney(value.compareNetCashFlowAmountKzt)
        || !sameMoney(value.compareReceiptsAmountKzt - value.comparePaymentsAmountKzt, value.compareNetCashFlowAmountKzt)))
        throw new Error('invalid-bdds-response');
    if (value.includesActuals !== undefined && value.includesActuals !== includeActuals)
        throw new Error('invalid-bdds-response');
    if (includeActuals && (value.includesActuals !== true || value.canIncludeActuals !== true))
        throw new Error('invalid-bdds-response');
    if (includeActuals && options?.actualsYear !== undefined
        && (value.actualsYear !== options.actualsYear
            || (value.actualsSnapshotId === null ? value.actualsSourceYear !== null : value.actualsSourceYear !== options.actualsYear)))
        throw new Error('invalid-bdds-response');
    if (includeActuals && ('actualsYear' in value || 'actualsSourceYear' in value)
        && (value.actualsYear !== (options?.actualsYear ?? year)
            || (value.actualsSnapshotId === null ? value.actualsSourceYear !== null : value.actualsSourceYear !== value.actualsYear)))
        throw new Error('invalid-bdds-response');
    const available = includeActuals && Array.isArray(value.actualsAvailableMonths) ? value.actualsAvailableMonths as string[] : [];
    if (includeActuals && (!Array.isArray(value.actualsAvailableMonths) || new Set(available).size !== available.length
        || available.some(month => !months.includes(month))
        || (value.actualsSnapshotId === null ? available.length !== 0 || value.actualsCapturedAtUtc !== null
            : !isPublicExpenseRequestUuid(value.actualsSnapshotId) || !utcTimestamp(value.actualsCapturedAtUtc))
        || !nullableMoney(value.actualReceiptsAmountKzt) || !nullableMoney(value.actualPaymentsAmountKzt)
        || !nullableMoney(value.actualNetCashFlowAmountKzt)))
        throw new Error('invalid-bdds-response');
    const rows = value.rows.map(raw => {
        let row = reportRow(raw, months.length);
        if (comparingVersions)
            row = versionComparisonRow(raw as Record<string, unknown>, row, months);
        return includeActuals ? comparisonRow(raw as Record<string, unknown>, row, months, available) : row;
    });
    const byKey = new Map<string, BddsReportRow>();
    for (const row of rows) {
        if (byKey.has(row.key))
            throw new Error('invalid-bdds-response');
        const parent = row.parentKey === null ? null : byKey.get(row.parentKey);
        const validHierarchy = (row.kind === 'Activity' && parent === null)
            || (row.kind === 'Section' && parent?.kind === 'Activity')
            || (row.kind === 'Group' && parent?.kind === 'Section')
            || (row.kind === 'Article' && (parent?.kind === 'Group'
                // The server removes redundant same-caption wrappers in comparisons.
                // An article remains within its exact, validated activity/section.
                || (comparingVersions && parent?.kind === 'Section')))
            || (row.kind === 'Net' && parent === null);
        if (!validHierarchy)
            throw new Error('invalid-bdds-response');
        byKey.set(row.key, row);
    }
    const netRows = rows.filter(row => row.kind === 'Net');
    if (netRows.length !== 1
        || Math.round(netRows[0].total * 100) !== Math.round((value.netCashFlowAmountKzt as number) * 100)) {
        throw new Error('invalid-bdds-response');
    }
    if (comparingVersions && !sameMoney(netRows[0].compareTotal!, value.compareNetCashFlowAmountKzt as number))
        throw new Error('invalid-bdds-response');
    if (includeActuals) {
        const net = netRows[0];
        const hasAvailableActuals = available.length > 0 && value.actualsSnapshotId !== null;
        if (!hasAvailableActuals ? value.actualReceiptsAmountKzt !== null || value.actualPaymentsAmountKzt !== null
            || value.actualNetCashFlowAmountKzt !== null
            : value.actualReceiptsAmountKzt === null || value.actualPaymentsAmountKzt === null || value.actualNetCashFlowAmountKzt === null
                || (value.actualReceiptsAmountKzt as number) < 0 || (value.actualPaymentsAmountKzt as number) < 0
                || !sameMoney((value.actualReceiptsAmountKzt as number) - (value.actualPaymentsAmountKzt as number), value.actualNetCashFlowAmountKzt as number))
            throw new Error('invalid-bdds-response');
        if (net.actualTotal === null ? value.actualNetCashFlowAmountKzt !== null
            : !sameMoney(net.actualTotal!, value.actualNetCashFlowAmountKzt as number))
            throw new Error('invalid-bdds-response');
        for (let i = 0; i < months.length; i++) {
            const sum = rows.filter(row => row.kind === 'Article').reduce((sum, row) => sum + (row.actualAmounts![i] ?? 0), 0);
            if (available.includes(months[i]) && (net.actualAmounts![i] === null || !sameMoney(sum, net.actualAmounts![i]!)))
                throw new Error('invalid-bdds-response');
        }
    }
    return {
        snapshotId: value.snapshotId,
        sourceVersionDate: value.sourceVersionDate as string,
        scenario: value.scenario,
        budgetYear: value.budgetYear,
        costCenterId: value.costCenterId as string | null,
        costCenters: centers,
        rows,
        receiptsAmountKzt: value.receiptsAmountKzt as number,
        paymentsAmountKzt: value.paymentsAmountKzt as number,
        netCashFlowAmountKzt: value.netCashFlowAmountKzt as number,
        approvedAtUtc: value.approvedAtUtc,
        months: value.months === undefined ? undefined : months,
        canIncludeActuals: value.canIncludeActuals === true,
        includesActuals: includeActuals,
        ...(includeActuals && value.actualsYear !== undefined ? {
            actualsYear: value.actualsYear as number,
            actualsSourceYear: value.actualsSourceYear as number | null,
        } : {}),
        ...(includeActuals ? { actualsSnapshotId: value.actualsSnapshotId as string | null,
            actualsCapturedAtUtc: value.actualsCapturedAtUtc as string | null, actualsAvailableMonths: available,
            actualReceiptsAmountKzt: value.actualReceiptsAmountKzt as number | null,
            actualPaymentsAmountKzt: value.actualPaymentsAmountKzt as number | null,
            actualNetCashFlowAmountKzt: value.actualNetCashFlowAmountKzt as number | null } : {}),
        ...(value.planSource === undefined ? {} : { planSource: value.planSource as string }),
        ...(value.planVersion === undefined ? {} : { planVersion }),
        ...(value.compareVersion === undefined ? {} : { compareVersion }),
        ...(comparingVersions ? { compareReceiptsAmountKzt: value.compareReceiptsAmountKzt as number,
            comparePaymentsAmountKzt: value.comparePaymentsAmountKzt as number, compareNetCashFlowAmountKzt: value.compareNetCashFlowAmountKzt as number } : {}),
    };
}
export async function getBddsReport(year: number, costCenterId?: string, signal?: AbortSignal, options?: BddsReportOptions): Promise<BddsReport> {
    if (!Number.isInteger(year) || year < 2000 || year > 2100
        || (options?.actualsYear !== undefined
            && (!options.includeActuals || (!Number.isInteger(options.actualsYear) || options.actualsYear < 2025 || options.actualsYear > 2100)))
        || (costCenterId !== undefined && !isPublicExpenseRequestUuid(costCenterId))
        || (options?.versionId !== undefined && !isPublicExpenseRequestUuid(options.versionId))
        || (options?.compareVersionId !== undefined && !isPublicExpenseRequestUuid(options.compareVersionId))
        || (options?.compareVersionId && options.versionId?.toLowerCase() === options.compareVersionId.toLowerCase())) {
        throw new BddsReportApiError(400);
    }
    selectedMonths(year, options);
    const parameters = new URLSearchParams({ year: String(year) });
    if (costCenterId !== undefined)
        parameters.set('costCenterId', costCenterId);
    if (options) {
        parameters.set('fromMonth', options.fromMonth);
        parameters.set('toMonth', options.toMonth);
        parameters.set('includeActuals', String(options.includeActuals));
        if (options.actualsYear !== undefined)
            parameters.set('actualsYear', String(options.actualsYear));
        if (options.versionId)
            parameters.set('versionId', options.versionId);
        if (options.compareVersionId)
            parameters.set('compareVersionId', options.compareVersionId);
    }
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/budget/reports/bdds?${parameters}`), { ...secureReadInit(), signal });
    if (response.status !== 200)
        throw new BddsReportApiError(response.status);
    return publicBddsReport(await response.json(), year, costCenterId, options);
}
export function bddsReportErrorMessage(error: unknown, budgetYear = 2026): string {
    if (error instanceof BddsReportApiError) {
        if (error.status === 401)
            return 'Для просмотра БДДС требуется авторизация.';
        if (error.status === 403)
            return 'Нет доступа ни к одному ЦФО отчёта БДДС.';
        if (error.status === 404)
            return `БДДС за ${budgetYear} год не найден или выбранная версия недоступна.`;
        if (error.status === 409)
            return 'Не удалось построить БДДС: проверьте состояние выбранных версий и сопоставление статей факта.';
    }
    return 'Не удалось загрузить БДДС. Повторите попытку.';
}
