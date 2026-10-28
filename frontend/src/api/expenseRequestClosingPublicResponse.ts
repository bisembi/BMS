import { isExpenseRequestStatus } from '../expenseRequestStatus';
import type { ExpenseRequestStatus } from '../types';
import { isPublicExpenseRequestDate, isPublicExpenseRequestMoney, isPublicExpenseRequestTimestamp, isPublicExpenseRequestUuid, publicExpenseRequestMoneyCents } from './expenseRequestPublicResponse';
export type ExpenseRequestClosingLine = {
    lineId: string;
    sortOrder: number;
    originalAmount: number;
    originalAmountKzt: number;
    confirmedClosedAmount: number;
    confirmedClosedAmountKzt: number;
    remainingAmount: number;
    remainingAmountKzt: number;
};
export type ExpenseRequestClosingState = {
    expenseRequestId: string;
    status: ExpenseRequestStatus;
    currencyCode: string;
    fullyPaidAtUtc: string | null;
    latestDocumentsReceivedDate: string | null;
    confirmedClosingControlAmount: number;
    closedPartially: boolean;
    canExecuteClosingCommands: boolean;
    concurrencyStamp: string;
    lines: ExpenseRequestClosingLine[];
};
export const invalidExpenseRequestClosingResponseMessage = 'Некорректный ответ API закрытия заявки.';
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function cents(value: number): number | null {
    return publicExpenseRequestMoneyCents(value);
}
function sumCents(values: number[]): number | null {
    let total = 0;
    for (const value of values) {
        const current = cents(value);
        if (current === null || !Number.isSafeInteger(total + current))
            return null;
        total += current;
    }
    return total;
}
function nullableDate(value: unknown): value is string | null {
    return value === null || isPublicExpenseRequestDate(value);
}
function nullableTimestamp(value: unknown): value is string | null {
    return value === null || isPublicExpenseRequestTimestamp(value);
}
function publicClosingLine(value: unknown): ExpenseRequestClosingLine | null {
    if (!isRecord(value)
        || !isPublicExpenseRequestUuid(value.lineId)
        || typeof value.sortOrder !== 'number'
        || !Number.isSafeInteger(value.sortOrder)
        || value.sortOrder <= 0
        || !isPublicExpenseRequestMoney(value.originalAmount, false)
        || !isPublicExpenseRequestMoney(value.originalAmountKzt)
        || !isPublicExpenseRequestMoney(value.confirmedClosedAmount)
        || !isPublicExpenseRequestMoney(value.confirmedClosedAmountKzt)
        || !isPublicExpenseRequestMoney(value.remainingAmount)
        || !isPublicExpenseRequestMoney(value.remainingAmountKzt)) {
        return null;
    }
    const original = cents(value.originalAmount);
    const originalKzt = cents(value.originalAmountKzt);
    const confirmed = cents(value.confirmedClosedAmount);
    const confirmedKzt = cents(value.confirmedClosedAmountKzt);
    const remaining = cents(value.remainingAmount);
    const remainingKzt = cents(value.remainingAmountKzt);
    if (original === null
        || originalKzt === null
        || confirmed === null
        || confirmedKzt === null
        || remaining === null
        || remainingKzt === null
        || confirmed + remaining !== original
        || confirmedKzt + remainingKzt !== originalKzt
        || (remaining === 0 && remainingKzt !== 0)) {
        return null;
    }
    return {
        lineId: value.lineId,
        sortOrder: value.sortOrder,
        originalAmount: value.originalAmount,
        originalAmountKzt: value.originalAmountKzt,
        confirmedClosedAmount: value.confirmedClosedAmount,
        confirmedClosedAmountKzt: value.confirmedClosedAmountKzt,
        remainingAmount: value.remainingAmount,
        remainingAmountKzt: value.remainingAmountKzt,
    };
}
export function publicExpenseRequestClosingState(payload: unknown, expectedRequestId?: string): ExpenseRequestClosingState {
    if (!isRecord(payload)
        || !isPublicExpenseRequestUuid(payload.expenseRequestId)
        || (expectedRequestId !== undefined
            && (!isPublicExpenseRequestUuid(expectedRequestId)
                || payload.expenseRequestId.toLocaleLowerCase('en-US')
                    !== expectedRequestId.toLocaleLowerCase('en-US')))
        || !isExpenseRequestStatus(payload.status)
        || typeof payload.currencyCode !== 'string'
        || !/^[A-Z]{3}$/u.test(payload.currencyCode)
        || !nullableTimestamp(payload.fullyPaidAtUtc)
        || !nullableDate(payload.latestDocumentsReceivedDate)
        || !isPublicExpenseRequestMoney(payload.confirmedClosingControlAmount)
        || typeof payload.closedPartially !== 'boolean'
        || typeof payload.canExecuteClosingCommands !== 'boolean'
        || !isPublicExpenseRequestUuid(payload.concurrencyStamp)
        || !Array.isArray(payload.lines)) {
        throw new Error(invalidExpenseRequestClosingResponseMessage);
    }
    const lines: ExpenseRequestClosingLine[] = [];
    const lineIds = new Set<string>();
    let previousSortOrder = 0;
    for (const candidate of payload.lines) {
        const line = publicClosingLine(candidate);
        const canonicalId = line?.lineId.toLocaleLowerCase('en-US') ?? '';
        if (line === null
            || line.sortOrder <= previousSortOrder
            || lineIds.has(canonicalId)) {
            throw new Error(invalidExpenseRequestClosingResponseMessage);
        }
        lineIds.add(canonicalId);
        previousSortOrder = line.sortOrder;
        lines.push(line);
    }
    const control = cents(payload.confirmedClosingControlAmount);
    const confirmed = sumCents(lines.map((line) => line.confirmedClosedAmount));
    const remaining = sumCents(lines.map((line) => line.remainingAmount));
    if (control === null
        || confirmed === null
        || remaining === null
        || control < confirmed
        || (payload.closedPartially
            && (payload.status !== 'PAID'
                || control <= 0
                || payload.fullyPaidAtUtc === null
                || payload.latestDocumentsReceivedDate === null))
        || (payload.status === 'CLOSED'
            && (payload.closedPartially
                || payload.fullyPaidAtUtc === null
                || payload.latestDocumentsReceivedDate === null
                || remaining !== 0))
        || (payload.canExecuteClosingCommands
            && payload.status !== 'PAID'
            && payload.status !== 'CLOSED')
        || (payload.latestDocumentsReceivedDate !== null
            && payload.fullyPaidAtUtc === null)
        || (control > 0 && payload.latestDocumentsReceivedDate === null)
        || (payload.currencyCode === 'KZT'
            && lines.some((line) => cents(line.originalAmount) !== cents(line.originalAmountKzt)))) {
        throw new Error(invalidExpenseRequestClosingResponseMessage);
    }
    return {
        expenseRequestId: payload.expenseRequestId,
        status: payload.status,
        currencyCode: payload.currencyCode,
        fullyPaidAtUtc: payload.fullyPaidAtUtc,
        latestDocumentsReceivedDate: payload.latestDocumentsReceivedDate,
        confirmedClosingControlAmount: payload.confirmedClosingControlAmount,
        closedPartially: payload.closedPartially,
        canExecuteClosingCommands: payload.canExecuteClosingCommands,
        concurrencyStamp: payload.concurrencyStamp,
        lines,
    };
}
