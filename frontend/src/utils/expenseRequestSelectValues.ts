import type { CounterpartyResidencyCode, PaymentBasisCode, VatRateCode } from '../types';
import { isExpenseRequestStatus } from '../expenseRequestStatus';
import type { ExpenseRequestStatusFilter, ExpenseRequestUrgencyFilter } from './expenseRequestListFilters';
export function parseExpenseRequestUrgencyFilter(value: string): ExpenseRequestUrgencyFilter {
    return value === 'urgent' || value === 'normal' ? value : 'all';
}
export function parseExpenseRequestStatusFilter(value: string): ExpenseRequestStatusFilter {
    return isExpenseRequestStatus(value) ? value : '';
}
export function parsePaymentBasisCode(value: string): PaymentBasisCode | '' {
    return value === 'Contract'
        || value === 'InvoiceWithoutContract'
        || value === 'PublicOffer'
        || value === 'OtherBasis'
        ? value
        : '';
}
export function parseCounterpartyResidencyCode(value: string): CounterpartyResidencyCode | '' {
    return value === 'ResidentKz' || value === 'NonResident' ? value : '';
}
export function parseVatRateCode(value: string): VatRateCode | null {
    return value === 'Vat16' || value === 'WithoutVat' ? value : null;
}
