import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
export type DebtorPerson470 = {
    key: string;
    name: string;
    requestCount: number;
    knownAmountKzt: string;
    unknownAmountCount: number;
    amountKzt: string | null;
};
export type DebtorsReport470 = {
    generatedAtUtc: string;
    items: DebtorPerson470[];
    requestCount: number;
    knownAmountKzt: string;
    unknownAmountCount: number;
    amountKzt: string | null;
};
export type DebtorsFilters470 = {
    organizationId: string;
    costCenterId: string;
    counterpartyId: string;
    plannedDate: string;
    urgency: '' | 'urgent' | 'regular';
};
const fail = (): never => { throw new Error('Не удалось загрузить суммы по именам.'); };
const record = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
export const debtorCents470 = (v: unknown): bigint | null => typeof v === 'string' && /^(0|[1-9]\d{0,23})\.\d{2}$/u.test(v) ? BigInt(v.replace('.', '')) : null;
const count = (v: unknown) => typeof v === 'number' && Number.isInteger(v) && v >= 0 && v <= 20000;
const fields = (r: Record<string, unknown>, names: string) => Object.keys(r).sort().join('|') === names;
export function publicDebtors470(v: unknown): DebtorsReport470 {
    if (!record(v) || !fields(v, 'amountKzt|generatedAtUtc|items|knownAmountKzt|requestCount|unknownAmountCount')
        || typeof v.generatedAtUtc !== 'string' || !/(Z|\+00:00)$/u.test(v.generatedAtUtc) || !Number.isFinite(Date.parse(v.generatedAtUtc))
        || !Array.isArray(v.items) || v.items.length > 20000 || !count(v.requestCount) || !count(v.unknownAmountCount)
        || debtorCents470(v.knownAmountKzt) === null || v.amountKzt !== (v.unknownAmountCount === 0 ? v.knownAmountKzt : null))
        return fail();
    let requests = 0, unknown = 0, known = 0n;
    const keys = new Set<string>();
    for (const item of v.items) {
        if (!record(item) || !fields(item, 'amountKzt|key|knownAmountKzt|name|requestCount|unknownAmountCount')
            || typeof item.key !== 'string' || !(/^[A-F0-9]{64}$/u).test(item.key) || keys.has(item.key)
            || typeof item.name !== 'string' || !item.name.trim() || item.name !== item.name.trim() || item.name.length > 256 || /[\u0000-\u001f\u007f-\u009f]/u.test(item.name) || /^S-1-/iu.test(item.name)
            || !count(item.requestCount) || item.requestCount === 0 || !count(item.unknownAmountCount)
            || Number(item.unknownAmountCount) > Number(item.requestCount) || debtorCents470(item.knownAmountKzt) === null
            || item.amountKzt !== (item.unknownAmountCount === 0 ? item.knownAmountKzt : null))
            return fail();
        keys.add(item.key);
        requests += Number(item.requestCount);
        unknown += Number(item.unknownAmountCount);
        known += debtorCents470(item.knownAmountKzt)!;
    }
    if (requests !== v.requestCount || unknown !== v.unknownAmountCount || known !== debtorCents470(v.knownAmountKzt))
        return fail();
    return v as DebtorsReport470;
}
export async function getDebtors470(f: DebtorsFilters470, signal?: AbortSignal): Promise<DebtorsReport470> {
    const p = new URLSearchParams();
    for (const k of ['organizationId', 'costCenterId', 'counterpartyId'] as const)
        if (f[k])
            p.set(k, f[k]);
    if (f.plannedDate)
        p.set('plannedPaymentDate', f.plannedDate);
    if (f.urgency)
        p.set('isUrgent', String(f.urgency === 'urgent'));
    const r = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/debtors/by-initiator?${p}`), { ...secureReadInit(), signal });
    if (r.status !== 200)
        return fail();
    return publicDebtors470(await r.json());
}
