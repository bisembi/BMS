import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { isPublicExpenseRequestUuid as uuid } from './expenseRequestPublicResponse';
export type ExpensePaymentRegisterRow = {
    expenseRequestId: string;
    organizationName: string;
    plannedPaymentDate: string | null;
    costCenterName: string;
    initiatorName: string;
    counterpartyName: string;
    currencyCode: string;
    amount: string;
    equivalentKzt: string | null;
};
export type ExpensePaymentRegister = {
    generatedAtUtc: string;
    items: ExpensePaymentRegisterRow[];
    currencyTotals: {
        currencyCode: string;
        amount: string;
    }[];
    totalEquivalentKzt: string | null;
    hasUnknownEquivalents: boolean;
};
const record = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const money = (v: unknown): v is string => typeof v === 'string' && /^(?:0|[1-9]\d{0,23})\.\d{2}$/u.test(v);
const cents = (v: string) => BigInt(v.replace('.', ''));
const text = (v: unknown, max = 2000): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= max && !/[\u0000-\u001f\u007f]/u.test(v);
export function publicExpensePaymentRegister(value: unknown): ExpensePaymentRegister {
    const fail = () => { throw new Error('Не удалось подтвердить данные реестра. Обновите его.'); };
    if (!record(value) || typeof value.generatedAtUtc !== 'string' || !/^\d{4}-\d{2}-\d{2}T.*(?:Z|\+00:00)$/u.test(value.generatedAtUtc)
        || !Number.isFinite(Date.parse(value.generatedAtUtc)) || !Array.isArray(value.items) || !Array.isArray(value.currencyTotals)
        || typeof value.hasUnknownEquivalents !== 'boolean')
        return fail();
    const ids = new Set<string>(), totals = new Map<string, bigint>();
    let equivalent = 0n, unknown = false;
    for (const row of value.items) {
        if (!record(row) || !uuid(row.expenseRequestId) || ids.has(row.expenseRequestId.toLowerCase())
            || ![row.organizationName, row.costCenterName, row.initiatorName].every(value => text(value)) || !text(row.counterpartyName, 6500)
            || typeof row.currencyCode !== 'string' || !/^[A-Z]{3}$/u.test(row.currencyCode) || !money(row.amount)
            || !(row.equivalentKzt === null || money(row.equivalentKzt)))
            return fail();
        if (row.plannedPaymentDate !== null && (typeof row.plannedPaymentDate !== 'string' || !/^\d{4}-\d{2}-\d{2}$/u.test(row.plannedPaymentDate)
            || !Number.isFinite(Date.parse(row.plannedPaymentDate + 'T00:00:00Z'))
            || new Date(row.plannedPaymentDate + 'T00:00:00Z').toISOString().slice(0, 10) !== row.plannedPaymentDate))
            return fail();
        ids.add(row.expenseRequestId.toLowerCase());
        totals.set(row.currencyCode, (totals.get(row.currencyCode) ?? 0n) + cents(row.amount));
        if (row.equivalentKzt === null)
            unknown = true;
        else
            equivalent += cents(row.equivalentKzt);
    }
    const seen = new Set<string>();
    for (const total of value.currencyTotals) {
        if (!record(total) || typeof total.currencyCode !== 'string' || seen.has(total.currencyCode)
            || !money(total.amount) || totals.get(total.currencyCode) !== cents(total.amount))
            return fail();
        seen.add(total.currencyCode);
    }
    if (seen.size !== totals.size || value.hasUnknownEquivalents !== unknown
        || (unknown ? value.totalEquivalentKzt !== null : !money(value.totalEquivalentKzt) || cents(value.totalEquivalentKzt) !== equivalent))
        return fail();
    return value as ExpensePaymentRegister;
}
export function formatRegisterMoney(value: string): string {
    const [whole, fraction] = value.split('.');
    return `${BigInt(whole).toLocaleString('ru-RU')},${fraction}`;
}
export async function getExpensePaymentRegister(signal: AbortSignal): Promise<ExpensePaymentRegister> {
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/expense-requests/payment-register'), { ...secureReadInit(), signal });
    if (!response.ok)
        throw new Error('Не удалось загрузить реестр заявок к оплате.');
    return publicExpensePaymentRegister(await response.json());
}
