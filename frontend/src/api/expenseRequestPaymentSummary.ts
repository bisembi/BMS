import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { publicExpenseRequestMoneyCents } from './expenseRequestPublicResponse';
/// The only vocabulary the payment block is allowed to speak. Anything the
/// server might add later is discarded rather than becoming accidental UI data.
export type ExpenseRequestPaymentProgress = 'unpaid' | 'partiallyPaid' | 'paid';
export type ExpenseRequestPaymentSummary = {
    currencyCode: string;
    obligationAmount: number;
    paidAmount: number;
    remainingAmount: number;
    progress: ExpenseRequestPaymentProgress;
    hasConfirmedPaymentFacts: boolean;
    fullyPaidDate: string | null;
    obligationAmountKzt: number | null;
    paidAmountKzt: number | null;
    bankPaymentAmountKzt: number | null;
    currencyDeviationKzt: number | null;
    approvedRoundingAmount?: number;
    cashPaymentAmount?: number;
};
const malformedResponseMessage = 'Некорректный ответ API оплаты заявки.';
const unavailableMessage = 'Сведения об оплате сейчас недоступны.';
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function moneyCents(value: number): number | null {
    return value >= 0 ? publicExpenseRequestMoneyCents(value) : null;
}
function isMoneyAmount(value: unknown): value is number {
    return typeof value === 'number' && moneyCents(value) !== null;
}
function signedMoneyCents(value: unknown): number | null {
    return typeof value === 'number' ? publicExpenseRequestMoneyCents(value) : null;
}
function isIsoDate(value: unknown): value is string {
    if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/u.test(value))
        return false;
    const parsed = new Date(`${value}T00:00:00.000Z`);
    return !Number.isNaN(parsed.getTime())
        && parsed.toISOString().slice(0, 10) === value;
}
function isNullableMoney(value: unknown): value is number | null {
    return value === null || isMoneyAmount(value);
}
function isNullableSignedMoney(value: unknown): value is number | null {
    return value === null || signedMoneyCents(value) !== null;
}
const progressValues: readonly ExpenseRequestPaymentProgress[] = ['unpaid', 'partiallyPaid', 'paid'];
function isPaymentProgress(value: unknown): value is ExpenseRequestPaymentProgress {
    return typeof value === 'string'
        && progressValues.some((progress) => progress === value);
}
/// Exact public parser. Beyond the shape it enforces the arithmetic the
/// backend promises: the three amounts reconcile, and the progress code always
/// agrees with them, so a tampered or half-written payload can never render a
/// misleading payment state.
export function publicExpenseRequestPaymentSummary(payload: unknown): ExpenseRequestPaymentSummary {
    if (!isRecord(payload)
        || typeof payload.currencyCode !== 'string'
        || !/^[A-Z]{3}$/u.test(payload.currencyCode)
        || !isMoneyAmount(payload.obligationAmount)
        || payload.obligationAmount <= 0
        || !isMoneyAmount(payload.paidAmount)
        || !isMoneyAmount(payload.remainingAmount)
        || !isPaymentProgress(payload.progress)
        || typeof payload.hasConfirmedPaymentFacts !== 'boolean'
        || !(payload.fullyPaidDate === null || isIsoDate(payload.fullyPaidDate))
        || !isNullableMoney(payload.obligationAmountKzt)
        || !isNullableMoney(payload.paidAmountKzt)
        || !isNullableMoney(payload.bankPaymentAmountKzt)
        || !isNullableSignedMoney(payload.currencyDeviationKzt)) {
        throw new Error(malformedResponseMessage);
    }
    const { obligationAmount, paidAmount, remainingAmount } = payload;
    const obligationCents = moneyCents(obligationAmount);
    const paidCents = moneyCents(paidAmount);
    const remainingCents = moneyCents(remainingAmount);
    if (obligationCents === null
        || paidCents === null
        || remainingCents === null
        || !Number.isSafeInteger(paidCents + remainingCents)
        || paidCents + remainingCents !== obligationCents
        || paidCents > obligationCents) {
        throw new Error(malformedResponseMessage);
    }
    const expectedProgress: ExpenseRequestPaymentProgress = paidCents === 0
        ? 'unpaid'
        : paidCents === obligationCents
            ? 'paid'
            : 'partiallyPaid';
    if (payload.progress !== expectedProgress) {
        throw new Error(malformedResponseMessage);
    }
    if (expectedProgress === 'paid' && payload.fullyPaidDate === null) {
        throw new Error(malformedResponseMessage);
    }
    const kztValues = [
        payload.obligationAmountKzt,
        payload.paidAmountKzt,
        payload.bankPaymentAmountKzt,
        payload.currencyDeviationKzt,
    ];
    const rounding = payload.approvedRoundingAmount;
    const cash = payload.cashPaymentAmount;
    if (rounding !== undefined || cash !== undefined) {
        if (payload.currencyCode !== 'KZT' || !isMoneyAmount(rounding) || rounding <= 0 || rounding >= 1
            || !isMoneyAmount(cash) || !payload.hasConfirmedPaymentFacts || expectedProgress !== 'paid'
            || moneyCents(cash)! + moneyCents(rounding)! !== paidCents) {
            throw new Error(malformedResponseMessage);
        }
    }
    if (payload.currencyCode === 'KZT') {
        if (kztValues.some(value => value !== null)) {
            throw new Error(malformedResponseMessage);
        }
    }
    else if (payload.obligationAmountKzt === null) {
        if (kztValues.slice(1).some(value => value !== null)) {
            throw new Error(malformedResponseMessage);
        }
    }
    else {
        const derived = kztValues.slice(1);
        const allDerivedNull = derived.every(value => value === null);
        const allDerivedPresent = derived.every(value => value !== null);
        if (payload.obligationAmountKzt <= 0
            || (!allDerivedNull && !allDerivedPresent)) {
            throw new Error(malformedResponseMessage);
        }
        if (allDerivedNull
            && (expectedProgress !== 'paid' || !payload.hasConfirmedPaymentFacts)) {
            throw new Error(malformedResponseMessage);
        }
        if (allDerivedPresent) {
            const { paidAmountKzt, bankPaymentAmountKzt, currencyDeviationKzt } = payload;
            if (paidAmountKzt === null || bankPaymentAmountKzt === null
                || currencyDeviationKzt === null) {
                throw new Error(malformedResponseMessage);
            }
            const obligationKztCents = moneyCents(payload.obligationAmountKzt);
            const paidKztCents = moneyCents(paidAmountKzt);
            const bankKztCents = moneyCents(bankPaymentAmountKzt);
            const deviationKztCents = signedMoneyCents(currencyDeviationKzt);
            if (obligationKztCents === null || paidKztCents === null
                || bankKztCents === null || deviationKztCents === null
                || bankKztCents > paidKztCents
                || !Number.isSafeInteger(paidKztCents - deviationKztCents)) {
                throw new Error(malformedResponseMessage);
            }
            const releasedReserveKztCents = paidKztCents - deviationKztCents;
            if (releasedReserveKztCents < 0
                || releasedReserveKztCents > obligationKztCents
                || (expectedProgress === 'unpaid'
                    && (paidKztCents !== 0 || bankKztCents !== 0
                        || deviationKztCents !== 0 || releasedReserveKztCents !== 0))
                || (expectedProgress === 'paid'
                    && releasedReserveKztCents !== obligationKztCents)) {
                throw new Error(malformedResponseMessage);
            }
        }
    }
    return {
        currencyCode: payload.currencyCode,
        obligationAmount,
        paidAmount,
        remainingAmount,
        progress: expectedProgress,
        hasConfirmedPaymentFacts: payload.hasConfirmedPaymentFacts,
        fullyPaidDate: payload.fullyPaidDate,
        obligationAmountKzt: payload.obligationAmountKzt,
        paidAmountKzt: payload.paidAmountKzt,
        bankPaymentAmountKzt: payload.bankPaymentAmountKzt,
        currencyDeviationKzt: payload.currencyDeviationKzt,
        ...(rounding === undefined ? {} : { approvedRoundingAmount: rounding as number, cashPaymentAmount: cash as number }),
    };
}
export async function getExpenseRequestPaymentSummary(requestId: string): Promise<ExpenseRequestPaymentSummary> {
    let response: Response;
    try {
        response = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${encodeURIComponent(requestId)}/payment-summary`), secureReadInit());
    }
    catch {
        throw new Error(unavailableMessage);
    }
    // A failed read is reported as unavailability, never as "not paid": the
    // block must not turn an outage into a statement about money.
    if (!response.ok) {
        throw new Error(unavailableMessage);
    }
    try {
        return publicExpenseRequestPaymentSummary(await response.json());
    }
    catch {
        throw new Error(unavailableMessage);
    }
}
