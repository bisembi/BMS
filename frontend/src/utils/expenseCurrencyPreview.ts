import type { ExpenseRequestDraft } from '../types';
import type { CurrencyRateRow } from '../api/currencyRates';
import { isValidPositiveMoneyInput } from '../api/expenseRequestCalculations';
// Display-only preview, never sent as a trusted conversion. Integer arithmetic
// matches the server's per-line rounding to cents (away from zero).
export function expenseCurrencyPreview(draft: ExpenseRequestDraft, rate: CurrencyRateRow | null): number | null {
    if (draft.currencyCode === 'KZT' || rate?.code !== draft.currencyCode || !rate.rate || !rate.multiplicity
        || !Number.isFinite(rate.rate) || rate.rate <= 0 || !Number.isSafeInteger(rate.multiplicity) || rate.multiplicity <= 0
        || draft.lines.length === 0 || draft.lines.some(line => !isValidPositiveMoneyInput(line.amount)))
        return null;
    const rateText = rate.rate.toFixed(6);
    if (Number(rateText) !== rate.rate || !/^\d+\.\d{6}$/u.test(rateText))
        return null;
    const numeratorRate = BigInt(rateText.replace('.', ''));
    const divisor = 1000000n * BigInt(rate.multiplicity);
    let totalCents = 0n;
    for (const line of draft.lines) {
        const [whole, fraction = ''] = line.amount.split('.');
        const sourceCents = BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
        const numerator = sourceCents * numeratorRate;
        const cents = (numerator + divisor / 2n) / divisor;
        totalCents += cents;
        if (totalCents > 9007199254740991n)
            return null;
    }
    const amount = Number(totalCents) / 100;
    const serialized = /^(\d+)(?:\.(\d{1,2}))?$/u.exec(JSON.stringify(amount));
    if (!serialized || BigInt(serialized[1]) * 100n + BigInt((serialized[2] ?? '').padEnd(2, '0')) !== totalCents)
        return null;
    return amount;
}
