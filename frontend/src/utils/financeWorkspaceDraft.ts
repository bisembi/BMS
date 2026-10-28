import type { FinanceWorkspaceId } from '../financeWorkspaces';
import { listMoneyCents } from '../api/expenseRequestListBusinessColumns';
import { canonicalMoneyText } from './moneyInput';
const wholeAmount = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 });
export function formatFinanceAmount(value: number | string | null | undefined): string {
    if (typeof value === 'string') {
        const cents = listMoneyCents(value);
        // Display whole units without losing integer precision in decimal(18,2).
        // This is display rounding only; the canonical source remains unchanged.
        return cents === null ? '—' : wholeAmount.format((cents + 50n) / 100n);
    }
    return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? wholeAmount.format(value) : '—';
}
export type FinancePreparation = {
    operationDate: string;
    periodFrom: string;
    periodTo: string;
    organizationId: string;
    counterpartyId: string;
    currency: string;
    amount: string;
    sourceNumber: string;
    secondBasis: string;
    reason: string;
    relatedRequestTitle: string;
};
export const emptyFinancePreparation: FinancePreparation = {
    operationDate: '', periodFrom: '', periodTo: '', organizationId: '', counterpartyId: '',
    currency: '', amount: '', sourceNumber: '', secondBasis: '', reason: '', relatedRequestTitle: '',
};
export function validFinanceDate(value: string): boolean {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith('0000'))
        return false;
    const date = new Date(`${value}T00:00:00Z`);
    return Number.isFinite(date.valueOf()) && date.toISOString().slice(0, 10) === value;
}
export function canonicalPreparationAmount(value: string): string | null {
    const compact = canonicalMoneyText(value);
    if (compact === null || !/^\d{1,16}(?:\.\d{1,2})?$/.test(compact))
        return null;
    const [whole, fractional = ''] = compact.split('.');
    const cents = BigInt(whole) * 100n + BigInt(fractional.padEnd(2, '0'));
    return cents > 0n ? `${cents / 100n}.${String(cents % 100n).padStart(2, '0')}` : null;
}
export function validateFinancePreparation(value: FinancePreparation, id: FinanceWorkspaceId): string[] {
    const errors: string[] = [];
    if (!validFinanceDate(value.operationDate))
        errors.push('Укажите корректную дату документа.');
    if (!value.organizationId)
        errors.push('Выберите организацию.');
    if (!value.counterpartyId)
        errors.push('Выберите контрагента.');
    if (!/^[A-Z]{3}$/.test(value.currency))
        errors.push('Выберите валюту.');
    if (!canonicalPreparationAmount(value.amount))
        errors.push('Укажите положительную сумму, не более двух знаков после запятой.');
    if (!value.reason.trim())
        errors.push('Заполните основание или пояснение.');
    if (id === 'reconciliations') {
        if (!validFinanceDate(value.periodFrom) || !validFinanceDate(value.periodTo))
            errors.push('Укажите начало и конец периода сверки.');
        else if (value.periodFrom > value.periodTo)
            errors.push('Конец периода не может быть раньше начала.');
    }
    if (id === 'debt-offsets' && !value.secondBasis.trim())
        errors.push('Укажите встречное основание для обсуждения взаимозачёта.');
    if ((id === 'cashless-receipts' || id === 'cashless-withdrawals') && !value.sourceNumber.trim())
        errors.push('Укажите номер документа-основания.');
    if (Object.values(value).some(field => /[\u0000-\u001f\u007f-\u009f]/u.test(field.replace(/[\r\n]/g, ''))))
        errors.push('Уберите управляющие символы из полей.');
    return errors;
}
// All amounts remain decimal strings. This is an explicitly unposted local
// document, not an import format, saved business document or write API payload.
export function financePreparationDocument(id: FinanceWorkspaceId, value: FinancePreparation, labels: {
    organization: string;
    counterparty: string;
}, basis: string | null) {
    const errors = validateFinancePreparation(value, id);
    if (errors.length)
        throw new Error('Черновик не заполнен.');
    return {
        format: 'BMS_FRONTEND_PREPARATION_V1', posted: false, storedInBms: false, workspace: id,
        preparedAtUtc: new Date().toISOString(), operationDate: value.operationDate,
        organization: labels.organization, counterparty: labels.counterparty, currency: value.currency,
        amount: canonicalPreparationAmount(value.amount),
        ...(id === 'reconciliations' ? { periodFrom: value.periodFrom, periodTo: value.periodTo } : {}),
        sourceNumber: value.sourceNumber.trim(), relatedRequest: basis, secondBasis: value.secondBasis.trim(), reason: value.reason.trim(),
        notice: 'Локальный неподписанный черновик. Не сохранён в BMS, не проведён и не является банковским документом.',
    };
}
