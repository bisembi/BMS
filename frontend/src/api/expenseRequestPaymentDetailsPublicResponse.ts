import { isPublicExpenseRequestMoney, isPublicExpenseRequestTimestamp } from './expenseRequestPublicResponse';
export type PaymentDetail = {
    position: number;
    paidAt: string;
    paymentNumber: string | null;
    amount: number;
    amountKzt: number;
    isReversal: boolean;
};
export type PaymentDetailsTotals = {
    originalAmount: number;
    originalAmountKzt: number;
    reversedAmount: number;
    reversedAmountKzt: number;
    netAmount: number;
    netAmountKzt: number;
};
export type ExpenseRequestPaymentDetails = {
    currencyCode: string;
    total: number;
    skip: number;
    take: number;
    totals: PaymentDetailsTotals;
    items: PaymentDetail[];
};
const invalid = 'Не удалось получить корректные строки платежей.';
const controls = /[\u0000-\u001f\u007f-\u009f]/u;
const instant = /^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,3})?(?:Z|[+-](?:0\d|1[0-3]):[0-5]\d|[+-]14:00)$/u;
// At 2**46 binary64 spacing exceeds one cent: JSON can already have merged
// adjacent decimal amounts before these checks. This is not a budget limit.
const exclusiveCentPrecisionLimit = 2 ** 46;
function record(v: unknown): v is Record<string, unknown> { return typeof v === 'object' && v !== null && !Array.isArray(v); }
function integer(v: unknown): v is number { return typeof v === 'number' && Number.isInteger(v) && v >= 0 && v <= 2147483647; }
function money(v: unknown, zero = true): v is number {
    return isPublicExpenseRequestMoney(v, zero) && v < exclusiveCentPrecisionLimit
        && Number.isSafeInteger(Math.round(v * 100)) && Math.round(v * 100) / 100 === v;
}
const cents = (v: number) => Math.round(v * 100);
function timestamp(v: unknown): v is string {
    return isPublicExpenseRequestTimestamp(v) && instant.test(v) && v.slice(0, 4) !== '0000';
}
function number(v: unknown): v is string | null {
    // Business numbers are not identities: preserve a valid source number,
    // including surrounding spaces, without substituting any technical id.
    return v === null || (typeof v === 'string' && v.trim().length > 0 && v.length <= 100 && !controls.test(v));
}
function totals(v: unknown): PaymentDetailsTotals {
    if (!record(v) || !money(v.originalAmount) || !money(v.originalAmountKzt)
        || !money(v.reversedAmount) || !money(v.reversedAmountKzt) || !money(v.netAmount) || !money(v.netAmountKzt)
        || cents(v.originalAmount) - cents(v.reversedAmount) !== cents(v.netAmount)
        || cents(v.originalAmountKzt) - cents(v.reversedAmountKzt) !== cents(v.netAmountKzt)
        || (v.originalAmount === 0) !== (v.originalAmountKzt === 0)
        || (v.reversedAmount === 0) !== (v.reversedAmountKzt === 0)
        || (v.netAmount === 0) !== (v.netAmountKzt === 0))
        throw new Error(invalid);
    return { originalAmount: v.originalAmount, originalAmountKzt: v.originalAmountKzt,
        reversedAmount: v.reversedAmount, reversedAmountKzt: v.reversedAmountKzt,
        netAmount: v.netAmount, netAmountKzt: v.netAmountKzt };
}
export function publicExpenseRequestPaymentDetails(v: unknown, skip = 0, take = 25): ExpenseRequestPaymentDetails {
    if (!record(v) || typeof v.currencyCode !== 'string' || !/^[A-Z]{3}$/u.test(v.currencyCode)
        || !integer(v.total) || !integer(v.skip) || !integer(v.take) || v.take < 1 || v.take > 100
        || v.skip !== skip || v.take !== take || !Array.isArray(v.items)
        || v.items.length !== Math.min(take, Math.max(0, v.total - skip)))
        throw new Error(invalid);
    const sum = totals(v.totals);
    if ((v.total === 0) !== (sum.originalAmount === 0)
        || (v.currencyCode === 'KZT' && (sum.originalAmount !== sum.originalAmountKzt
            || sum.reversedAmount !== sum.reversedAmountKzt || sum.netAmount !== sum.netAmountKzt)))
        throw new Error(invalid);
    let previousInstant = -Infinity;
    let original = 0, originalKzt = 0, reversed = 0, reversedKzt = 0;
    const items: PaymentDetail[] = v.items.map((item: unknown, index) => {
        if (!record(item) || item.position !== skip + index + 1 || !timestamp(item.paidAt)
            || !number(item.paymentNumber) || !money(item.amount, false) || !money(item.amountKzt, false)
            || typeof item.isReversal !== 'boolean' || (v.currencyCode === 'KZT' && item.amount !== item.amountKzt))
            throw new Error(invalid);
        const at = Date.parse(item.paidAt);
        if (at < previousInstant)
            throw new Error(invalid);
        previousInstant = at;
        if (item.isReversal) {
            reversed += cents(item.amount);
            reversedKzt += cents(item.amountKzt);
        }
        else {
            original += cents(item.amount);
            originalKzt += cents(item.amountKzt);
        }
        return { position: skip + index + 1, paidAt: item.paidAt, paymentNumber: item.paymentNumber,
            amount: item.amount, amountKzt: item.amountKzt, isReversal: item.isReversal };
    });
    if (![original, originalKzt, reversed, reversedKzt].every(Number.isSafeInteger)
        || original > cents(sum.originalAmount) || originalKzt > cents(sum.originalAmountKzt)
        || reversed > cents(sum.reversedAmount) || reversedKzt > cents(sum.reversedAmountKzt))
        throw new Error(invalid);
    if (skip === 0 && items.length === v.total && (original !== cents(sum.originalAmount)
        || originalKzt !== cents(sum.originalAmountKzt) || reversed !== cents(sum.reversedAmount)
        || reversedKzt !== cents(sum.reversedAmountKzt)))
        throw new Error(invalid);
    return { currencyCode: v.currencyCode, total: v.total, skip: v.skip, take: v.take, totals: sum, items };
}
