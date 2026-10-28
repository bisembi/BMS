import { useEffect, useRef, useState } from 'react';
import type { PreliminaryCurrencyCalculation, SavedExpenseRequest } from '../types';
import type { CurrencyRateRow } from '../api/currencyRates';
import { refreshExpenseRequestCurrency } from '../api/expenseRequestCurrencyRefresh';
import { expenseCurrencyRefreshErrorMessage } from '../utils/uiErrorMessages';
// Sharing an in-flight write makes StrictMode's effect replay safe. Never abort
// a write and pretend it failed: the server may already have committed it.
const pending = new Map<string, Promise<SavedExpenseRequest>>();
export function useExpenseCurrencyRefresh(input: {
    id: string | null;
    version: string | null;
    actor: string | null;
    enabled: boolean;
    currencyCode: string;
    calculation: PreliminaryCurrencyCalculation | null;
    rate: CurrencyRateRow | null;
    onRefreshed: (saved: SavedExpenseRequest) => void;
}) {
    const { id, version, actor, enabled, currencyCode, calculation, rate } = input;
    const callback = useRef(input.onRefreshed);
    callback.current = input.onRefreshed;
    const latestIdentity = useRef({ id, version, actor });
    latestIdentity.current = { id, version, actor };
    const [attempt, setAttempt] = useState(0);
    const [result, setResult] = useState<{
        key: string;
        error: string | null;
    } | null>(null);
    const attempted = useRef('');
    const needsRefresh = Boolean(id && version && actor && currencyCode !== 'KZT' && rate?.code === currencyCode
        && rate.rate && rate.multiplicity && (calculation?.currencyCode !== currencyCode
        || calculation.rateDate !== rate.rateDate || calculation.rate !== rate.rate
        || calculation.multiplicity !== rate.multiplicity || calculation.totalAmountKzt === null));
    const key = needsRefresh ? JSON.stringify([id, version, actor, rate?.stamp, rate?.rateDate, rate?.rate, rate?.multiplicity, attempt]) : '';
    const busy = Boolean(key && enabled && result?.key !== key);
    const command = useRef<{
        key: string;
        idempotencyKey: string;
    } | null>(null);
    useEffect(() => {
        if (!key || !enabled || !id || !version)
            return;
        if (result?.key === key)
            return;
        // Do not retry automatically after a network error or incomplete draft.
        if (attempted.current === key && !pending.has(key))
            return;
        attempted.current = key;
        const commandKey = JSON.stringify([id, version, actor, rate?.rateDate, rate?.rate, rate?.multiplicity]);
        if (command.current?.key !== commandKey)
            command.current = { key: commandKey, idempotencyKey: crypto.randomUUID() };
        let work = pending.get(key);
        if (!work) {
            work = refreshExpenseRequestCurrency(id, version, command.current.idempotencyKey);
            pending.set(key, work);
            void work.finally(() => {
                if (pending.get(key) === work)
                    pending.delete(key);
            }).catch(() => { });
        }
        let cancelled = false;
        void work.then(saved => {
            if (cancelled || latestIdentity.current.id !== id || latestIdentity.current.version !== version
                || latestIdentity.current.actor !== actor)
                return;
            if (saved.currencyCode !== currencyCode || !['DRAFT', 'RETURNED'].includes(saved.status))
                throw new Error('Валютный расчёт не соответствует открытой заявке. Обновите карточку.');
            callback.current(saved);
            setResult({ key, error: null });
        }).catch((error: unknown) => {
            if (!cancelled)
                setResult({ key, error: expenseCurrencyRefreshErrorMessage(error) });
        });
        return () => {
            cancelled = true;
            if (attempted.current === key)
                attempted.current = '';
        };
    }, [key, enabled, id, version, actor, currencyCode]);
    return { busy, error: result?.key === key ? result.error : null,
        // Same key on retry preserves idempotency after an uncertain response.
        retry: () => { attempted.current = ''; setResult(null); setAttempt(n => n + 1); } };
}
