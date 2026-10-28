import { isPublicExpenseRequestMoney, isPublicExpenseRequestText, isPublicExpenseRequestTimestamp, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
import type { ManualDeficit, ManualDeficitCorrection, ManualDeficitCorrectionState, ManualDeficitDetail, ManualDeficitPage, ManualDeficitQuery } from './manualDeficitTypes';
export const invalidManualDeficitResponse = 'Некорректный ответ API корректировок дефицита.';
const invalid = (): never => { throw new Error(invalidManualDeficitResponse); };
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const sameId = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();
const integer = (v: unknown): v is number => typeof v === 'number' && Number.isInteger(v) && v >= 0 && v <= 2147483647;
const currency = (v: unknown): v is string => typeof v === 'string' && /^[A-Z]{3}$/u.test(v);
const money = (v: unknown, zero = true): v is number => isPublicExpenseRequestMoney(v, zero)
    && Number.isSafeInteger(Math.round(v * 100)) && Math.round(v * 100) / 100 === v;
const cents = (v: number) => BigInt(Math.round(v * 100));
export function publicManualDeficitConflictCode(value: unknown): string | null {
    return record(value) && (value.code === 'state-conflict' || value.code === 'idempotency-conflict')
        ? value.code : null;
}
export function isManualDeficitText(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= 1000 && value.trim() === value
        && !/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/u.test(value);
}
function utc(value: unknown): value is string {
    return isPublicExpenseRequestTimestamp(value)
        && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?(?:Z|\+00:00)$/u.test(value)
        && new Date(value).toISOString().slice(0, 19) === value.slice(0, 19);
}
function state(value: unknown): value is ManualDeficitCorrectionState {
    return value === 'PendingApproval' || value === 'ApprovedAndApplied' || value === 'Rejected' || value === 'Annulled';
}
export function publicManualDeficit(value: unknown): ManualDeficit {
    if (!record(value) || !isPublicExpenseRequestUuid(value.id)
        || !isPublicExpenseRequestText(value.organizationName, 500)
        || !isPublicExpenseRequestText(value.counterpartyName, 500) || !currency(value.currencyCode)
        || !utc(value.occurredAtUtc) || !money(value.originalAmount, false) || !money(value.originalAmountKzt, false)
        || !money(value.currentAmount) || !money(value.currentAmountKzt) || !integer(value.correctionCount)
        || typeof value.hasPendingCorrection !== 'boolean' || typeof value.canPropose !== 'boolean'
        || typeof value.canDecide !== 'boolean')
        return invalid();
    if (value.currentAmount > value.originalAmount || value.currentAmountKzt > value.originalAmountKzt
        || (value.currentAmount === 0) !== (value.currentAmountKzt === 0)
        || (value.currencyCode === 'KZT' && (value.originalAmount !== value.originalAmountKzt || value.currentAmount !== value.currentAmountKzt))
        || (value.hasPendingCorrection && value.correctionCount === 0)
        || (value.canPropose && (value.hasPendingCorrection || value.currentAmount === 0))
        || (value.canDecide && !value.hasPendingCorrection))
        return invalid();
    return {
        id: value.id, organizationName: value.organizationName, counterpartyName: value.counterpartyName,
        currencyCode: value.currencyCode, occurredAtUtc: value.occurredAtUtc,
        originalAmount: value.originalAmount, originalAmountKzt: value.originalAmountKzt,
        currentAmount: value.currentAmount, currentAmountKzt: value.currentAmountKzt,
        correctionCount: value.correctionCount, hasPendingCorrection: value.hasPendingCorrection,
        canPropose: value.canPropose, canDecide: value.canDecide,
    };
}
export function publicManualDeficitCorrection(value: unknown, deficitId: string, correctionId?: string): ManualDeficitCorrection {
    if (!record(value) || !isPublicExpenseRequestUuid(value.id) || !isPublicExpenseRequestUuid(value.deficitId)
        || !sameId(value.deficitId, deficitId) || (correctionId !== undefined && !sameId(value.id, correctionId))
        || !currency(value.currencyCode) || !state(value.state) || !money(value.amount, false)
        || !money(value.amountKzt, false) || !money(value.deficitAmountAtCreation, false)
        || !money(value.deficitAmountKztAtCreation, false) || !isManualDeficitText(value.basis)
        || !(value.decidedAtUtc === null || utc(value.decidedAtUtc))
        || !(value.decisionComment === null || isManualDeficitText(value.decisionComment))
        || !(value.rejectionReason === null || isManualDeficitText(value.rejectionReason)))
        return invalid();
    const original = cents(value.deficitAmountAtCreation);
    const originalKzt = cents(value.deficitAmountKztAtCreation);
    const amount = cents(value.amount);
    const amountKzt = cents(value.amountKzt);
    // Match the server's paired allocation using integer minor units, including
    // midpoint-away-from-zero and the exact full-balance remainder.
    const expectedKzt = (2n * originalKzt * amount + original) / (2n * original);
    if (amount > original || amountKzt > originalKzt || expectedKzt !== amountKzt
        || (amount === original) !== (amountKzt === originalKzt)
        || (value.currencyCode === 'KZT' && (original !== originalKzt || amount !== amountKzt))
        || (value.state === 'PendingApproval' && (value.decidedAtUtc !== null || value.decisionComment !== null || value.rejectionReason !== null))
        || (value.state !== 'PendingApproval' && value.decidedAtUtc === null)
        || (value.state === 'Rejected') !== (value.rejectionReason !== null))
        return invalid();
    return {
        id: value.id, deficitId: value.deficitId, currencyCode: value.currencyCode, state: value.state,
        amount: value.amount, amountKzt: value.amountKzt, deficitAmountAtCreation: value.deficitAmountAtCreation,
        deficitAmountKztAtCreation: value.deficitAmountKztAtCreation, basis: value.basis,
        decidedAtUtc: value.decidedAtUtc, decisionComment: value.decisionComment, rejectionReason: value.rejectionReason,
    };
}
function page(value: Record<string, unknown>, skip: number, take: number, count: number): number {
    if (!integer(value.skip) || value.skip !== skip || !integer(value.take) || value.take !== take
        || take < 1 || take > 100 || skip > 2147483647 - take || !integer(value.total)
        || count !== Math.min(take, Math.max(0, value.total - skip)))
        return invalid();
    return value.total;
}
export function publicManualDeficitPage(value: unknown, query: ManualDeficitQuery): ManualDeficitPage {
    if (!record(value) || !Array.isArray(value.items))
        return invalid();
    const total = page(value, query.skip, query.take, value.items.length);
    const ids = new Set<string>();
    const items = value.items.map((row: unknown) => {
        const item = publicManualDeficit(row);
        if (ids.has(item.id.toLowerCase()) || (query.onlyPending && !item.hasPendingCorrection)
            || (!query.includeSettled && item.currentAmount === 0 && !item.hasPendingCorrection))
            return invalid();
        ids.add(item.id.toLowerCase());
        return item;
    });
    return { skip: query.skip, take: query.take, total, items };
}
export function publicManualDeficitDetail(value: unknown, id: string, skip: number, take: number): ManualDeficitDetail {
    if (!record(value) || !Array.isArray(value.corrections))
        return invalid();
    const deficit = publicManualDeficit(value.deficit);
    const total = page(value, skip, take, value.corrections.length);
    if (!sameId(deficit.id, id) || total !== deficit.correctionCount)
        return invalid();
    const ids = new Set<string>();
    const corrections = value.corrections.map((row: unknown) => {
        const correction = publicManualDeficitCorrection(row, id);
        if (ids.has(correction.id.toLowerCase()) || correction.currencyCode !== deficit.currencyCode
            || correction.deficitAmountAtCreation > deficit.originalAmount
            || correction.deficitAmountKztAtCreation > deficit.originalAmountKzt)
            return invalid();
        ids.add(correction.id.toLowerCase());
        return correction;
    });
    const pending = corrections.filter((row) => row.state === 'PendingApproval').length;
    if (pending > 1 || (pending > 0 && !deficit.hasPendingCorrection)
        || (skip === 0 && deficit.hasPendingCorrection !== (pending === 1)))
        return invalid();
    return { deficit, skip, take, total, corrections };
}
