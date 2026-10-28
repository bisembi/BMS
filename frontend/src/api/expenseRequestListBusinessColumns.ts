import type { ExpenseRequestListBusinessColumns } from '../types';
import { isPublicExpenseRequestDate, isPublicExpenseRequestText } from './expenseRequestPublicResponse';
const fieldSet = 'closingState|contractName|fullyPaidDate|obligationAmount|obligationAmountKzt|paidAmount|paymentBasisCode|remainingAmount';
const moneyPattern = /^(?:0|[1-9]\d{0,15})\.\d{2}$/u;
const bases = ['Contract', 'InvoiceWithoutContract', 'PublicOffer', 'OtherBasis'];
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
/** Canonical decimal(18,2) has an exact integer-cent representation. */
export function listMoneyCents(value: unknown): bigint | null {
    return typeof value === 'string' && moneyPattern.test(value)
        ? BigInt(value.replace('.', '')) : null;
}
/** Formats exact validated text without a binary64 round trip. */
export function formatListMoney(value: string): string {
    if (listMoneyCents(value) === null)
        throw new Error('Некорректная сумма списка.');
    const [whole, fraction] = value.split('.');
    return `${BigInt(whole).toLocaleString('ru-RU')},${fraction}`;
}
/** Whitelists the complete opt-in shape and its exact monetary invariant. */
export function publicListBusinessColumns(value: unknown, totalAmount: number, totalAmountKzt: number | null): ExpenseRequestListBusinessColumns | null {
    if (!isRecord(value))
        return null;
    const row = value;
    if (Object.keys(row).sort().join('|') !== fieldSet
        || !(row.contractName === null || isPublicExpenseRequestText(row.contractName, 300))
        || !(row.paymentBasisCode === null || (typeof row.paymentBasisCode === 'string' && bases.includes(row.paymentBasisCode)))
        || !(row.fullyPaidDate === null || isPublicExpenseRequestDate(row.fullyPaidDate))
        || !(row.closingState === 'notClosed' || row.closingState === 'partiallyClosed' || row.closingState === 'closed')
        || typeof row.obligationAmount !== 'string' || typeof row.paidAmount !== 'string'
        || typeof row.remainingAmount !== 'string'
        || !(row.obligationAmountKzt === null || typeof row.obligationAmountKzt === 'string'))
        return null;
    const obligation = listMoneyCents(row.obligationAmount);
    const paid = listMoneyCents(row.paidAmount);
    const remaining = listMoneyCents(row.remainingAmount);
    if (obligation === null || paid === null || remaining === null || paid + remaining !== obligation
        || Number(row.obligationAmount) !== totalAmount
        || (row.obligationAmountKzt === null ? totalAmountKzt !== null
            : listMoneyCents(row.obligationAmountKzt) === null || Number(row.obligationAmountKzt) !== totalAmountKzt))
        return null;
    return {
        contractName: row.contractName, paymentBasisCode: row.paymentBasisCode,
        obligationAmount: row.obligationAmount, obligationAmountKzt: row.obligationAmountKzt,
        paidAmount: row.paidAmount, remainingAmount: row.remainingAmount,
        fullyPaidDate: row.fullyPaidDate, closingState: row.closingState,
    };
}
