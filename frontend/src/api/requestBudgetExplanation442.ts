import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { isPublicExpenseRequestUuid as uuid, isPublicExpenseRequestDate as validDay } from './expenseRequestPublicResponse';
import { publicBddsDrill, type BddsDrillPage } from './bddsDrill';
import type { ExpenseBudgetDetailContext440 } from '../utils/expenseBudgetDetail440';
export type RequestBudgetContext442 = ExpenseBudgetDetailContext440 & {
    requestId: string;
    version: string;
    expectedAfter: number | null;
};
export type RequestReserve442 = {
    requestId: string;
    requestNumber: string | null;
    plannedPaymentDate: string;
    counterparty: string | null;
    amountKzt: number;
    isCurrent: boolean;
    comment: string | null;
};
export type RequestBudgetExplanation442 = {
    requestId: string;
    requestVersion: string;
    cashFlowItemId: string;
    costCenterId: string;
    budgetMonth: string;
    revision: number;
    snapshotId: string;
    budgetInstanceNumber: string | null;
    limitKzt: number;
    paidIncludedKzt: number;
    reservedBeforeKzt: number;
    ownReservedKzt: number;
    ownPaidKzt: number;
    availableBeforeKzt: number;
    availableAfterKzt: number;
    laterPaidKzt: number;
    reservations: RequestReserve442[];
    reserveTotal: number;
    reserveSkip: number;
    hiddenReserveKzt: number;
    facts: BddsDrillPage;
};
const fail = (): never => { throw new Error('Расшифровка бюджета недоступна. Обновите заявку и повторите попытку.'); };
// Keep HTTP classification without exposing server details or treating denied access as stale data.
export class RequestBudgetExplanationHttpError442 extends Error {
    constructor(readonly status: number) { super('Расшифровка бюджета недоступна. Обновите заявку и повторите попытку.'); }
}
const record = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const integer = (v: unknown): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0;
const text = (v: unknown): v is string | null => v === null || typeof v === 'string' && v.length <= 1000 && !/[\u0000-\u001f\u007f-\u009f]/u.test(v);
// Initiator comments are free text, not directory labels: preserve newlines and tabs.
const comment = (v: unknown): v is string | null => v === null || typeof v === 'string' && v.length <= 1000 && !/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/u.test(v);
export function budgetCents442(v: unknown): number {
    if (typeof v !== 'number' || !Number.isFinite(v) || !/^-?\d+(?:\.\d{1,2})?$/u.test(String(v)))
        return fail();
    const [whole, fraction = ''] = String(Math.abs(v)).split('.');
    const result = BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
    if (result > BigInt(Number.MAX_SAFE_INTEGER))
        return fail();
    return Number(result) * (v < 0 ? -1 : 1);
}
export function publicRequestBudgetExplanation442(value: unknown, context: RequestBudgetContext442, factSkip = 0, reserveSkip = 0, revision?: number): RequestBudgetExplanation442 {
    if (!record(value) || value.requestId !== context.requestId || value.requestVersion !== context.version
        || value.costCenterId !== context.key.costCenterId || value.cashFlowItemId !== context.key.cashFlowItemId
        || value.budgetMonth !== context.key.budgetMonth || !uuid(value.snapshotId) || !integer(value.revision)
        || revision !== undefined && value.revision !== revision || !text(value.budgetInstanceNumber)
        || value.reserveSkip !== reserveSkip || !integer(value.reserveTotal) || value.reserveTotal > 20000
        || !Array.isArray(value.reservations) || value.reservations.length > 50
        || value.reservations.length !== Math.min(50, Math.max(0, value.reserveTotal - reserveSkip)))
        return fail();
    for (const key of ['limitKzt', 'paidIncludedKzt', 'reservedBeforeKzt', 'ownReservedKzt', 'ownPaidKzt', 'laterPaidKzt', 'hiddenReserveKzt'])
        if (budgetCents442(value[key]) < 0)
            return fail();
    const limit = budgetCents442(value.limitKzt), paid = budgetCents442(value.paidIncludedKzt), reserve = budgetCents442(value.reservedBeforeKzt) + budgetCents442(value.ownReservedKzt);
    if (!Number.isSafeInteger(reserve) || !Number.isSafeInteger(limit - paid - reserve) || paid < budgetCents442(value.ownPaidKzt) || budgetCents442(value.availableAfterKzt) !== limit - paid - reserve
        || budgetCents442(value.availableBeforeKzt) !== limit - (paid - budgetCents442(value.ownPaidKzt)) - budgetCents442(value.reservedBeforeKzt))
        return fail();
    const seen = new Set<string>();
    let sum = 0;
    const reservations = value.reservations.map(row => {
        if (!record(row) || !uuid(row.requestId) || seen.has(row.requestId) || !text(row.requestNumber) || !text(row.counterparty) || !comment(row.comment)
            || !validDay(row.plannedPaymentDate) || typeof row.isCurrent !== 'boolean' || row.isCurrent !== (row.requestId === context.requestId)
            || budgetCents442(row.amountKzt) <= 0)
            return fail();
        seen.add(row.requestId);
        sum += budgetCents442(row.amountKzt);
        return row as RequestReserve442;
    });
    if (!Number.isSafeInteger(sum) || !Number.isSafeInteger(sum + budgetCents442(value.hiddenReserveKzt)) || sum + budgetCents442(value.hiddenReserveKzt) > reserve
        || value.reserveTotal === reservations.length && sum + budgetCents442(value.hiddenReserveKzt) !== reserve)
        return fail();
    const facts = publicBddsDrill(value.facts, { year: Number(context.key.budgetMonth.slice(0, 4)), fromMonth: context.key.budgetMonth, toMonth: context.key.budgetMonth,
        snapshotId: value.snapshotId, costCenterId: context.key.costCenterId, versionId: null, articleIds: [context.key.cashFlowItemId], skip: factSkip, take: 50 });
    budgetCents442(facts.totalKzt);
    for (const row of facts.items) {
        budgetCents442(row.amountKzt);
        if (!row.entryDateLocal.startsWith(context.key.budgetMonth.slice(0, 7)))
            return fail();
    }
    if (facts.total === facts.items.length && facts.items.reduce((s, r) => s + budgetCents442(r.amountKzt), 0) !== budgetCents442(facts.totalKzt))
        return fail();
    return { ...value, reservations, facts } as RequestBudgetExplanation442;
}
export async function getRequestBudgetExplanation442(context: RequestBudgetContext442, factSkip: number, reserveSkip: number, signal: AbortSignal, revision?: number): Promise<RequestBudgetExplanation442> {
    if (!uuid(context.requestId) || !uuid(context.version) || !uuid(context.key.costCenterId) || !uuid(context.key.cashFlowItemId)
        || !validDay(context.key.budgetMonth) || !context.key.budgetMonth.endsWith('-01')
        || !integer(factSkip) || factSkip > 100000 || !integer(reserveSkip) || reserveSkip > 20000 || revision !== undefined && !integer(revision))
        return fail();
    const query = new URLSearchParams({ version: context.version, articleId: context.key.cashFlowItemId, factSkip: String(factSkip), reserveSkip: String(reserveSkip) });
    if (revision !== undefined)
        query.set('revision', String(revision));
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/budget/expense-requests/${context.requestId}/explanation?${query}`), { ...secureReadInit(), signal });
    if (!response.ok)
        throw new RequestBudgetExplanationHttpError442(response.status);
    return publicRequestBudgetExplanation442(await response.json(), context, factSkip, reserveSkip, revision);
}
