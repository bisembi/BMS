import { apiUrl, fetchWithSafeTransport, fetchWithOneTimeRefresh, secureReadInit, secureWriteInit } from './http';
import { getCurrentSession } from './session';
import { isPublicExpenseRequestUuid as uuid } from './expenseRequestPublicResponse';
import { adjustmentServerErrorMessage } from '../utils/uiErrorMessages';
export type AdjustmentCell = {
    articleId: string;
    month: string;
    direction: 0 | 1;
    amountKzt: number;
};
export type AdjustmentUsage = {
    articleId: string;
    month: string;
    reservedKzt: number;
    paidKzt: number;
    direction?: 0 | 1;
};
export type AdjustmentMove = {
    sourceArticleId: string;
    sourceMonth: string;
    targetArticleId: string;
    targetMonth: string;
    amountKzt: number;
    flowDirection: 0 | 1;
};
export type AdjustmentItem = {
    id: string;
    number: string;
    baseBudgetId: string;
    baseBudgetNumber: string;
    costCenterId: string;
    costCenterName: string;
    state: string;
    stamp: string;
    transferAmountKzt: number;
    authorName: string;
    createdAtUtc: string;
    approvedAtUtc: string | null;
};
export type AdjustmentArticle = {
    id: string;
    code: string;
    name: string;
    flowDirection: 'Payment' | 'Receipt' | null;
    selectable: boolean;
};
export type AdjustmentBasis = {
    baseBudgetId: string;
    number: string;
    costCenterId: string;
    validFrom: string;
    validTo: string;
    cells: AdjustmentCell[];
    consumption: AdjustmentUsage[];
    canCreate: boolean;
    articles: AdjustmentArticle[];
};
export type AdjustmentDoc = {
    document: AdjustmentItem;
    reason: string;
    validFrom: string;
    validTo: string;
    transfers: AdjustmentMove[];
    effectiveCells: AdjustmentCell[];
    consumption: AdjustmentUsage[];
    decisions: {
        submission: number;
        stage: number;
        roleCode: string;
        employeeId: string;
        name: string;
        decision: string;
        atUtc: string;
        comment: string | null;
        actingRoleCode: string | null;
    }[];
    includedVersions: string[];
    canEdit: boolean;
    canSubmit: boolean;
    canApprove: boolean;
    canStop: boolean;
    approverNames: string[];
};
export type AdjustmentPage = {
    items: AdjustmentItem[];
    skip: number;
    take: number;
    total: number;
};
export class AdjustmentError extends Error {
    constructor(public status: number, message: string) { super(message); }
}
const record = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const text = (v: unknown): v is string => typeof v === 'string' && v.length <= 2000 && !/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/u.test(v);
const money = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v) && v >= 0 && Number(v.toFixed(2)) === v;
const date = (v: unknown): v is string => typeof v === 'string' && /^\d{4}-\d{2}-\d{2}$/u.test(v) && Number.isFinite(Date.parse(v));
const month = (v: unknown) => date(v) && v.endsWith('-01');
const invalid = () => new Error('Некорректный ответ сервера корректировок. Данные не заменены.');
export function adjustmentItem(v: unknown): AdjustmentItem {
    if (!record(v) || !uuid(v.id) || !uuid(v.baseBudgetId) || !uuid(v.costCenterId) || !uuid(v.stamp) || !text(v.number) || !text(v.baseBudgetNumber)
        || !text(v.costCenterName) || !text(v.authorName) || !money(v.transferAmountKzt) || !['Draft', 'Returned', 'Rejected', 'PendingFd', 'PendingGd', 'Approved'].includes(String(v.state))
        || typeof v.createdAtUtc !== 'string' || !Number.isFinite(Date.parse(v.createdAtUtc)))
        throw invalid();
    return v as AdjustmentItem;
}
function cells(v: unknown): asserts v is AdjustmentCell[] {
    if (!Array.isArray(v) || v.some(c => !record(c) || !uuid(c.articleId) || !month(c.month) || (c.direction !== 0 && c.direction !== 1) || !money(c.amountKzt)))
        throw invalid();
}
function usage(v: unknown): asserts v is AdjustmentUsage[] {
    if (!Array.isArray(v) || v.some(c => !record(c) || !uuid(c.articleId) || !month(c.month) || !money(c.reservedKzt) || !money(c.paidKzt) || (c.direction !== undefined && c.direction !== 0 && c.direction !== 1)))
        throw invalid();
}
export function adjustmentBasis(v: unknown): AdjustmentBasis {
    if (!record(v) || !uuid(v.baseBudgetId) || !uuid(v.costCenterId) || !text(v.number) || !date(v.validFrom) || !date(v.validTo) || typeof v.canCreate !== 'boolean')
        throw invalid();
    if (!Array.isArray(v.articles) || v.articles.some(a => !record(a) || !uuid(a.id) || !text(a.code) || !text(a.name) || typeof a.selectable !== 'boolean' || !['Payment', 'Receipt', null].includes(a.flowDirection as string | null)))
        throw invalid();
    cells(v.cells);
    usage(v.consumption);
    return v as AdjustmentBasis;
}
export function adjustmentDoc(v: unknown): AdjustmentDoc {
    if (!record(v))
        throw invalid();
    adjustmentItem(v.document);
    cells(v.effectiveCells);
    usage(v.consumption);
    if (!text(v.reason) || !date(v.validFrom) || !date(v.validTo) || !Array.isArray(v.transfers) || v.transfers.length > 200
        || v.transfers.some(m => !record(m) || !uuid(m.sourceArticleId) || !uuid(m.targetArticleId) || !month(m.sourceMonth) || !month(m.targetMonth) || !money(m.amountKzt) || m.amountKzt <= 0 || (m.flowDirection !== 0 && m.flowDirection !== 1))
        || !['canEdit', 'canSubmit', 'canApprove', 'canStop'].every(k => typeof v[k] === 'boolean')
        || !Array.isArray(v.decisions) || v.decisions.some(d => !record(d) || !uuid(d.employeeId) || !text(d.name) || !text(d.roleCode) || !text(d.decision) || typeof d.atUtc !== 'string' || !Number.isFinite(Date.parse(d.atUtc)))
        || !Array.isArray(v.includedVersions) || !v.includedVersions.every(text) || !Array.isArray(v.approverNames) || !v.approverNames.every(text))
        throw invalid();
    return v as AdjustmentDoc;
}
async function request(path: string, method: 'GET' | 'POST' | 'PUT' = 'GET', body?: unknown, key?: string, signal?: AbortSignal): Promise<unknown> {
    const bodyText = JSON.stringify(body);
    const send = () => fetchWithSafeTransport(apiUrl('/api/v1/budget/adjustments' + path), {
        ...(method === 'GET' ? secureReadInit() : secureWriteInit(method, { 'Content-Type': 'application/json', 'Idempotency-Key': key ?? '' }, bodyText)), signal
    });
    const r = method === 'GET' ? await send() : await fetchWithOneTimeRefresh(send, async (response) => {
        if (response.status !== 403)
            return false;
        try {
            const p: unknown = await response.clone().json();
            return record(p) && p.code === 'csrf-token-invalid';
        }
        catch {
            return false;
        }
    }, getCurrentSession);
    if (!r.ok) {
        let message = r.status === 403 ? 'Недостаточно прав для действия с корректировкой.' : r.status === 409 ? 'Документ изменён. Обновите его перед повторным действием.' : 'Не удалось подтвердить операцию. Повторите ту же попытку.';
        try {
            message = adjustmentServerErrorMessage(await r.json(), message);
        }
        catch { }
        throw new AdjustmentError(r.status, message);
    }
    return r.json();
}
export async function listAdjustments(base: string | undefined, skip: number, signal?: AbortSignal): Promise<AdjustmentPage> {
    const q = new URLSearchParams({ skip: String(skip), take: '25' });
    if (base)
        q.set('baseBudgetId', base);
    const v = await request('?' + q, 'GET', undefined, undefined, signal);
    if (!record(v) || !Array.isArray(v.items) || v.skip !== skip || v.take !== 25 || !Number.isInteger(v.total) || Number(v.total) < 0)
        throw invalid();
    return { ...v, items: v.items.map(adjustmentItem) } as AdjustmentPage;
}
export async function getAdjustmentBasis(id: string, signal?: AbortSignal) {
    if (!uuid(id))
        throw invalid();
    const v = adjustmentBasis(await request('/basis/' + id, 'GET', undefined, undefined, signal));
    if (v.baseBudgetId.toLowerCase() !== id.toLowerCase())
        throw invalid();
    return v;
}
export async function getAdjustment(id: string, signal?: AbortSignal) {
    if (!uuid(id))
        throw invalid();
    const v = adjustmentDoc(await request('/' + id, 'GET', undefined, undefined, signal));
    if (v.document.id.toLowerCase() !== id.toLowerCase())
        throw invalid();
    return v;
}
export async function writeAdjustment(path: string, method: 'POST' | 'PUT', body: unknown, key: string) {
    const v = adjustmentDoc(await request(path, method, body, key));
    return v;
}
export const adjustmentStates: Record<string, string> = { Draft: 'Черновик', Returned: 'На доработке', PendingFd: 'На согласовании — ФД', PendingGd: 'На утверждении — ГД', Approved: 'Утверждена ГД', Rejected: 'Отклонена' };
