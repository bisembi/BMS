import { FINANCE_WORKSPACES } from '../financeWorkspaces';
import { REFERENCE_DIRECTORIES, keyForView, type View } from '../navigation';
export const historyField = 'bmsWorkspaceNavigationV1';
const uuid = /^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i;
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value);
const plainKinds = new Set(['my-tasks', 'budget-register', 'expense-payment-register', 'help', 'integration-monitor', 'business-audit-monitor', 'change-journal', 'session-monitor',
    'budget-execution', 'bdds-report', 'unbudgeted-articles', 'budget-instances', 'company-budget-versions', 'budget-directory',
    'authorization-directory', 'currency-rates', 'payment-calendar', 'dated-payment-calendar', 'manual-deficits', 'active-accruals', 'opiu-report',
    'accrual-notifications', 'approval-matrix', 'budget-owner-cost-center-assignment-directory']);
const sections = new Set(['home', 'budgeting', 'treasury', 'management', 'directories', 'administration', 'staff']);
export type ScrollPosition = {
    mainTop: number;
    mainLeft: number;
    windowTop: number;
    windowLeft: number;
};
export const emptyScroll: ScrollPosition = { mainTop: 0, mainLeft: 0, windowTop: 0, windowLeft: 0 };
export type WorkspaceHistoryEntry = {
    version: 1;
    context: string;
    run: string;
    page: string;
    index: number;
    ceiling: number;
    key: string;
    view: View;
    scroll: ScrollPosition;
    closed: string[];
};
/** Explicit allowlist: never serialize amounts, comments, draft fields or tokens. */
export function historyView(view: View): View {
    switch (view.kind) {
        case 'staff-substitutions': return { kind: view.kind, ...(view.documentId ? { documentId: view.documentId } : {}) };
        case 'opiu-report': return { kind: 'opiu-report' };
        case 'bdds-drill': return { kind: view.kind, workspaceKey: view.workspaceKey };
        case 'budget-document': return { kind: view.kind, workspaceKey: view.workspaceKey, instanceId: view.instanceId };
        case 'budget-adjustments': return { kind: view.kind, ...(view.baseBudgetId ? { baseBudgetId: view.baseBudgetId } : {}), ...(view.adjustmentId ? { adjustmentId: view.adjustmentId } : {}) };
        case 'expense-form': return { kind: view.kind, requestId: view.requestId };
        case 'expense-list': return { kind: view.kind, ...(view.onlyMyTasks ? { onlyMyTasks: true } : {}) };
        case 'section': return { kind: view.kind, sectionId: view.sectionId };
        case 'reference-directory': return { kind: view.kind, directoryId: view.directoryId };
        case 'finance-workspace': return { kind: view.kind, workspaceId: view.workspaceId };
        case 'placeholder': return { kind: view.kind, sectionId: view.sectionId, title: view.title };
        default: return { kind: view.kind };
    }
}
export function readHistoryView(value: unknown): View | null {
    if (!object(value) || typeof value.kind !== 'string')
        return null;
    if (value.kind === 'staff-substitutions' && (value.documentId === undefined || value.documentId === 'new' || typeof value.documentId === 'string' && uuid.test(value.documentId)))
        return { kind: 'staff-substitutions', ...(typeof value.documentId === 'string' ? { documentId: value.documentId } : {}) };
    if (plainKinds.has(value.kind))
        return { kind: value.kind } as View;
    if (value.kind === 'bdds-drill' && typeof value.workspaceKey === 'string' && uuid.test(value.workspaceKey))
        return { kind: value.kind, workspaceKey: value.workspaceKey };
    if (value.kind === 'section' && typeof value.sectionId === 'string' && sections.has(value.sectionId))
        return { kind: value.kind, sectionId: value.sectionId } as View;
    if (value.kind === 'expense-list')
        return { kind: value.kind, ...(value.onlyMyTasks === true ? { onlyMyTasks: true } : {}) };
    if (value.kind === 'budget-adjustments'
        && (value.baseBudgetId === undefined || (typeof value.baseBudgetId === 'string' && uuid.test(value.baseBudgetId)))
        && (value.adjustmentId === undefined || (typeof value.adjustmentId === 'string' && uuid.test(value.adjustmentId))))
        return { kind: value.kind, ...(typeof value.baseBudgetId === 'string' ? { baseBudgetId: value.baseBudgetId.toLowerCase() } : {}),
            ...(typeof value.adjustmentId === 'string' ? { adjustmentId: value.adjustmentId.toLowerCase() } : {}) };
    if (value.kind === 'expense-form' && (value.requestId === null || (typeof value.requestId === 'string' && uuid.test(value.requestId))))
        return { kind: value.kind, requestId: typeof value.requestId === 'string' ? value.requestId.toLowerCase() : null };
    if (value.kind === 'budget-document' && typeof value.workspaceKey === 'string'
        && (value.workspaceKey === 'create' || /^local:\d{1,10}$/u.test(value.workspaceKey)
            || (value.workspaceKey.startsWith('saved:') && uuid.test(value.workspaceKey.slice(6))))
        && (value.instanceId === null || (typeof value.instanceId === 'string' && uuid.test(value.instanceId))))
        return { kind: value.kind, workspaceKey: value.workspaceKey, instanceId: typeof value.instanceId === 'string' ? value.instanceId.toLowerCase() : null };
    if (value.kind === 'reference-directory' && typeof value.directoryId === 'string' && Object.prototype.hasOwnProperty.call(REFERENCE_DIRECTORIES, value.directoryId))
        return { kind: value.kind, directoryId: value.directoryId } as View;
    if (value.kind === 'finance-workspace' && typeof value.workspaceId === 'string' && Object.prototype.hasOwnProperty.call(FINANCE_WORKSPACES, value.workspaceId))
        return { kind: value.kind, workspaceId: value.workspaceId } as View;
    if (value.kind === 'placeholder' && typeof value.sectionId === 'string' && sections.has(value.sectionId)
        && typeof value.title === 'string' && value.title.length <= 200 && !/[\u0000-\u001f]/u.test(value.title))
        return { kind: value.kind, sectionId: value.sectionId, title: value.title } as View;
    return null;
}
export function isTransientView(view: View) {
    if (view.kind === 'staff-substitutions' && view.documentId === 'new')
        return true;
    return (view.kind === 'budget-document' && view.instanceId === null) || (view.kind === 'expense-form' && view.requestId === null);
}
export function normalizeReloadView(view: View): View {
    return view.kind === 'budget-document' && view.instanceId
        ? { kind: view.kind, workspaceKey: `saved:${view.instanceId.toLowerCase()}`, instanceId: view.instanceId } : view;
}
export function sameSavedDocument(a: View, b: View): boolean {
    return (a.kind === 'budget-adjustments' && b.kind === a.kind && !!a.adjustmentId && !!b.adjustmentId && a.adjustmentId.toLowerCase() === b.adjustmentId.toLowerCase())
        || (a.kind === 'budget-document' && b.kind === a.kind && a.instanceId !== null && b.instanceId !== null && a.instanceId.toLowerCase() === b.instanceId.toLowerCase())
        || (a.kind === 'expense-form' && b.kind === a.kind && a.requestId !== null && b.requestId !== null && a.requestId.toLowerCase() === b.requestId.toLowerCase());
}
const coordinate = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 1000000000;
export function readHistoryEntry(state: unknown): WorkspaceHistoryEntry | null {
    if (!object(state) || !object(state[historyField]))
        return null;
    const entry = state[historyField];
    const view = readHistoryView(entry.view);
    const scroll = entry.scroll;
    if (entry.version !== 1 || !view || typeof entry.context !== 'string' || !/^[\da-f]{64}$/u.test(entry.context)
        || typeof entry.run !== 'string' || !uuid.test(entry.run) || typeof entry.page !== 'string' || !uuid.test(entry.page)
        || typeof entry.index !== 'number' || !Number.isSafeInteger(entry.index) || entry.index < 0 || entry.index > 1000000
        || typeof entry.ceiling !== 'number' || !Number.isSafeInteger(entry.ceiling) || entry.ceiling < entry.index || entry.ceiling > 1000000
        || typeof entry.key !== 'string' || !entry.key.length || entry.key.length > 250 || entry.key !== keyForView(view)
        || !object(scroll) || !['mainTop', 'mainLeft', 'windowTop', 'windowLeft'].every(key => coordinate(scroll[key]))
        || !Array.isArray(entry.closed) || entry.closed.length > 512 || entry.closed.some(key => typeof key !== 'string' || key.length > 250))
        return null;
    return { version: 1, context: entry.context, run: entry.run, page: entry.page, index: entry.index, ceiling: entry.ceiling, key: entry.key,
        view, scroll: scroll as ScrollPosition, closed: entry.closed as string[] };
}
export function browserState(entry: WorkspaceHistoryEntry): Record<string, unknown> {
    const prior: unknown = window.history.state;
    return { ...(object(prior) ? prior : {}), [historyField]: entry };
}
export async function historyContext(binding: string): Promise<string | null> {
    // Digest the identity/role binding, not an authentication token. Authority is
    // still checked by the current session and every document's server endpoint.
    if (!crypto.subtle)
        return null;
    try {
        const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(binding));
        return Array.from(new Uint8Array(bytes), byte => byte.toString(16).padStart(2, '0')).join('');
    }
    catch {
        return null;
    }
}
