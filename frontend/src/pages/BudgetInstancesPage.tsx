import { useWorkspaceRefreshRevision, useWorkspaceRefreshAction, useWorkspaceRefreshGuard, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { Copy, Trash2 } from 'lucide-react';
import { openableTableRow } from '../utils/openableTableRow';
import { BudgetGuideLink } from '../components/BudgetGuideLink';
import { canonicalMoneyText } from '../utils/moneyInput';
import { BudgetAmountInput } from '../components/budget-instance/BudgetAmountInput';
import { BudgetOperationInput } from '../components/budget-instance/BudgetOperationInput';
import { BudgetContextRow } from '../components/budget-instance/BudgetContextRow';
import { useCommandLock } from '../components/WorkspaceCommandLockContext';
import { useCallback, useEffect, useId, useRef, useState } from 'react';
import type { CSSProperties, FormEvent, SetStateAction } from 'react';
import { BudgetLineColumnsHead, useBudgetLineColumnWidths } from '../components/budget-instance/BudgetLineColumns';
import { sortBudgetLines, type BudgetLineSort, type BudgetLineSortColumn } from './budgetLineSort';
import { BudgetApprovalPanel } from '../components/budget-instance/BudgetApprovalPanel';
import { BudgetApprovalHistoryPanel } from '../components/budget-instance/BudgetApprovalHistoryPanel';
import { budgetInstanceListStatusParts, budgetInstanceListStatusClass, useBudgetApprovalListStatuses } from './budgetInstanceApprovalStatus';
import { approveBudgetInstance, budgetInstanceErrorMessage, budgetInstanceErrorDetails, budgetInstanceDeletionErrorMessage, createBudgetInstance, deleteBudgetInstance, getBudgetInstance, isBudgetInstanceConflict, isBudgetInstanceResultUncertain, isBudgetInstanceValidationError, getBudgetInstanceCreationOptions, listAllBudgetInstanceCostCenters, listBudgetInstanceCostCenters, listBudgetInstances, updateBudgetInstance, type BudgetInstance, type BudgetInstanceCostCenter, type BudgetInstanceCostCenterPage, type BudgetInstanceLineInput, type BudgetInstanceListItem, type CreateBudgetInstanceInput, type UpdateBudgetInstanceInput, type DeleteBudgetInstanceInput } from '../api/budgetInstances';
import { CashFlowItemCombobox } from '../components/reference-directory/CashFlowItemCombobox';
import { BudgetCopyActualsDialog, type BudgetCopyActualsSelection } from '../components/budget-instance/BudgetCopyActualsDialog';
import { previewBudgetActuals, publicBudgetActualsPreview, type BudgetActualsInput } from '../api/budgetActuals';
import { replaceBudgetActualsSlice } from './budgetActualsReplacement';
import { useCashFlowItemOptions } from '../hooks/useCashFlowItemOptions';
import { useBudgetInstanceVersions } from '../hooks/useBudgetInstanceVersions';
import { useBudgetSubmission } from '../hooks/useBudgetSubmission';
import { createOperationKey } from '../utils/operationKey';
import { formatBudgetPeriodDisplay } from '../utils/budgetPeriodDisplay';
import { BudgetInstanceMatrix } from '../components/budget-instance/BudgetInstanceMatrix';
import { CompactMonthInput } from '../components/budget-instance/CompactMonthInput';
import { BudgetMatrixPasteDialog } from '../components/budget-instance/BudgetMatrixPasteDialog';
import { budgetPasteErrorMessage, budgetPasteScopeKey, pasteBudgetMatrixDrafts, prepareBudgetMatrixPaste, type BudgetPastePlan } from './budgetMatrixPaste';
import { BudgetMatrixInputError, budgetMatrixErrorMessage, budgetMatrixAmount, budgetMatrixCellKey, budgetMatrixMonths, previewBudgetMatrix, signedBudgetCents, type BudgetDraftLine } from './budgetInstanceMatrix';
import { budgetArticleAssignmentReason, budgetSaveRowLabel, collectBudgetSaveLineIssues } from './budgetSaveDiagnostics';
import { downloadBudgetWorkbook } from './budgetInstanceExcel';
import { selectedBudgetDirection, manualBudgetCellText, replaceManualBudgetCell } from './budgetCatalogDirection';
import '../styles/budget-toolbar.css';
import '../styles/budget-new-row.css';
import '../styles/budget-status-colors.css';
// Local rendering identity only; payloadLines explicitly selects API fields.
type DraftLine = BudgetDraftLine & {
    uiRowKey?: string;
};
type Mode = 'list' | 'create' | 'edit';
type FrozenAttempt = {
    kind: 'create';
    key: string;
    input: CreateBudgetInstanceInput;
} | {
    kind: 'update';
    key: string;
    id: string;
    input: UpdateBudgetInstanceInput;
};
type FrozenApproval = {
    key: string;
    id: string;
    input: {
        expectedConcurrencyStamp: string;
    };
};
type FrozenDeletion = {
    key: string;
    id: string;
    input: DeleteBudgetInstanceInput;
};
type CopySeed = {
    number: string;
    validFrom: string;
    validTo: string;
};
type Confirmation = 'leave' | 'reload' | null;
const take = 25;
const listPageSize = 50;
const wholeMoneyFormatter = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 });
const sameId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();
/** Display-only rounding, never used for submitted amounts. */
export function formatBudgetLimitDisplay(value: number): string {
    return `${wholeMoneyFormatter.format(value)} ₸`;
}
function todayMonth(): string {
    const now = new Date();
    return `${String(now.getFullYear()).padStart(4, '0')}-${String(now.getMonth() + 1).padStart(2, '0')}`;
}
function monthStart(value: string): string {
    if (!/^\d{4}-(0[1-9]|1[0-2])$/u.test(value) || value.startsWith('0000-'))
        return '';
    return `${value}-01`;
}
function monthEnd(value: string): string {
    const start = monthStart(value);
    if (!start)
        return '';
    const date = new Date(`${start}T00:00:00.000Z`);
    if (Number.isNaN(date.getTime()))
        return '';
    date.setUTCMonth(date.getUTCMonth() + 1, 0);
    return date.toISOString().slice(0, 10);
}
function emptyLineForPeriod(value: string): DraftLine {
    // A new amount is intentionally blank: zero is a meaningful value and must
    // only be entered explicitly by the user.
    return {
        cashFlowItemId: '',
        budgetMonth: monthStart(value),
        limitAmountKzt: '',
        comment: '',
        flowDirection: 'Payment',
    };
}
type DerivedPeriod = {
    fromMonth: string;
    toMonth: string;
    from: string;
    to: string;
};
function sameLineCalendar(left: readonly {
    budgetMonth: string;
}[], right: readonly {
    budgetMonth: string;
}[]): boolean {
    const leftMonths = new Set(left.map(line => line.budgetMonth.slice(0, 7)));
    const rightMonths = new Set(right.map(line => line.budgetMonth.slice(0, 7)));
    return leftMonths.size === rightMonths.size && [...leftMonths].every(month => rightMonths.has(month));
}
function derivePeriod(lines: readonly DraftLine[]): DerivedPeriod | null {
    const months = [...new Set(lines.map(line => line.budgetMonth.slice(0, 7)))]
        .filter(month => monthStart(month) !== '')
        .sort();
    const fromMonth = months[0];
    const toMonth = months[months.length - 1];
    if (!fromMonth || !toMonth)
        return null;
    const from = monthStart(fromMonth);
    const to = monthEnd(toMonth);
    return from && to ? { fromMonth, toMonth, from, to } : null;
}
function amountInputValue(value: number): string {
    if (!Number.isFinite(value))
        return '';
    if (Number.isInteger(value))
        return String(value);
    return value.toFixed(2).replace(/0+$/u, '').replace(/\.$/u, '');
}
function linesFrom(instance: BudgetInstance, previous: readonly DraftLine[] = []): DraftLine[] {
    const available = [...previous];
    return instance.lines.map(line => {
        const sameCell = (old: DraftLine) => sameId(old.cashFlowItemId, line.cashFlowItemId)
            && old.budgetMonth === line.budgetMonth && old.flowDirection === (line.flowDirection ?? 'Payment');
        let index = available.findIndex(old => sameCell(old) && old.comment.trim() === (line.comment ?? '').trim()
            && signedBudgetCents(old.limitAmountKzt) === signedBudgetCents(amountInputValue(line.limitAmountKzt)));
        // Do not attach a different operation's identity to an ambiguous duplicate.
        if (index < 0 && available.filter(sameCell).length === 1
            && instance.lines.filter(candidate => sameId(candidate.cashFlowItemId, line.cashFlowItemId)
                && candidate.budgetMonth === line.budgetMonth && (candidate.flowDirection ?? 'Payment') === (line.flowDirection ?? 'Payment')).length === 1)
            index = available.findIndex(sameCell);
        const uiRowKey = index < 0 ? undefined : available.splice(index, 1)[0].uiRowKey;
        return {
            ...(uiRowKey ? { uiRowKey } : {}),
            cashFlowItemId: line.cashFlowItemId,
            budgetMonth: line.budgetMonth,
            limitAmountKzt: amountInputValue(line.limitAmountKzt),
            comment: line.comment ?? '',
            flowDirection: line.flowDirection ?? 'Payment',
        };
    });
}
function parseLimit(value: string): number | null {
    // Accept ordinary decimal input, never coerce a blank, exponent or malformed amount to zero.
    const normalized = canonicalMoneyText(value);
    if (normalized === null || !/^\d+(?:\.\d{1,2})?$/u.test(normalized))
        return null;
    const [whole, fraction = ''] = normalized.split('.');
    const expectedCents = BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
    if (expectedCents > BigInt(Number.MAX_SAFE_INTEGER))
        return null;
    const amount = Number(normalized);
    const cents = Math.round(amount * 100);
    if (!Number.isFinite(amount) || amount < 0 || !Number.isSafeInteger(cents)
        || BigInt(cents) !== expectedCents)
        return null;
    // Number can change a typed cent while still producing integral, safe JS cents.
    // Compare the actual JSON numeric representation against the original decimal digits,
    // not against another conversion of the same already rounded Number.
    const wire = JSON.stringify(amount);
    if (!/^\d+(?:\.\d{1,2})?$/u.test(wire))
        return null;
    const [wireWhole, wireFraction = ''] = wire.split('.');
    const wireCents = BigInt(wireWhole) * 100n + BigInt(wireFraction.padEnd(2, '0'));
    return wireCents === expectedCents ? amount : null;
}
function payloadLines(lines: readonly DraftLine[], from: string, to: string): BudgetInstanceLineInput[] | null {
    if (lines.length === 0)
        return null;
    let totalCents = 0;
    const result: BudgetInstanceLineInput[] = [];
    for (const line of lines) {
        const amount = parseLimit(line.limitAmountKzt);
        if (!line.cashFlowItemId || monthStart(line.budgetMonth.slice(0, 7)) !== line.budgetMonth
            || line.budgetMonth < from || line.budgetMonth > to || amount === null)
            return null;
        totalCents += Math.round(amount * 100);
        if (!Number.isSafeInteger(totalCents))
            return null;
        result.push({
            cashFlowItemId: line.cashFlowItemId,
            budgetMonth: line.budgetMonth,
            limitAmountKzt: amount,
            ...(line.flowDirection === 'Receipt' ? { flowDirection: line.flowDirection } : {}),
            ...(line.comment.trim().length > 0 ? { comment: line.comment.trim() } : {}),
        });
    }
    return result;
}
function newAttemptKey(): string {
    return createOperationKey();
}
function useMemberCostCenters(enabled: boolean, validFrom: string) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [query, setQueryState] = useState('');
    const [skip, setSkipState] = useState(0);
    const [revision, setRevision] = useState(0);
    const [page, setPage] = useState<BudgetInstanceCostCenterPage | null>(null);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const generation = useRef(0);
    const active = useRef<AbortController | null>(null);
    useEffect(() => { setSkipState(0); }, [validFrom]);
    useEffect(() => {
        const request = ++generation.current;
        const controller = new AbortController();
        active.current = controller;
        setPage(null);
        setError(null);
        setLoading(enabled && validFrom !== '');
        if (!enabled || validFrom === '')
            return () => { controller.abort(); generation.current += 1; };
        void listBudgetInstanceCostCenters(query, skip, take, validFrom, controller.signal).then(answer => {
            if (controller.signal.aborted || request !== generation.current)
                return;
            if (skip > 0 && skip >= answer.total) {
                setSkipState(answer.total === 0 ? 0 : Math.floor((answer.total - 1) / take) * take);
                return;
            }
            setPage(answer);
        }).catch(failure => {
            if (!controller.signal.aborted && request === generation.current)
                setError(budgetInstanceErrorMessage(failure));
        }).finally(() => {
            if (!controller.signal.aborted && request === generation.current)
                setLoading(false);
        });
        return () => { controller.abort(); generation.current += 1; };
    }, [enabled, query, revision, skip, validFrom, workspaceRefresh]);
    function invalidate() {
        generation.current += 1;
        active.current?.abort();
        setPage(null);
        setError(null);
        setLoading(enabled && validFrom !== '');
    }
    return {
        query, skip, page, loading, error,
        setQuery(value: string) {
            if (value === query)
                return;
            invalidate();
            setQueryState(value);
            setSkipState(0);
        },
        setSkip(value: number) {
            if (value === skip)
                return;
            invalidate();
            setSkipState(Math.max(0, value));
        },
        reload() { invalidate(); setRevision(value => value + 1); },
    };
}
type BudgetDocumentEntry = {
    kind: 'open' | 'copy';
    id: string;
} | {
    kind: 'new';
};
type BudgetDocumentMetadata = Pick<BudgetInstance, 'id' | 'number'>;
export function BudgetInstancesPage({ onNavigationLockChange, initialDocument, listReloadToken = 0, onOpenInstance, onCreateInstance, onCopyInstance, onBackToList, onDocumentLoaded, onDocumentSaved, onOpenAdjustments }: {
    onOpenAdjustments?: (baseBudgetId: string) => void;
    onNavigationLockChange?: (locked: boolean) => void;
    initialDocument?: BudgetDocumentEntry;
    listReloadToken?: number;
    onOpenInstance?: (id: string, number: string) => void;
    onCreateInstance?: () => void;
    onCopyInstance?: (id: string, number: string) => void;
    onBackToList?: () => void;
    onDocumentLoaded?: (document: BudgetDocumentMetadata) => void;
    onDocumentSaved?: (document: BudgetDocumentMetadata) => void;
}) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    // Mutable tab metadata is not a new opening intent. Freeze the initial intent
    // so the first confirmed save cannot reinitialize or remount this editor.
    const documentEntry = useRef(initialDocument);
    const documentCallbacks = useRef({ onDocumentLoaded, onDocumentSaved });
    documentCallbacks.current = { onDocumentLoaded, onDocumentSaved };
    const saveCommand = useCommandLock('budget-save');
    const approveCommand = useCommandLock('budget-approve');
    const deleteCommand = useCommandLock('budget-delete');
    const [mode, setMode] = useState<Mode>('list');
    const [items, setItems] = useState<BudgetInstanceListItem[]>([]);
    const [total, setTotal] = useState(0);
    const [skip, setSkip] = useState(0);
    const [filterCostCenterId, setFilterCostCenterId] = useState('');
    const [filterVersionId, setFilterVersionId] = useState('');
    const [filterVersionName, setFilterVersionName] = useState('');
    const [filterPeriodFrom, setFilterPeriodFrom] = useState('');
    const [filterPeriodTo, setFilterPeriodTo] = useState('');
    const [appliedListFilters, setAppliedListFilters] = useState({ center: '', version: '', from: '', to: '' });
    const [listLoading, setListLoading] = useState(initialDocument === undefined);
    const [detailLoading, setDetailLoading] = useState(false);
    const [listError, setListError] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [notice, setNotice] = useState<string | null>(null);
    const [saveIssues, setSaveIssues] = useState<string[]>([]);
    const [failedSaveRevision, setFailedSaveRevision] = useState(0);
    const saveAlert = useRef<HTMLDivElement>(null);
    const [current, setCurrent] = useState<BudgetInstance | null>(null);
    const [copySeed, setCopySeed] = useState<CopySeed | null>(null);
    const [pendingDeletion, setPendingDeletion] = useState<string | null>(null);
    const [deleting, setDeleting] = useState(false);
    const [deletionUncertain, setDeletionUncertain] = useState(false);
    const deleteInFlight = useRef(false);
    const deletionUncertainRef = useRef(false);
    const deletionAttempt = useRef<FrozenDeletion | null>(null);
    const [reviewRouteTarget, setReviewRouteTarget] = useState<HTMLSpanElement | null>(null);
    const [chosenCenter, setChosenCenter] = useState<BudgetInstanceCostCenter | null>(null);
    const [creatorCenterLoading, setCreatorCenterLoading] = useState(false);
    const [creatorCenterError, setCreatorCenterError] = useState<string | null>(null);
    const [canChooseAnyCenter, setCanChooseAnyCenter] = useState(false);
    const [creationCenters, setCreationCenters] = useState<BudgetInstanceCostCenter[]>([]);
    const centerChosenManually = useRef(false);
    const [knownCenters, setKnownCenters] = useState<BudgetInstanceCostCenter[]>([]);
    const [lines, setLineState] = useState<DraftLine[]>([]);
    const setLines = useCallback((update: SetStateAction<DraftLine[]>) => {
        setLineState(previous => {
            const next = typeof update === 'function' ? update(previous) : update;
            const seen = new Set<string>();
            let assigned = false;
            const keyed = next.map(line => {
                if (line.uiRowKey && !seen.has(line.uiRowKey)) {
                    seen.add(line.uiRowKey);
                    return line;
                }
                const uiRowKey = createOperationKey();
                seen.add(uiRowKey);
                assigned = true;
                return { ...line, uiRowKey };
            });
            return assigned ? keyed : next;
        });
    }, []);
    const [newRowKeys, setNewRowKeys] = useState<ReadonlySet<string>>(new Set());
    const newRowTimers = useRef(new Map<string, ReturnType<typeof setTimeout>>());
    useEffect(() => {
        setNewRowKeys(new Set());
        const timers = newRowTimers.current;
        return () => {
            for (const timer of timers.values())
                clearTimeout(timer);
            timers.clear();
        };
    }, [mode, current?.id, current?.concurrencyStamp, chosenCenter?.id]);
    const [tabular, setTabular] = useState(false);
    const lineColumns = useBudgetLineColumnWidths();
    const [lineSort, setLineSort] = useState<BudgetLineSort | null>(null);
    const [sortedRowKeys, setSortedRowKeys] = useState<readonly string[] | null>(null);
    useEffect(() => { setLineSort(null); setSortedRowKeys(null); }, [mode, current?.id, current?.concurrencyStamp, chosenCenter?.id]);
    const [matrixDrafts, setMatrixDraftState] = useState<Record<string, string | null>>({});
    const pastedMatrixCells = useRef(new Set<string>());
    function setMatrixDrafts(next: Record<string, string | null>) {
        if (Object.keys(next).length === 0)
            pastedMatrixCells.current.clear();
        setMatrixDraftState(next);
    }
    const matrixDraftsRef = useRef(matrixDrafts);
    matrixDraftsRef.current = matrixDrafts;
    const [matrixEdited, setMatrixEdited] = useState(false);
    const [pastePreview, setPastePreview] = useState<{
        plan: BudgetPastePlan;
        generation: number;
        centerId: string | undefined;
        scopeKey: string;
        source: readonly DraftLine[];
        drafts: Readonly<Record<string, string | null>>;
        stamp: string | undefined;
    } | null>(null);
    const [pasteUndo, setPasteUndo] = useState<{
        lines: DraftLine[];
        drafts: Record<string, string | null>;
        dirty: boolean;
        matrixEdited: boolean;
        pastedKeys: Set<string>;
        calendar: typeof actualsCalendar.current;
        generation: number;
        centerId: string | undefined;
    } | null>(null);
    const [saving, setSaving] = useState(false);
    const [approving, setApproving] = useState(false);
    const [approvalUncertain, setApprovalUncertain] = useState(false);
    const [dirty, setDirty] = useState(false);
    const formId = useId();
    const [reviewActionTarget, setReviewActionTarget] = useState<HTMLSpanElement | null>(null);
    const [reviewStatusTarget, setReviewStatusTarget] = useState<HTMLSpanElement | null>(null);
    const [reviewReadToken, setReviewReadToken] = useState(0);
    const [uncertain, setUncertain] = useState(false);
    const [needsRefresh, setNeedsRefresh] = useState(false);
    const [pendingLineRemoval, setPendingLineRemoval] = useState<number | null>(null);
    const [confirmation, setConfirmation] = useState<Confirmation>(null);
    const [listRevision, setListRevision] = useState(0);
    const listSequence = useRef(0);
    const detailSequence = useRef(0);
    const listRead = useRef<AbortController | null>(null);
    const detailRead = useRef<AbortController | null>(null);
    const mounted = useRef(false);
    const saveInFlight = useRef(false);
    const approvalInFlight = useRef(false);
    const dirtyRef = useRef(false);
    const uncertainRef = useRef(false);
    const approvalUncertainRef = useRef(false);
    const needsRefreshRef = useRef(false);
    const attempt = useRef<FrozenAttempt | null>(null);
    const approvalAttempt = useRef<FrozenApproval | null>(null);
    const [actualsDialogOpen, setActualsDialogOpen] = useState(false);
    const actualsSequence = useRef(0);
    const actualsRead = useRef<AbortController | null>(null);
    const actualsCalendar = useRef<{
        documentGeneration: number;
        lines: readonly DraftLine[];
    } | null>(null);
    const linesRef = useRef(lines);
    linesRef.current = lines;
    const notifyLock = useRef(onNavigationLockChange);
    const [reviewPanelLocked, setReviewLocked] = useState(false);
    const submission = useBudgetSubmission({ prepare: prepareBudgetSubmission, onSubmitted: async (id) => {
            await readDetail(id, false, true);
            if (mounted.current) {
                setListRevision(value => value + 1);
                setNotice(needsRefreshRef.current ? 'Подача подтверждена. Перечитайте документ перед следующей правкой.'
                    : 'Бюджет подан на согласование.');
            }
        } });
    const reviewLocked = reviewPanelLocked || submission.busy || submission.unknown;
    const reviewLockedRef = useRef(false);
    reviewLockedRef.current = reviewLocked;
    const heading = useRef<HTMLHeadingElement>(null);
    notifyLock.current = onNavigationLockChange;
    const optionDate = mode === 'list' ? monthStart(filterPeriodFrom || todayMonth()) : '';
    const centers = useMemberCostCenters(mode === 'list' && documentEntry.current === undefined, optionDate);
    const versions = useBudgetInstanceVersions(mode === 'list' && documentEntry.current === undefined, workspaceRefresh);
    const articleCostCenterId = mode === 'create' ? chosenCenter?.id : current?.costCenterId;
    const articleCostCenterRef = useRef(articleCostCenterId);
    articleCostCenterRef.current = articleCostCenterId;
    const cashFlowOptions = useCashFlowItemOptions(mode !== 'list' ? lines.map(line => line.cashFlowItemId).filter(Boolean) : [], 0, mode !== 'list', mode === 'list' ? undefined : articleCostCenterId || null);
    useWorkspaceRefreshGuard(dirty || saving || approving || uncertain || approvalUncertain || reviewLocked || deleting || deletionUncertain
        || pastePreview !== null || actualsDialogOpen || confirmation !== null || pendingLineRemoval !== null || pendingDeletion !== null
        ? 'Сначала сохраните или отмените изменения бюджета и закройте диалог; неподтверждённую команду нужно повторить.' : null);
    useWorkspaceRefreshBusy(listLoading || detailLoading || creatorCenterLoading || centers.loading || versions.loading);
    useWorkspaceRefreshAction(() => {
        const id = current?.id ?? (documentEntry.current?.kind === 'open' ? documentEntry.current.id : null);
        if (id !== null)
            return readDetail(id, false, true);
        if (mode === 'create' && derivePeriod(lines)?.fromMonth)
            return refreshCreationCatalog();
    });
    useEffect(() => {
        mounted.current = true;
        return () => {
            mounted.current = false;
            listSequence.current += 1;
            detailSequence.current += 1;
            listRead.current?.abort();
            detailRead.current?.abort();
            actualsSequence.current += 1;
            actualsRead.current?.abort();
            // Never abort an already sent write on unmount. Do not accept its late result into another session.
            notifyLock.current?.(false);
        };
    }, []);
    useEffect(() => {
        const locked = reviewLocked || deleting || deletionUncertain || (mode !== 'list' && (dirty || saving || approving || uncertain || approvalUncertain
            || detailLoading || needsRefresh || actualsDialogOpen));
        notifyLock.current?.(locked);
    }, [reviewLocked, actualsDialogOpen, approvalUncertain, approving, deleting, deletionUncertain, detailLoading, dirty, mode, needsRefresh, saving, uncertain]);
    useEffect(() => {
        function beforeUnload(event: BeforeUnloadEvent) {
            if (!reviewLockedRef.current && !dirtyRef.current && !saveInFlight.current && !uncertainRef.current
                && !approvalInFlight.current && !approvalUncertainRef.current
                && !deleteInFlight.current && !deletionUncertainRef.current)
                return;
            event.preventDefault();
            event.returnValue = '';
        }
        window.addEventListener('beforeunload', beforeUnload);
        return () => window.removeEventListener('beforeunload', beforeUnload);
    }, []);
    useEffect(() => {
        heading.current?.focus();
    }, [mode, current?.id]);
    useEffect(() => {
        if (failedSaveRevision === 0)
            return;
        saveAlert.current?.focus();
        saveAlert.current?.scrollIntoView?.({ block: 'nearest' });
    }, [failedSaveRevision]);
    useEffect(() => {
        if (error === null)
            setSaveIssues([]);
    }, [error]);
    function showSaveFailure(message: string, issues: string[] = []) {
        // Diagnostics refer to persisted/source row numbers, so show that same order.
        setLineSort(null);
        setSortedRowKeys(null);
        setError(message);
        setSaveIssues(issues);
        setNotice(null);
        setFailedSaveRevision(value => value + 1);
    }
    useEffect(() => {
        if (!centers.page)
            return;
        setKnownCenters(previous => {
            const map = new Map(previous.map(item => [item.id.toLowerCase(), item]));
            centers.page!.items.forEach(item => map.set(item.id.toLowerCase(), item));
            return [...map.values()];
        });
    }, [centers.page]);
    async function refreshCreationCatalog() {
        const fromMonth = derivePeriod(lines)?.fromMonth;
        if (!fromMonth)
            return;
        const sequence = ++detailSequence.current;
        setCreatorCenterLoading(true);
        setCreatorCenterError(null);
        try {
            const options = await getBudgetInstanceCreationOptions(monthStart(fromMonth));
            const available = options.canChooseAnyCostCenter || copySeed !== null
                ? await listAllBudgetInstanceCostCenters(monthStart(fromMonth)) : [];
            if (!mounted.current || sequence !== detailSequence.current)
                return;
            setCanChooseAnyCenter(options.canChooseAnyCostCenter);
            setCreationCenters(available);
            // Catalogue refresh never resets the creation/copy intent, chosen CFO,
            // lines, paste, matrix or presentation. Server write authority is unchanged.
            if (chosenCenter && (options.canChooseAnyCostCenter || copySeed !== null)
                && !available.some(center => sameId(center.id, chosenCenter.id)))
                setCreatorCenterError('Ранее выбранный ЦФО больше недоступен. Выберите другой ЦФО.');
        }
        catch (error) {
            if (mounted.current && sequence === detailSequence.current)
                setCreatorCenterError('Не удалось загрузить доступные ЦФО. Повторите обновление.');
            throw error;
        }
        finally {
            if (mounted.current && sequence === detailSequence.current)
                setCreatorCenterLoading(false);
        }
    }
    const creatorFromMonth = mode === 'create' ? (derivePeriod(lines)?.fromMonth ?? '') : '';
    useEffect(() => {
        if (mode !== 'create')
            return;
        if (creatorFromMonth === '') {
            setCreatorCenterLoading(false);
            setCreatorCenterError(null);
            return;
        }
        const controller = new AbortController();
        setCreatorCenterLoading(true);
        setCreatorCenterError(null);
        void getBudgetInstanceCreationOptions(monthStart(creatorFromMonth), controller.signal)
            .then(async (options) => {
            const available = options.canChooseAnyCostCenter || copySeed !== null
                ? await listAllBudgetInstanceCostCenters(monthStart(creatorFromMonth), controller.signal) : [];
            if (controller.signal.aborted)
                return;
            const resetArticleAssignments = () => {
                // The creation context is refreshed before any matrix editing resumes.
                // Materialize defensively if a pending overlay survived a context change.
                const prepared = materializeMatrix();
                if (!prepared) {
                    setCreatorCenterError('ЦФО изменён. Исправьте введённые ячейки и повторно выберите период перед сохранением.');
                    return false;
                }
                setLines(prepared.map(line => ({ ...line, cashFlowItemId: '' })));
                setMatrixDrafts({});
                matrixDraftsRef.current = {};
                setMatrixEdited(false);
                setTabular(false);
                return true;
            };
            setCanChooseAnyCenter(options.canChooseAnyCostCenter);
            setCreationCenters(available);
            if ((options.canChooseAnyCostCenter || copySeed !== null) && (centerChosenManually.current || copySeed !== null)
                && articleCostCenterRef.current && !available.some(center => sameId(center.id, articleCostCenterRef.current!))) {
                if (!resetArticleAssignments())
                    return;
                closeActualsDialog();
                detailSequence.current += 1;
                setChosenCenter(null);
                setCreatorCenterError(options.canChooseAnyCostCenter
                    ? 'Ранее выбранный ЦФО больше недоступен. Выберите другой ЦФО.'
                    : 'ЦФО копируемого экземпляра больше недоступен. Вернитесь к списку.');
            }
            if (copySeed === null && (!centerChosenManually.current || !options.canChooseAnyCostCenter)) {
                if (articleCostCenterRef.current && articleCostCenterRef.current !== options.defaultCostCenter?.id) {
                    if (!resetArticleAssignments())
                        return;
                    closeActualsDialog();
                    detailSequence.current += 1;
                    setNotice('ЦФО автора изменён для выбранного периода. Выберите его статьи ДДС; суммы и комментарии сохранены.');
                }
                const defaultUnavailable = options.canChooseAnyCostCenter && options.defaultCostCenter !== null
                    && !available.some(center => sameId(center.id, options.defaultCostCenter!.id));
                setChosenCenter(defaultUnavailable ? null : options.defaultCostCenter);
                if (defaultUnavailable)
                    setCreatorCenterError('ЦФО по умолчанию больше недоступен. Выберите другой ЦФО.');
            }
            if (!options.canChooseAnyCostCenter && copySeed === null && options.defaultCostCenter === null) {
                setCreatorCenterError('Не удалось однозначно определить ЦФО автора. Проверьте активную связь сотрудника ровно с одним ЦФО.');
            }
        })
            .catch(() => {
            if (!controller.signal.aborted) {
                setCanChooseAnyCenter(false);
                setCreationCenters([]);
                setCreatorCenterError('Не удалось загрузить доступные ЦФО. Повторите открытие формы.');
            }
        })
            .finally(() => {
            if (!controller.signal.aborted)
                setCreatorCenterLoading(false);
        });
        return () => controller.abort();
    }, [copySeed, creatorFromMonth, mode]);
    const reloadList = useCallback(async (nextSkip: number): Promise<void> => {
        const sequence = ++listSequence.current;
        listRead.current?.abort();
        const controller = new AbortController();
        listRead.current = controller;
        setListLoading(true);
        setListError(null);
        setPendingDeletion(null);
        const from = appliedListFilters.from ? monthStart(appliedListFilters.from) : undefined;
        const to = appliedListFilters.to ? monthEnd(appliedListFilters.to) : undefined;
        if ((from !== undefined && !from) || (to !== undefined && !to) || (from && to && from > to)) {
            setItems([]);
            setTotal(0);
            setListLoading(false);
            setListError('Начало периода должно быть не позже окончания.');
            return;
        }
        try {
            const page = await listBudgetInstances({
                skip: nextSkip, take: listPageSize, costCenterId: appliedListFilters.center || undefined, periodFrom: from, periodTo: to,
                ...(appliedListFilters.version ? { versionId: appliedListFilters.version } : {}),
            }, controller.signal);
            if (!mounted.current || controller.signal.aborted || sequence !== listSequence.current)
                return;
            if (nextSkip > 0 && nextSkip >= page.total) {
                await reloadList(page.total === 0 ? 0 : Math.floor((page.total - 1) / listPageSize) * listPageSize);
                return;
            }
            setItems(page.items);
            setTotal(page.total);
            setSkip(page.skip);
        }
        catch (cause) {
            if (!mounted.current || controller.signal.aborted || sequence !== listSequence.current)
                return;
            setListError(budgetInstanceErrorMessage(cause));
            setItems([]);
            setTotal(0);
        }
        finally {
            if (mounted.current && !controller.signal.aborted && sequence === listSequence.current)
                setListLoading(false);
        }
    }, [appliedListFilters]);
    const approvalListStatuses = useBudgetApprovalListStatuses(items, mode === 'list' && !listLoading);
    const listPosition = useRef(skip);
    listPosition.current = skip;
    const previousListQuery = useRef<typeof reloadList | null>(null);
    useEffect(() => {
        if (documentEntry.current !== undefined)
            return;
        const queryChanged = previousListQuery.current !== reloadList;
        previousListQuery.current = reloadList;
        void reloadList(queryChanged ? 0 : listPosition.current);
    }, [listRevision, listReloadToken, reloadList, workspaceRefresh]);
    useEffect(() => {
        const entry = documentEntry.current;
        if (entry?.kind === 'open')
            void readDetail(entry.id);
        else if (entry?.kind === 'copy')
            void copyInstance(entry.id);
        else if (entry?.kind === 'new')
            startCreate();
        // The immutable opening intent belongs to the workspace mount, not props
        // such as the number/ID updated by a confirmed write. StrictMode may repeat
        // this read-only initialization; sequence/abort guards reject the old read.
    }, []);
    function synchronize(instance: BudgetInstance, preserveView = false, preserveRowKeys = false) {
        setPastePreview(null);
        setPasteUndo(null);
        actualsCalendar.current = null;
        setCopySeed(null);
        setCurrent(instance);
        setReviewReadToken(value => value + 1);
        setLines(linesFrom(instance, preserveRowKeys ? linesRef.current : []));
        setMatrixDrafts({});
        setMatrixEdited(false);
        setMode('edit');
        setDirty(false);
        dirtyRef.current = false;
        setUncertain(false);
        uncertainRef.current = false;
        setApprovalUncertain(false);
        approvalUncertainRef.current = false;
        setNeedsRefresh(false);
        needsRefreshRef.current = false;
        setPendingLineRemoval(null);
        setConfirmation(null);
        attempt.current = null;
        approvalAttempt.current = null;
        notifyLock.current?.(saveInFlight.current || approvalInFlight.current);
        if (!preserveView)
            cashFlowOptions.setQuery('');
        documentCallbacks.current.onDocumentLoaded?.({ id: instance.id, number: instance.number });
    }
    async function readDetail(id: string, afterSave: 'draft' | 'approval' | false = false, preserveView = false) {
        if (deleteInFlight.current || deletionUncertainRef.current)
            return;
        setPendingDeletion(null);
        const sequence = ++detailSequence.current;
        detailRead.current?.abort();
        const controller = new AbortController();
        detailRead.current = controller;
        setDetailLoading(true);
        setError(null);
        try {
            const detail = await getBudgetInstance(id, controller.signal);
            if (!mounted.current || controller.signal.aborted || sequence !== detailSequence.current)
                return;
            synchronize(detail, preserveView, afterSave !== false || current?.id === detail.id);
            setNotice(afterSave === 'approval'
                ? 'Бюджет утверждён. Показано текущее состояние.'
                : afterSave === 'draft' ? 'Черновик сохранён. Показано текущее состояние.' : null);
            return detail;
        }
        catch (cause) {
            if (!mounted.current || controller.signal.aborted || sequence !== detailSequence.current)
                return;
            setError(budgetInstanceErrorMessage(cause));
            setNotice(afterSave
                ? 'Запись подтверждена, но текущее состояние не загрузилось. Перечитайте документ перед следующей правкой.'
                : null);
            if (mode !== 'list' || afterSave) {
                setNeedsRefresh(true);
                needsRefreshRef.current = true;
                notifyLock.current?.(true);
            }
        }
        finally {
            if (mounted.current && !controller.signal.aborted && sequence === detailSequence.current)
                setDetailLoading(false);
        }
    }
    function changed() {
        setPasteUndo(null);
        setPastePreview(null);
        setDirty(true);
        dirtyRef.current = true;
        setError(null);
        setNotice(null);
        setConfirmation(null);
        setPendingLineRemoval(null);
        notifyLock.current?.(true);
        // A new command is permitted only after a known previous outcome, never after an unknown write.
        if (!uncertainRef.current)
            attempt.current = null;
    }
    const formEditable = !reviewLocked && mode !== 'list' && !saving && !approving && !detailLoading && !uncertain
        && !approvalUncertain && !needsRefresh
        && (mode === 'create' || (current?.status === 'Draft' && current.canEdit === true));
    const editable = formEditable && pastePreview === null;
    const saveBlockedReason = pastePreview ? 'Завершите или отмените предпросмотр вставки перед сохранением.'
        : saving ? 'Сохранение уже выполняется. Дождитесь результата.'
            : detailLoading ? 'Загружается текущее состояние бюджета. Дождитесь завершения загрузки.'
                : needsRefresh ? 'Перед следующей правкой загрузите текущее состояние бюджета. Ввод сохранён в форме.'
                    : actualsDialogOpen ? 'Завершите или закройте копирование факта перед сохранением.'
                        : approving ? 'Выполняется утверждение бюджета. Дождитесь результата.'
                            : approvalUncertain ? 'Сначала повторите ту же попытку утверждения; её результат неизвестен.'
                                : reviewLocked ? 'Команда согласования блокирует правку. Дождитесь завершения команды или возврата бюджета на доработку.'
                                    : uncertain ? null
                                        : mode === 'edit' && current?.status === 'Approved' ? 'Бюджет утверждён. Редактирование лимитов закрыто.'
                                            : mode === 'edit' && current?.canEdit !== true ? 'Нет права изменять этот бюджет. Обратитесь к ответственному за доступ.'
                                                : mode === 'edit' && !dirty ? 'Нет несохранённых изменений.'
                                                    : (mode === 'edit' || mode === 'create') && cashFlowOptions.loading ? 'Статьи ДДС загружаются. Дождитесь завершения загрузки.'
                                                        : (mode === 'edit' || mode === 'create') && cashFlowOptions.error !== null ? 'Справочник статей ДДС не загружен. Повторите его загрузку.' : null;
    function diagnosticCatalogue() {
        return [...(cashFlowOptions.selectedItems ?? []), ...(cashFlowOptions.allScopedItems ?? cashFlowOptions.items)]
            .filter((item, index, all) => all.findIndex(candidate => sameId(candidate.id, item.id)) === index);
    }
    function diagnosticCenters(): BudgetInstanceCostCenter[] {
        return [...knownCenters, ...creationCenters, ...(chosenCenter ? [chosenCenter] : []),
            ...(current?.costCenterCode && current.costCenterName ? [{ id: current.costCenterId,
                    code: current.costCenterCode, name: current.costCenterName, isActive: true }] : [])];
    }
    function startCreate() {
        if (saveInFlight.current || approvalInFlight.current || deleteInFlight.current
            || deletionUncertainRef.current || detailLoading || mode !== 'list')
            return;
        setCopySeed(null);
        setPendingDeletion(null);
        setPastePreview(null);
        setPasteUndo(null);
        actualsCalendar.current = null;
        detailSequence.current += 1;
        detailRead.current?.abort();
        closeActualsDialog();
        const month = todayMonth();
        setCurrent(null);
        setChosenCenter(null);
        centerChosenManually.current = false;
        setCanChooseAnyCenter(false);
        setCreationCenters([]);
        setLines([emptyLineForPeriod(month)]);
        setTabular(false);
        setMatrixDrafts({});
        setMatrixEdited(false);
        setMode('create');
        setError(null);
        setNotice(null);
        setDirty(false);
        dirtyRef.current = false;
        setUncertain(false);
        uncertainRef.current = false;
        setApprovalUncertain(false);
        approvalUncertainRef.current = false;
        setNeedsRefresh(false);
        needsRefreshRef.current = false;
        setPendingLineRemoval(null);
        setConfirmation(null);
        attempt.current = null;
        approvalAttempt.current = null;
        centers.setQuery('');
        cashFlowOptions.setQuery('');
    }
    function leaveForm() {
        if (saveInFlight.current || uncertainRef.current || approvalInFlight.current || approvalUncertainRef.current || reviewLockedRef.current)
            return;
        detailSequence.current += 1;
        detailRead.current?.abort();
        setPastePreview(null);
        setPasteUndo(null);
        closeActualsDialog();
        actualsCalendar.current = null;
        setMode('list');
        setCurrent(null);
        setChosenCenter(null);
        setCopySeed(null);
        setLines([]);
        setTabular(false);
        setMatrixDrafts({});
        setMatrixEdited(false);
        setError(null);
        setNotice(null);
        setDirty(false);
        dirtyRef.current = false;
        setNeedsRefresh(false);
        needsRefreshRef.current = false;
        setPendingLineRemoval(null);
        setConfirmation(null);
        attempt.current = null;
        approvalAttempt.current = null;
        notifyLock.current?.(false);
        setListRevision(value => value + 1);
    }
    function requestReload() {
        if (reviewLocked || !current || saveInFlight.current || uncertainRef.current || approvalInFlight.current
            || approvalUncertainRef.current || detailLoading)
            return;
        if (dirtyRef.current)
            setConfirmation('reload');
        else
            void readDetail(current.id);
    }
    function changeLine(index: number, patch: Partial<DraftLine>) {
        if (!editable || saveInFlight.current || uncertainRef.current || needsRefreshRef.current)
            return;
        if (patch.cashFlowItemId && !sameId(patch.cashFlowItemId, linesRef.current[index]?.cashFlowItemId ?? '')) {
            const direction = selectedBudgetDirection(diagnosticCatalogue(), patch.cashFlowItemId);
            if (!direction) {
                setError('Для этой статьи не задано направление. Укажите его в справочнике «Статьи ДДС».');
                return;
            }
            patch = { ...patch, flowDirection: direction };
        }
        setLines(previous => previous.map((line, row) => row === index ? { ...line, ...patch } : line));
        changed();
    }
    function copyLine(index: number) {
        if (!editable || actualsDialogOpen || saveInFlight.current || approvalInFlight.current
            || uncertainRef.current || needsRefreshRef.current || reviewLockedRef.current)
            return;
        const original = linesRef.current[index];
        if (!original || cashFlowOptions.loading || cashFlowOptions.error
            || !(cashFlowOptions.allScopedItems ?? cashFlowOptions.items).some(item => sameId(item.id, original.cashFlowItemId)))
            return;
        setLineSort(null);
        setSortedRowKeys(null);
        setLines(previous => {
            const source = previous[index];
            return source ? [...previous.slice(0, index + 1), { ...source, uiRowKey: createOperationKey() }, ...previous.slice(index + 1)] : previous;
        });
        changed();
    }
    function addLine() {
        if (!editable || actualsDialogOpen || saveInFlight.current || approvalInFlight.current
            || uncertainRef.current || needsRefreshRef.current || reviewLockedRef.current)
            return;
        setLineSort(null);
        setSortedRowKeys(null);
        const uiRowKey = createOperationKey();
        const line = { ...emptyLineForPeriod(derivePeriod(linesRef.current)?.fromMonth ?? todayMonth()), uiRowKey };
        setLines(previous => [line, ...previous]);
        setNewRowKeys(previous => new Set([...previous, uiRowKey]));
        newRowTimers.current.set(uiRowKey, setTimeout(() => {
            newRowTimers.current.delete(uiRowKey);
            setNewRowKeys(previous => { const next = new Set(previous); next.delete(uiRowKey); return next; });
        }, 1000));
        changed();
    }
    function removeZeroLines() {
        const blocked = () => !editable || tabular || actualsDialogOpen || saveInFlight.current || approvalInFlight.current
            || uncertainRef.current || approvalUncertainRef.current || needsRefreshRef.current || reviewLockedRef.current
            || (mode === 'create' && creatorCenterLoading) || Object.keys(matrixDraftsRef.current).length > 0;
        if (blocked())
            return;
        const source = linesRef.current;
        const kept = source.filter(line => parseLimit(line.limitAmountKzt) !== 0);
        const count = source.length - kept.length;
        if (count === 0) {
            setNotice('Строк с нулевой суммой нет.');
            return;
        }
        const generation = detailSequence.current;
        if (!window.confirm(`Удалить строки с суммой 0: ${count}? Ненулевые суммы и незаполненные строки сохранятся. Изменения запишутся только после сохранения черновика.`))
            return;
        // Never apply a confirmation to a different document or changed local input.
        if (blocked() || generation !== detailSequence.current || source !== linesRef.current)
            return;
        const next = kept.length > 0 ? kept : [{ ...emptyLineForPeriod(derivePeriod(source)?.fromMonth ?? todayMonth()), uiRowKey: createOperationKey() }];
        // Removing zero-only boundary months must not silently narrow the annual header.
        // Preserve a header only if the calendar had not already been changed manually.
        const priorCalendar = actualsCalendar.current;
        const headerWasPreserved = current !== null && (matrixEdited || sameLineCalendar(source, current.lines)
            || (priorCalendar !== null && priorCalendar.documentGeneration === generation && sameLineCalendar(source, priorCalendar.lines)));
        actualsCalendar.current = headerWasPreserved ? { documentGeneration: generation, lines: next } : null;
        setLineSort(null);
        setSortedRowKeys(null);
        linesRef.current = next;
        setLines(next);
        changed();
        setNotice(`Удалено строк с нулевой суммой: ${count}. Сохраните черновик.${kept.length === 0 ? ' Оставлена новая незаполненная строка; заполните её для сохранения.' : ''}`);
    }
    function changeCostCenter(id: string) {
        if (mode !== 'create' || !editable || !canChooseAnyCenter || creatorCenterLoading || actualsDialogOpen)
            return;
        const center = creationCenters.find(item => sameId(item.id, id)) ?? null;
        if (id !== '' && center === null)
            return;
        centerChosenManually.current = true;
        if (chosenCenter?.id.toLowerCase() === center?.id.toLowerCase())
            return;
        const prepared = materializeMatrix();
        if (!prepared)
            return;
        closeActualsDialog();
        detailSequence.current += 1;
        actualsCalendar.current = null;
        setChosenCenter(center);
        setCreatorCenterError(null);
        setLines(prepared.map(line => ({ ...line, cashFlowItemId: '' })));
        setTabular(false);
        setMatrixDrafts({});
        setMatrixEdited(false);
        setPendingLineRemoval(null);
        cashFlowOptions.setQuery('');
        changed();
        setNotice('ЦФО изменён. Выберите его статьи ДДС; суммы, месяцы и комментарии сохранены.');
    }
    function closeActualsDialog() {
        actualsSequence.current += 1;
        actualsRead.current?.abort();
        setActualsDialogOpen(false);
    }
    async function copyActuals(selection: BudgetCopyActualsSelection) {
        const costCenterId = mode === 'create' ? chosenCenter?.id : current?.costCenterId;
        const period = derivePeriod(lines);
        if (!editable || !costCenterId || !actualsDialogOpen || !period
            || (mode === 'create' && (creatorCenterLoading || creatorCenterError !== null))
            || Number(period.from.slice(0, 4)) < 2026 || period.from.slice(0, 4) !== period.to.slice(0, 4))
            return;
        const input: BudgetActualsInput = {
            costCenterId, fromMonth: `${selection.fromMonth}-01`, toMonth: `${selection.toMonth}-01`,
            cashFlowItemIds: [...selection.cashFlowItemIds],
            ...(current ? { budgetInstanceId: current.id, expectedConcurrencyStamp: current.concurrencyStamp } : {}),
        };
        const originalLines = linesRef.current;
        const documentGeneration = detailSequence.current;
        const requestGeneration = ++actualsSequence.current;
        actualsRead.current?.abort();
        const controller = new AbortController();
        actualsRead.current = controller;
        const preview = publicBudgetActualsPreview(await previewBudgetActuals(input, controller.signal), input);
        if (!mounted.current || controller.signal.aborted || actualsSequence.current !== requestGeneration
            || detailSequence.current !== documentGeneration || linesRef.current !== originalLines
            || articleCostCenterRef.current?.toLowerCase() !== costCenterId.toLowerCase())
            return;
        // Only the sole empty initial row of a new form is a disposable placeholder.
        // User-added blank rows (including period boundaries) remain untouched.
        const initialPlaceholder = mode === 'create' && originalLines.length === 1
            && originalLines[0].cashFlowItemId === '' && originalLines[0].limitAmountKzt === '' && originalLines[0].comment === '';
        const replacement = replaceBudgetActualsSlice(initialPlaceholder ? [] : originalLines, preview);
        if (replacement.zeroedRows > 0 && !window.confirm(`Копирование обнулит ${replacement.zeroedRows} ранее введённых сумм в выбранных статьях и месяцах. Остальные строки сохранятся. Продолжить?`)) {
            setNotice('Копирование отменено. Ввод не изменён.');
            return;
        }
        // Copying values into previously empty months does not redefine the
        // document's annual header. A later explicit row-calendar change does;
        // cancelled, stale or failed previews must never install this marker.
        const previousActualsCalendar = actualsCalendar.current;
        const headerWasPreserved = current !== null && (matrixEdited
            || sameLineCalendar(originalLines, current.lines)
            || (previousActualsCalendar !== null && previousActualsCalendar.documentGeneration === documentGeneration
                && sameLineCalendar(originalLines, previousActualsCalendar.lines)));
        // Do not undo a manual calendar change made before opening the dialog.
        actualsCalendar.current = headerWasPreserved ? { documentGeneration, lines: replacement.lines } : null;
        setLines(replacement.lines);
        setMatrixDrafts({});
        setPendingLineRemoval(null);
        changed();
        setNotice('Факт выбранного года скопирован в форму. Сохраните черновик.');
    }
    const matrixItems = [...(cashFlowOptions.allScopedItems ?? cashFlowOptions.items),
        ...(mode === 'edit' ? (cashFlowOptions.selectedItems ?? []).filter(item => lines.some(line => sameId(line.cashFlowItemId, item.id))) : [])]
        .filter((item, index, all) => all.findIndex(candidate => sameId(candidate.id, item.id)) === index);
    const matrixBase = copySeed ?? current;
    const derivedMatrixPeriod = derivePeriod(lines);
    const matrixFrom = [matrixBase?.validFrom.slice(0, 7), derivedMatrixPeriod?.fromMonth]
        .filter((month): month is string => month !== undefined).sort().at(0) ?? todayMonth();
    const matrixThrough = [matrixBase?.validTo.slice(0, 7) ?? matrixFrom, derivedMatrixPeriod?.toMonth ?? matrixFrom].sort().at(-1)!;
    const matrixMonths = budgetMatrixMonths(matrixFrom, matrixThrough);
    const pasteScopeKey = budgetPasteScopeKey(cashFlowOptions.allScopedItems ?? cashFlowOptions.items, matrixMonths, matrixItems);
    function preparePaste(articleId: string, month: string, text: string) {
        if (!editable || actualsDialogOpen || cashFlowOptions.loading || cashFlowOptions.error !== null
            || saveInFlight.current || approvalInFlight.current || (mode === 'create' && creatorCenterLoading))
            return;
        try {
            const plan = prepareBudgetMatrixPaste(text, articleId, month, matrixItems, (cashFlowOptions.allScopedItems ?? cashFlowOptions.items).map(item => item.id), matrixMonths, previewBudgetMatrix(linesRef.current, matrixDraftsRef.current), linesRef.current);
            plan.cells = plan.cells.map(cell => {
                if (cell.raw === null)
                    return cell;
                const item = matrixItems.find(candidate => sameId(candidate.id, cell.articleId));
                if (!item)
                    throw new BudgetMatrixInputError('unavailableArticle');
                const raw = manualBudgetCellText(linesRef.current, item, cell.month, cell.raw, 'paste');
                return { ...cell, raw, afterCents: signedBudgetCents(raw) };
            });
            setError(null);
            setPastePreview({ plan, generation: detailSequence.current, centerId: articleCostCenterId,
                scopeKey: pasteScopeKey, source: linesRef.current, drafts: matrixDraftsRef.current, stamp: current?.concurrencyStamp });
        }
        catch (failure) {
            setError(failure instanceof BudgetMatrixInputError ? budgetMatrixErrorMessage(failure) : budgetPasteErrorMessage(failure));
        }
    }
    function applyPaste() {
        const preview = pastePreview;
        if (!preview)
            return;
        if (!formEditable || saveInFlight.current || approvalInFlight.current || actualsDialogOpen
            || cashFlowOptions.loading || cashFlowOptions.error !== null || (mode === 'create' && creatorCenterLoading)
            || preview.generation !== detailSequence.current || preview.centerId !== articleCostCenterId
            || preview.scopeKey !== pasteScopeKey || preview.source !== linesRef.current
            || preview.drafts !== matrixDraftsRef.current || preview.stamp !== current?.concurrencyStamp) {
            setPastePreview(null);
            setError('Форма или доступные статьи изменились. Подготовьте вставку заново.');
            return;
        }
        const undo = { lines: [...linesRef.current], drafts: { ...matrixDraftsRef.current }, dirty: dirtyRef.current,
            pastedKeys: new Set(pastedMatrixCells.current),
            matrixEdited, calendar: actualsCalendar.current, generation: detailSequence.current, centerId: articleCostCenterId };
        const next = pasteBudgetMatrixDrafts(matrixDraftsRef.current, preview.plan);
        for (const cell of preview.plan.cells)
            pastedMatrixCells.current.add(budgetMatrixCellKey(cell.articleId, cell.month));
        matrixDraftsRef.current = next;
        setMatrixDrafts(next);
        changed();
        setPasteUndo(undo);
        setNotice('Вставка применена к форме. Сохраните черновик; отмена доступна до следующей правки.');
    }
    function undoPaste() {
        if (!pasteUndo || !editable || actualsDialogOpen || saveInFlight.current || approvalInFlight.current
            || pasteUndo.generation !== detailSequence.current || pasteUndo.centerId !== articleCostCenterId
            || cashFlowOptions.loading || cashFlowOptions.error !== null)
            return;
        setLines(pasteUndo.lines);
        linesRef.current = pasteUndo.lines;
        setMatrixDrafts(pasteUndo.drafts);
        matrixDraftsRef.current = pasteUndo.drafts;
        pastedMatrixCells.current = new Set(pasteUndo.pastedKeys);
        setMatrixEdited(pasteUndo.matrixEdited);
        actualsCalendar.current = pasteUndo.calendar;
        setDirty(pasteUndo.dirty);
        dirtyRef.current = pasteUndo.dirty;
        notifyLock.current?.(pasteUndo.dirty);
        setPasteUndo(null);
        setTabular(true);
        setPendingLineRemoval(null);
        setError(null);
        setNotice('Последняя вставка отменена.');
    }
    function changeMatrixCell(articleId: string, month: string, value: string) {
        if (!editable || actualsDialogOpen || saveInFlight.current || uncertainRef.current || needsRefreshRef.current
            || (mode === 'create' && creatorCenterLoading))
            return;
        if (!matrixItems.some(item => sameId(item.id, articleId)) || !matrixMonths.includes(month))
            return;
        if (signedBudgetCents(value) !== null) {
            const item = matrixItems.find(candidate => sameId(candidate.id, articleId))!;
            try {
                value = manualBudgetCellText(linesRef.current, item, month, value);
            }
            catch (failure) {
                setError(budgetMatrixErrorMessage(failure));
                return;
            }
        }
        const key = budgetMatrixCellKey(articleId, month);
        pastedMatrixCells.current.delete(key);
        const next = { ...matrixDraftsRef.current, [key]: value };
        matrixDraftsRef.current = next;
        setMatrixDrafts(next);
        changed();
    }
    function materializeMatrix(failedSubmit = false): DraftLine[] | null {
        const sourceLines = linesRef.current;
        const drafts = Object.entries(matrixDraftsRef.current);
        if (drafts.length === 0)
            return sourceLines;
        const invalidDrafts = drafts.filter(([, text]) => text !== null && signedBudgetCents(text) === null);
        if (invalidDrafts.length > 0) {
            const issues = invalidDrafts.map(([key]) => {
                const [id, month] = key.split('\u0000');
                const sourceIndex = sourceLines.findIndex(line => sameId(line.cashFlowItemId, id) && line.budgetMonth === month);
                const label = budgetSaveRowLabel({ cashFlowItemId: id, budgetMonth: month, limitAmountKzt: '', comment: '', flowDirection: 'Payment' }, Math.max(0, sourceIndex), diagnosticCatalogue());
                return `${sourceIndex < 0 ? label.replace(/^Строка 1:/u, 'Ячейка:') : label} — изменённая ячейка: введите сумму с точностью до двух знаков; для нулевого лимита укажите 0.`;
            });
            if (failedSubmit)
                showSaveFailure('Черновик не сохранён. Исправьте изменённые ячейки.', issues);
            else {
                setError('Исправьте изменённые ячейки.');
                setSaveIssues(issues);
            }
            return null;
        }
        const mixed = drafts.some(([key, text]) => {
            const [id, month] = key.split('\u0000');
            if (text === null)
                return false;
            const rows = sourceLines.filter(line => sameId(line.cashFlowItemId, id) && line.budgetMonth === month);
            const previous = budgetMatrixAmount(rows, [id], [month]);
            if (!pastedMatrixCells.current.has(key) && !previous.invalid && previous.cents === signedBudgetCents(text))
                return false;
            return rows.some(line => line.flowDirection === 'Receipt') && rows.some(line => line.flowDirection === 'Payment');
        });
        if (mixed && !window.confirm('В изменённых ячейках есть поступления и выплаты одновременно. Заменить их одной суммой по направлению из справочника? Знак введённой или вставленной суммы направление не меняет. Расходный лимит этих ячеек изменится. Остальные ячейки сохранятся.')) {
            setNotice('Сохранение отменено. Исходные суммы и введённые ячейки сохранены в форме.');
            return null;
        }
        try {
            // Only the untouched sole initial placeholder is disposable. Other incomplete rows must be fixed explicitly.
            let next = mode === 'create' && sourceLines.length === 1 && !sourceLines[0].cashFlowItemId
                && !sourceLines[0].limitAmountKzt && !sourceLines[0].comment ? [] : sourceLines;
            for (const [key, text] of drafts) {
                const [id, month] = key.split('\u0000');
                const article = matrixItems.find(item => sameId(item.id, id));
                if (!article)
                    throw new BudgetMatrixInputError('unavailableArticle');
                next = text === null ? next.filter(line => !sameId(line.cashFlowItemId, article.id) || line.budgetMonth !== month)
                    : replaceManualBudgetCell(next, sourceLines, article, month, text, true, pastedMatrixCells.current.has(key) ? 'paste' : 'manual');
            }
            return next;
        }
        catch (failure) {
            if (failedSubmit)
                showSaveFailure(budgetMatrixErrorMessage(failure));
            else
                setError(budgetMatrixErrorMessage(failure));
            return null;
        }
    }
    function openActualsDialog() {
        if (!editable || actualsDialogOpen || saveInFlight.current || uncertainRef.current || needsRefreshRef.current
            || (mode === 'create' && creatorCenterLoading))
            return;
        if (Object.keys(matrixDraftsRef.current).length > 0) {
            const prepared = materializeMatrix();
            if (!prepared)
                return;
            setLines(prepared);
            setMatrixDrafts({});
            matrixDraftsRef.current = {};
            setMatrixEdited(true);
        }
        setActualsDialogOpen(true);
    }
    function toggleTabular() {
        if (reviewLocked || saving || approving || detailLoading || uncertain || approvalUncertain || needsRefresh || actualsDialogOpen || pastePreview
            || (mode === 'create' && creatorCenterLoading))
            return;
        if (tabular && Object.keys(matrixDraftsRef.current).length > 0) {
            const prepared = materializeMatrix();
            if (!prepared)
                return;
            setLines(prepared);
            setMatrixDrafts({});
            matrixDraftsRef.current = {};
            setMatrixEdited(true);
        }
        setTabular(previous => !previous);
    }
    function exportExcel() {
        if (!current || dirty || saving || approving || detailLoading || uncertain || approvalUncertain
            || needsRefresh || actualsDialogOpen || pastePreview || reviewLocked || cashFlowOptions.loading || cashFlowOptions.error)
            return;
        try {
            downloadBudgetWorkbook({ number: current.number, center: centerLabel(current),
                lines: current.lines.map(line => ({ ...line, comment: line.comment ?? '',
                    flowDirection: line.flowDirection ?? 'Payment', limitAmountKzt: amountInputValue(line.limitAmountKzt) })),
                items: matrixItems, months: matrixMonths });
        }
        catch {
            setError('Не удалось выгрузить бюджет. Проверьте суммы и повторите выгрузку.');
        }
    }
    function submit(event: FormEvent) { event.preventDefault(); void saveDraft(); }
    async function prepareBudgetSubmission(): Promise<BudgetInstance | undefined> {
        if (pastePreview || actualsDialogOpen || saving || approving || uncertain || approvalUncertain || needsRefresh
            || detailLoading || reviewPanelLocked || (mode !== 'create' && mode !== 'edit'))
            return;
        if (mode === 'create' || dirtyRef.current)
            return saveDraft();
        if (!current)
            return;
        const fresh = await readDetail(current.id, false, true);
        if (fresh && fresh.concurrencyStamp !== current.concurrencyStamp) {
            setNotice('Бюджет изменился. Проверьте загруженные изменения и подайте его снова.');
            return;
        }
        return fresh;
    }
    async function saveDraft(): Promise<BudgetInstance | undefined> {
        if (saveBlockedReason || saveInFlight.current || needsRefreshRef.current || (mode !== 'create' && mode !== 'edit')) {
            showSaveFailure(saveBlockedReason ?? 'Сохранение сейчас недоступно. Дождитесь завершения текущей команды.');
            return;
        }
        let frozen = attempt.current;
        if (uncertainRef.current) {
            if (!frozen)
                return;
        }
        else {
            if (!editable || (mode === 'edit' && !dirtyRef.current)) {
                showSaveFailure('Сохранение недоступно: правка закрыта или нет несохранённых изменений.');
                return;
            }
            if (cashFlowOptions.loading) {
                showSaveFailure('Статьи ДДС ещё загружаются. Повторите попытку после завершения загрузки.');
                return;
            }
            if (cashFlowOptions.error !== null) {
                showSaveFailure('Не удалось загрузить статьи ДДС. Повторите загрузку справочника.');
                return;
            }
            const editingMatrix = Object.keys(matrixDraftsRef.current).length > 0;
            const effectiveLines = materializeMatrix(true);
            if (!effectiveLines)
                return;
            const derived = derivePeriod(effectiveLines);
            const actualsCalendarUnchanged = actualsCalendar.current !== null
                && actualsCalendar.current.documentGeneration === detailSequence.current
                && sameLineCalendar(effectiveLines, actualsCalendar.current.lines);
            // Preserve empty boundary months of the source header. User-entered
            // months outside it may extend the copy, but never silently shrink it.
            // Money, comments, direction or article changes do not change the calendar.
            // Keep empty header months; explicit changes of line months still derive a new period.
            const preserved = mode === 'create' ? copySeed
                : current && (editingMatrix || matrixEdited || actualsCalendarUnchanged
                    || sameLineCalendar(effectiveLines, current.lines)) ? current : null;
            const period = derived && preserved
                ? { ...derived, from: derived.from < preserved.validFrom ? derived.from : preserved.validFrom,
                    to: derived.to > preserved.validTo ? derived.to : preserved.validTo } : derived;
            const prepared = period ? payloadLines(effectiveLines, period.from, period.to) : null;
            const issues = collectBudgetSaveLineIssues(effectiveLines, period, parseLimit, diagnosticCatalogue(), mode === 'create'
                ? { targetId: chosenCenter?.id, allowedIds: (cashFlowOptions.allScopedItems ?? cashFlowOptions.items).map(item => item.id),
                    centers: diagnosticCenters() } : undefined);
            if (!prepared || issues.length > 0) {
                showSaveFailure('Черновик не сохранён. Исправьте строки бюджета.', issues.length > 0 ? issues
                    : ['Не удалось определить период бюджета. Укажите корректные месяцы строк.']);
                return;
            }
            if (mode === 'create' && creatorCenterLoading) {
                showSaveFailure('ЦФО автора ещё определяется. Повторите попытку после завершения загрузки.');
                return;
            }
            if (mode === 'create' && creatorCenterError !== null) {
                showSaveFailure(creatorCenterError);
                return;
            }
            if (mode === 'create' && chosenCenter === null) {
                showSaveFailure('ЦФО автора не определён. Настройте одну активную связь сотрудника с ЦФО.');
                return;
            }
            if (mode === 'edit' && (!current || current.canEdit !== true))
                return;
            try {
                const key = newAttemptKey();
                const copied = prepared.map(line => Object.freeze({ ...line }));
                Object.freeze(copied);
                frozen = mode === 'create'
                    ? { kind: 'create', key, input: { costCenterId: chosenCenter!.id, validFrom: period!.from, validTo: period!.to, lines: copied } }
                    : { kind: 'update', key, id: current!.id,
                        input: { expectedConcurrencyStamp: current!.concurrencyStamp, validFrom: period!.from, validTo: period!.to, lines: copied } };
                Object.freeze(frozen.input);
                Object.freeze(frozen);
                attempt.current = frozen;
            }
            catch {
                showSaveFailure('Не удалось подготовить безопасный ключ операции. Запрос не отправлен.');
                return;
            }
        }
        if (!frozen)
            return;
        const hadUncertainOutcome = uncertainRef.current;
        const ticket = saveCommand.acquire();
        if (!ticket) {
            showSaveFailure('Другая команда блокирует сохранение или рабочая область устарела. Дождитесь завершения команды и проверьте текущее состояние.');
            return;
        }
        saveInFlight.current = true;
        setSaving(true);
        setError(null);
        setNotice(null);
        setConfirmation(null);
        notifyLock.current?.(true);
        try {
            const saved = frozen.kind === 'create'
                ? await createBudgetInstance(frozen.input, frozen.key)
                : await updateBudgetInstance(frozen.id, frozen.input, frozen.key);
            if (!mounted.current || attempt.current !== frozen)
                return;
            // A replay response may describe an older successful write; read fresh before permitting another edit.
            attempt.current = null;
            setUncertain(false);
            uncertainRef.current = false;
            setCurrent(saved);
            setMode('edit');
            setNeedsRefresh(true);
            needsRefreshRef.current = true;
            setNotice('Черновик сохранён. Загружается текущее состояние.');
            documentCallbacks.current.onDocumentSaved?.({ id: saved.id, number: saved.number });
            const fresh = await readDetail(saved.id, 'draft');
            if (mounted.current)
                setListRevision(value => value + 1);
            if (fresh && fresh.id === saved.id && fresh.concurrencyStamp === saved.concurrencyStamp)
                return fresh;
            if (fresh)
                setNotice('Черновик сохранён, но затем изменился. Проверьте загруженные изменения перед подачей.');
        }
        catch (cause) {
            if (!mounted.current || attempt.current !== frozen)
                return;
            const details = budgetInstanceErrorDetails(cause);
            const submitted = frozen.input.lines.map(line => ({ ...line, limitAmountKzt: amountInputValue(line.limitAmountKzt),
                comment: line.comment ?? '', flowDirection: line.flowDirection ?? 'Payment' }));
            const rowIssues = details?.rowIndices.filter(index => index < submitted.length).map(index => {
                const line = submitted[index];
                const reason = details.code === 'budget-article-cfo-mismatch'
                    ? budgetArticleAssignmentReason(line, diagnosticCatalogue(), articleCostCenterId, diagnosticCenters())
                    : details.code === 'budget-lines-invalid' ? 'строка лимита отклонена. Проверьте статью, месяц, сумму и текст операции (не более 2 000 символов, без управляющих символов)'
                        : 'статья отсутствует в доступном справочнике. Обновите справочник или выберите другую статью';
                return `${budgetSaveRowLabel(line, index, diagnosticCatalogue())} — ${reason}.`;
            }) ?? [];
            showSaveFailure(budgetInstanceErrorMessage(cause), rowIssues);
            if (hadUncertainOutcome || isBudgetInstanceResultUncertain(cause)) {
                setUncertain(true);
                uncertainRef.current = true;
                // Keep frozen body and key. This button retry is the only path that may send it again.
            }
            else {
                attempt.current = null;
                setUncertain(false);
                uncertainRef.current = false;
                if (!isBudgetInstanceValidationError(cause)) {
                    setNeedsRefresh(true);
                    needsRefreshRef.current = true;
                    if (isBudgetInstanceConflict(cause) && mode === 'create') {
                        showSaveFailure('Создание отклонено из-за конфликта. Ввод сохранён. Вернитесь к списку и проверьте его перед новой попыткой.');
                    }
                }
            }
        }
        finally {
            saveInFlight.current = false;
            if (saveCommand.isCurrent(ticket)) {
                if (uncertainRef.current)
                    saveCommand.retain(ticket);
                else
                    saveCommand.release(ticket);
            }
            if (mounted.current)
                setSaving(false);
        }
    }
    async function approve() {
        if (!current || current.status !== 'Draft' || mode !== 'edit'
            || actualsDialogOpen || saveInFlight.current || approvalInFlight.current || detailLoading || needsRefreshRef.current)
            return;
        let frozen = approvalAttempt.current;
        if (approvalUncertainRef.current) {
            if (!frozen)
                return;
        }
        else {
            if (dirtyRef.current) {
                setError('Сначала сохраните или отмените изменения черновика.');
                return;
            }
            try {
                frozen = {
                    key: newAttemptKey(),
                    id: current.id,
                    input: { expectedConcurrencyStamp: current.concurrencyStamp },
                };
                Object.freeze(frozen.input);
                Object.freeze(frozen);
                approvalAttempt.current = frozen;
            }
            catch {
                setError('Не удалось подготовить безопасный ключ операции. Запрос не отправлен.');
                return;
            }
        }
        if (!frozen)
            return;
        const hadUncertainOutcome = approvalUncertainRef.current;
        const ticket = approveCommand.acquire();
        if (!ticket)
            return;
        approvalInFlight.current = true;
        setApproving(true);
        setError(null);
        setNotice(null);
        setConfirmation(null);
        notifyLock.current?.(true);
        try {
            const approved = await approveBudgetInstance(frozen.id, frozen.input, frozen.key);
            if (!mounted.current || approvalAttempt.current !== frozen)
                return;
            approvalAttempt.current = null;
            setApprovalUncertain(false);
            approvalUncertainRef.current = false;
            setCurrent(approved);
            setNeedsRefresh(true);
            needsRefreshRef.current = true;
            setNotice('Бюджет утверждён. Загружается текущее состояние.');
            documentCallbacks.current.onDocumentSaved?.({ id: approved.id, number: approved.number });
            await readDetail(approved.id, 'approval');
            if (mounted.current)
                setListRevision(value => value + 1);
        }
        catch (cause) {
            if (!mounted.current || approvalAttempt.current !== frozen)
                return;
            setError(budgetInstanceErrorMessage(cause));
            if (hadUncertainOutcome || isBudgetInstanceResultUncertain(cause)) {
                setApprovalUncertain(true);
                approvalUncertainRef.current = true;
            }
            else {
                approvalAttempt.current = null;
                setApprovalUncertain(false);
                approvalUncertainRef.current = false;
                if (!isBudgetInstanceValidationError(cause)) {
                    setNeedsRefresh(true);
                    needsRefreshRef.current = true;
                }
            }
        }
        finally {
            approvalInFlight.current = false;
            if (approveCommand.isCurrent(ticket)) {
                if (approvalUncertainRef.current)
                    approveCommand.retain(ticket);
                else
                    approveCommand.release(ticket);
            }
            if (mounted.current)
                setApproving(false);
        }
    }
    async function copyInstance(id: string) {
        if (mode !== 'list' || detailLoading || listLoading || deleteInFlight.current || deletionUncertainRef.current)
            return;
        setPendingDeletion(null);
        const sequence = ++detailSequence.current;
        detailRead.current?.abort();
        const controller = new AbortController();
        detailRead.current = controller;
        setDetailLoading(true);
        setError(null);
        setNotice(null);
        try {
            const source = await getBudgetInstance(id, controller.signal);
            if (!mounted.current || controller.signal.aborted || sequence !== detailSequence.current)
                return;
            if (!source.costCenterCode || !source.costCenterName)
                throw new Error('Copy source label unavailable.');
            closeActualsDialog();
            actualsCalendar.current = null;
            setCurrent(null);
            centerChosenManually.current = true;
            setCanChooseAnyCenter(false);
            setCreationCenters([]);
            setCopySeed({ number: source.number, validFrom: source.validFrom, validTo: source.validTo });
            setChosenCenter({ id: source.costCenterId, code: source.costCenterCode, name: source.costCenterName, isActive: true });
            setCreatorCenterLoading(false);
            setCreatorCenterError(null);
            setLines(linesFrom(source));
            setTabular(false);
            setMatrixDrafts({});
            setMatrixEdited(false);
            setMode('create');
            setDirty(true);
            dirtyRef.current = true;
            setUncertain(false);
            uncertainRef.current = false;
            setApprovalUncertain(false);
            approvalUncertainRef.current = false;
            setNeedsRefresh(false);
            needsRefreshRef.current = false;
            setPendingLineRemoval(null);
            setConfirmation(null);
            attempt.current = null;
            approvalAttempt.current = null;
            cashFlowOptions.setQuery('');
            notifyLock.current?.(true);
            setNotice(`Копия ${source.number}. Новый черновик ещё не сохранён.`);
        }
        catch (cause) {
            if (mounted.current && !controller.signal.aborted && sequence === detailSequence.current) {
                setError(budgetInstanceErrorMessage(cause));
            }
        }
        finally {
            if (mounted.current && !controller.signal.aborted && sequence === detailSequence.current)
                setDetailLoading(false);
        }
    }
    async function performDeletion(frozen: FrozenDeletion) {
        if (deleteInFlight.current || mode !== 'list')
            return;
        // A refusal of a retry cannot establish whether the earlier request
        // committed before its response was lost. Keep that attempt frozen.
        const hadUncertainOutcome = deletionUncertainRef.current;
        const ticket = deleteCommand.acquire();
        if (!ticket)
            return;
        deleteInFlight.current = true;
        setDeleting(true);
        setError(null);
        setNotice(null);
        listSequence.current += 1;
        listRead.current?.abort();
        notifyLock.current?.(true);
        try {
            await deleteBudgetInstance(frozen.id, frozen.input, frozen.key);
            if (!mounted.current || deletionAttempt.current !== frozen)
                return;
            deletionAttempt.current = null;
            setPendingDeletion(null);
            setDeletionUncertain(false);
            deletionUncertainRef.current = false;
            setNotice('Черновик удалён.');
            await reloadList(skip);
        }
        catch (cause) {
            if (!mounted.current || deletionAttempt.current !== frozen)
                return;
            setError(budgetInstanceDeletionErrorMessage(cause));
            if (hadUncertainOutcome || isBudgetInstanceResultUncertain(cause)) {
                setDeletionUncertain(true);
                deletionUncertainRef.current = true;
            }
            else {
                deletionAttempt.current = null;
                setPendingDeletion(null);
                setDeletionUncertain(false);
                deletionUncertainRef.current = false;
                await reloadList(skip);
            }
        }
        finally {
            deleteInFlight.current = false;
            if (deleteCommand.isCurrent(ticket)) {
                if (deletionUncertainRef.current)
                    deleteCommand.retain(ticket);
                else
                    deleteCommand.release(ticket);
            }
            if (mounted.current)
                setDeleting(false);
        }
    }
    function requestDeletion(item: BudgetInstanceListItem) {
        if (mode !== 'list' || listLoading || detailLoading || deleteInFlight.current || deletionUncertainRef.current
            || item.status !== 'Draft' || item.canDelete !== true)
            return;
        if (pendingDeletion !== item.id) {
            setPendingDeletion(item.id);
            setNotice(null);
            return;
        }
        try {
            const frozen = Object.freeze({ id: item.id, key: newAttemptKey(),
                input: Object.freeze({ costCenterId: item.costCenterId, expectedConcurrencyStamp: item.concurrencyStamp }) });
            deletionAttempt.current = frozen;
            void performDeletion(frozen);
        }
        catch {
            setError('Не удалось подготовить безопасный ключ операции. Запрос не отправлен.');
        }
    }
    const listActionsBlocked = reviewLocked || detailLoading || deleting || deletionUncertain;
    function centerLabel(instance: Pick<BudgetInstance, 'costCenterId' | 'costCenterCode' | 'costCenterName'>): string {
        if (instance.costCenterCode !== undefined && instance.costCenterName !== undefined) {
            return instance.costCenterName;
        }
        const center = knownCenters.find(item => sameId(item.id, instance.costCenterId));
        return center ? center.name : 'Наименование ЦФО не загружено';
    }
    // Capture display order only on a header click: typing must not move the focused row.
    function sortLineView(column: BudgetLineSortColumn) {
        if (saveInFlight.current || approvalInFlight.current || detailLoading || uncertainRef.current
            || approvalUncertainRef.current || reviewLockedRef.current || pastePreview || actualsDialogOpen)
            return;
        const next: BudgetLineSort | null = lineSort?.column !== column ? { column, direction: 'ascending' }
            : lineSort.direction === 'ascending' ? { column, direction: 'descending' } : null;
        const items = [...cashFlowOptions.items, ...(cashFlowOptions.allScopedItems ?? []), ...(cashFlowOptions.selectedItems ?? [])];
        setSortedRowKeys(next ? sortBudgetLines(linesRef.current, items, next).map(({ line }) => line.uiRowKey!) : null);
        setLineSort(next);
        setPendingLineRemoval(null);
    }
    const rowRanks = new Map((sortedRowKeys ?? []).map((key, index) => [key, index]));
    const displayLines = lines.map((line, sourceIndex) => ({ line, sourceIndex }));
    if (sortedRowKeys)
        displayLines.sort((a, b) => (rowRanks.get(a.line.uiRowKey!) ?? Number.MAX_SAFE_INTEGER)
            - (rowRanks.get(b.line.uiRowKey!) ?? Number.MAX_SAFE_INTEGER) || a.sourceIndex - b.sourceIndex);
    const parsedAmounts = lines.map(line => parseLimit(line.limitAmountKzt));
    const sumCents = parsedAmounts.reduce<number>((sum, value, index) => sum + (value === null || lines[index]?.flowDirection === 'Receipt' ? 0 : Math.round(value * 100)), 0);
    const receiptCents = parsedAmounts.reduce<number>((sum, value, index) => sum + (value === null || lines[index]?.flowDirection !== 'Receipt' ? 0 : Math.round(value * 100)), 0);
    const displayedTotal = parsedAmounts.some(value => value === null) || !Number.isSafeInteger(sumCents)
        ? 'Проверьте введённые суммы' : formatBudgetLimitDisplay(sumCents / 100);
    if (documentEntry.current !== undefined && mode === 'list')
        return (<div className="directory-page budget-instance-workspace">
      <h1 tabIndex={-1} ref={heading}>Открытие экземпляра бюджета</h1>
      {error !== null ? <p className="directory-error" role="alert">{error}</p>
                : <p role="status">Загрузка экземпляра бюджета…</p>}
      {error !== null && <button type="button" disabled={detailLoading} onClick={() => {
                    const entry = documentEntry.current;
                    if (entry?.kind === 'open')
                        void readDetail(entry.id);
                    else if (entry?.kind === 'copy')
                        void copyInstance(entry.id);
                }}>Повторить открытие</button>}
      <button type="button" onClick={onBackToList}>К списку</button>
    </div>);
    if (mode !== 'list')
        return (<div className="directory-page budget-instance-workspace">
      <div className="budget-document-heading">
      <h1 tabIndex={-1} ref={heading}>{mode === 'create'
                ? 'Новый экземпляр бюджета'
                : `Бюджет ${current?.number ?? ''}`}</h1>
      {mode === 'edit' && current && <span className="budget-heading-status" ref={setReviewStatusTarget}/>}
      {mode === 'edit' && current && <span className="budget-heading-cfo">ЦФО: {centerLabel(current)}</span>}
      <span className="budget-heading-route" ref={setReviewRouteTarget}/>
      </div>
      <BudgetApprovalHistoryPanel key={current?.id ?? 'new'} budget={mode === 'edit' ? current ?? undefined : undefined} toolbar={historyButton => <div className="directory-actions document-command-bar budget-document-command-bar budget-unified-toolbar" role="toolbar" aria-label="Действия экземпляра бюджета">
          {(mode === 'create' || dirty || uncertain) && <button type="submit" form={formId} className="budget-primary-action" disabled={saveBlockedReason !== null} title={saveBlockedReason ?? undefined}>
            {uncertain ? 'Повторить ту же попытку' : 'Сохранить черновик'}</button>}
          <span className="budget-unified-primary" ref={setReviewActionTarget}/>
          {mode === 'create' && <button type="button" className="budget-primary-action" disabled={saveBlockedReason !== null || submission.busy || submission.unknown} title="Сохранить, проверить и отправить на согласование" onClick={() => void submission.execute()}>Подать на согласование</button>}
          {submission.unknown && <button type="button" disabled={submission.busy} onClick={() => void submission.execute()}>Повторить подачу</button>}
          <button type="button" disabled={!editable || actualsDialogOpen
                    || (mode === 'create' && (creatorCenterLoading || creatorCenterError !== null))
                    || !(mode === 'create' ? chosenCenter : current)
                    || !derivePeriod(lines) || Number(derivePeriod(lines)?.from.slice(0, 4)) < 2026 || derivePeriod(lines)?.from.slice(0, 4) !== derivePeriod(lines)?.to.slice(0, 4)} title="Копирование факта по статьям данного ЦФО за выбранные месяцы исходного года, без ВГО" className="budget-copy-actuals-button" onClick={openActualsDialog}>Скопировать факт</button>
          <button type="button" aria-pressed={tabular} disabled={!!pastePreview || saving || approving || detailLoading || uncertain
                    || approvalUncertain || needsRefresh || actualsDialogOpen || (mode === 'create' && creatorCenterLoading)} onClick={toggleTabular}>
            {tabular ? 'Строковый вид' : 'Табличный вид'}</button>
          <button type="button" disabled={!current || dirty || saving || approving || detailLoading || uncertain || approvalUncertain
                    || needsRefresh || actualsDialogOpen || !!pastePreview || reviewLocked || cashFlowOptions.loading || cashFlowOptions.error !== null} title={!current || dirty ? 'Сначала сохраните черновик. Выгружается сохранённый бюджет.' : 'Табличный и строчный вид в одном файле'} onClick={exportExcel}>Выгрузить в Excel</button>
          {historyButton}
          <BudgetGuideLink />
          {current?.status === 'Approved' && onOpenAdjustments && <button type="button" disabled={dirty || saving || approving || uncertain || approvalUncertain} onClick={() => onOpenAdjustments(current.id)}>Корректировки экземпляра</button>}
          {pasteUndo && <button type="button" disabled={!editable || actualsDialogOpen || cashFlowOptions.loading} onClick={undoPaste}>Отменить последнюю вставку</button>}
          {approvalUncertain && <button type="button" disabled={!!pastePreview || reviewLocked || saving || approving || detailLoading || uncertain} onClick={() => void approve()}>Повторить утверждение</button>}
          <span className="document-save-state" role="status">{detailLoading ? 'Загрузка…' : saving ? 'Сохранение…'
                    : uncertain || approvalUncertain ? 'Результат записи неизвестен' : needsRefresh ? 'Требуется обновление документа'
                        : dirty ? 'Есть несохранённые изменения' : error ? 'Проверьте сообщение об ошибке'
                            : current ? 'Сохранено' : 'Новый черновик'}</span>
        </div>}>
      {mode === 'edit' && current && <BudgetApprovalPanel budget={current} compact actionTarget={reviewActionTarget} statusTarget={reviewStatusTarget} routeTarget={reviewRouteTarget} refreshToken={reviewReadToken} showSubmit={!submission.unknown && !uncertain && !approvalUncertain} onSubmitRequested={() => void submission.execute()} submissionDisabled={!!pastePreview || actualsDialogOpen || saving || detailLoading || uncertain || needsRefresh || approving || approvalUncertain || submission.busy || reviewPanelLocked} disabled={!!pastePreview || actualsDialogOpen || dirty || saving || detailLoading || uncertain || needsRefresh || approving || approvalUncertain || submission.busy || submission.unknown} onChanged={() => {
                    if (current) {
                        documentCallbacks.current.onDocumentSaved?.({ id: current.id, number: current.number });
                        void readDetail(current.id);
                    }
                }} onLocked={setReviewLocked}/>}
      </BudgetApprovalHistoryPanel>
      {submission.error && <p role="alert" className="directory-error">{submission.error}</p>}
      <form id={formId} className="budget-instance-document-form" noValidate onSubmit={event => void submit(event)}>
      <BudgetContextRow center={mode === 'create' ? <label className="budget-instance-center-field">ЦФО
          {canChooseAnyCenter ? <select aria-label="ЦФО" required value={chosenCenter?.id ?? ''} disabled={!editable || creatorCenterLoading || actualsDialogOpen} onChange={event => changeCostCenter(event.target.value)}>
            <option value="">Выберите ЦФО</option>
            {creationCenters.map(center => <option key={center.id} value={center.id}>{center.name}</option>)}
          </select> : <input aria-label="ЦФО" required readOnly value={chosenCenter?.name ?? ''} disabled={!editable || creatorCenterLoading}/>}
  </label> : undefined} actions={!tabular ? <div className="budget-line-toolbar" role="toolbar" aria-label="Действия со строками бюджета">
    <button type="button" disabled={!editable || actualsDialogOpen || (mode === 'create' && creatorCenterLoading)} onClick={addLine}>Добавить строку</button>
    <button type="button" disabled={!editable || actualsDialogOpen || (mode === 'create' && creatorCenterLoading)
                    || Object.keys(matrixDrafts).length > 0 || !lines.some(line => parseLimit(line.limitAmountKzt) === 0)} onClick={removeZeroLines}>Удалить пустые строки</button>
  </div> : undefined}>
      {mode === 'edit' && current?.status === 'Approved' && <p role="status">Статус: утверждён. Редактирование лимитов закрыто.</p>}
      {mode === 'edit' && current?.canEdit === false && <p role="alert">Правка этого документа недоступна.</p>}
      {notice !== null && <p role="status">{notice}</p>}
      {detailLoading && <p role="status">Загрузка текущего состояния экземпляра бюджета…</p>}
      {uncertain && <p role="alert">
        Результат записи неизвестен. Ввод и ключ попытки зафиксированы. Не создавайте второй документ:
        сначала повторите эту же попытку. Автоматического повтора нет.
      </p>}
      {approvalUncertain && <p role="alert">
        Результат утверждения неизвестен. Повторите ту же попытку; новый ключ не создаётся.
        Автоматического повтора нет.
      </p>}
        {needsRefresh && !uncertain && !approvalUncertain && <button type="button" disabled={reviewLocked || saving || approving || detailLoading || !!pastePreview} onClick={requestReload}>Перечитать после ошибки</button>}
        {error !== null && <div ref={saveAlert} tabIndex={-1} className="directory-error" role="alert" aria-label="Ошибка бюджета">
          <p>{error}</p>
          {saveIssues.length > 0 && <ul>{saveIssues.map((issue, index) => <li key={index}>{issue}</li>)}</ul>}
        </div>}
        {mode === 'create' && <>
          {creatorCenterLoading && <span role="status"> Загрузка доступных ЦФО…</span>}
          {creatorCenterError !== null && <span role="alert"> {creatorCenterError}</span>}
        </>}
        {cashFlowOptions.loading && <p role="status">Загрузка статей ДДС…</p>}
        {!cashFlowOptions.loading && cashFlowOptions.error === null && cashFlowOptions.items.length === 0
                && <p role="status">Для этого ЦФО статьи ДДС не назначены. Укажите ЦФО в справочнике статей ДДС.</p>}
        {cashFlowOptions.error !== null && <p className="directory-error" role="alert">{cashFlowOptions.error}
          <button type="button" disabled={!editable} onClick={cashFlowOptions.reload}>Повторить загрузку статей ДДС</button>
        </p>}
      </BudgetContextRow>
        {tabular ? <BudgetInstanceMatrix lines={lines} items={matrixItems} months={matrixMonths} cellDrafts={matrixDrafts} editable={formEditable && !actualsDialogOpen && !cashFlowOptions.loading && cashFlowOptions.error === null
                    && !(mode === 'create' && creatorCenterLoading)} onChange={changeMatrixCell} onPaste={preparePaste}/> : <div className="directory-table-wrap budget-instance-line-wrap" tabIndex={0} aria-label="Таблица строк черновика">
          <table className="directory-table budget-instance-line-table" aria-label="Строки сумм экземпляра бюджета" style={{ '--budget-line-width': `${lineColumns.totalWidth}px`, '--budget-line-actions-width': `${lineColumns.widths[5]}px` } as CSSProperties}>
            <colgroup>
              <col className="budget-instance-col-comment" style={{ width: lineColumns.widths[0] }}/>
              <col className="budget-instance-col-cash-flow" style={{ width: lineColumns.widths[1] }}/>
              <col className="budget-instance-col-direction" style={{ width: lineColumns.widths[2] }}/>
              <col className="budget-instance-col-month" style={{ width: lineColumns.widths[3] }}/>
              <col className="budget-instance-col-amount" style={{ width: lineColumns.widths[4] }}/>
              <col className="table-action-column" style={{ width: lineColumns.widths[5] }}/>
            </colgroup>
            <BudgetLineColumnsHead widths={lineColumns.widths} changeWidth={lineColumns.changeWidth} sort={lineSort} sortDisabled={saving || approving || detailLoading || uncertain || approvalUncertain || reviewLocked || !!pastePreview || actualsDialogOpen} onSort={sortLineView}/>
            <tbody>{displayLines.map(({ line, sourceIndex: index }, displayIndex) => <tr key={line.uiRowKey ?? index} className={line.uiRowKey && newRowKeys.has(line.uiRowKey) ? 'budget-new-row' : undefined}>
              <td><BudgetOperationInput aria-label={`Операция, строка ${displayIndex + 1}`} maxLength={2000} value={line.comment} title={line.comment} disabled={!editable} onChange={event => changeLine(index, { comment: event.target.value })}/></td>
              <td><CashFlowItemCombobox ariaLabel={`Статья ДДС, строка ${displayIndex + 1}`} required value={line.cashFlowItemId} items={cashFlowOptions.items} displayItem={cashFlowOptions.selectedItems?.find(item => item.id.toLowerCase() === line.cashFlowItemId.toLowerCase())} emptyLabel="Выберите статью" disabled={!editable || cashFlowOptions.loading || cashFlowOptions.error !== null} onChange={cashFlowItemId => changeLine(index, { cashFlowItemId })}/></td>
              <td><span aria-label={`Направление, строка ${displayIndex + 1}`} title="Направление новой строки задаётся в справочнике статей ДДС. Исторические операции сохраняют своё направление.">
                {!line.cashFlowItemId ? '—' : line.flowDirection === 'Receipt' ? 'Поступление' : 'Выплата'}
              </span></td>
              <td><CompactMonthInput ariaLabel={`Месяц, строка ${displayIndex + 1}`} required value={line.budgetMonth.slice(0, 7)} disabled={!editable} onChange={value => changeLine(index, { budgetMonth: monthStart(value) })}/></td>
              <td><BudgetAmountInput aria-label={`Сумма, строка ${displayIndex + 1}`} required inputMode="decimal" value={line.limitAmountKzt} disabled={!editable} onValueChange={value => changeLine(index, { limitAmountKzt: value })}/></td>
              <td className="budget-instance-line-actions"><div className="budget-instance-row-actions">
                <button type="button" className="line-copy-button" aria-label={`Скопировать строку ${displayIndex + 1}`} title="Скопировать строку со всеми аналитиками и суммой" disabled={!editable || actualsDialogOpen} onClick={() => copyLine(index)}>
                  <Copy size={17} aria-hidden="true"/>
                </button>
                <button type="button" className={`line-delete-button${pendingLineRemoval === index ? ' is-pending' : ''}`} aria-label={pendingLineRemoval === index
                        ? `Подтвердить удаление строки ${displayIndex + 1}` : `Удалить строку ${displayIndex + 1}`} title={pendingLineRemoval === index
                        ? 'Нажмите ещё раз для удаления строки' : 'Подготовить удаление строки'} disabled={!editable || lines.length <= 1} onClick={() => {
                        if (!editable || saveInFlight.current)
                            return;
                        if (pendingLineRemoval === index) {
                            setLines(previous => previous.filter((_, row) => row !== index));
                            setPendingLineRemoval(null);
                            changed();
                        }
                        else {
                            setPendingLineRemoval(index);
                        }
                    }}>
                <Trash2 size={17} aria-hidden="true"/>
              </button></div></td>
            </tr>)}</tbody>
          </table>
        </div>}
        {!tabular && <p role="status">Итого сумм в форме: {displayedTotal}</p>}
        {!tabular && receiptCents > 0 && <p role="status">Поступления: {formatBudgetLimitDisplay(receiptCents / 100)};{' '}
          выплаты: {formatBudgetLimitDisplay(sumCents / 100)};{' '}
          чистый поток: {formatBudgetLimitDisplay((receiptCents - sumCents) / 100)}</p>}
      </form>
      {pastePreview && <BudgetMatrixPasteDialog plan={pastePreview.plan} onApply={applyPaste} onClose={() => setPastePreview(null)}/>}
      {actualsDialogOpen && <BudgetCopyActualsDialog items={cashFlowOptions.allScopedItems ?? cashFlowOptions.items} loading={cashFlowOptions.loading} catalogueError={cashFlowOptions.error} reload={cashFlowOptions.reload} initialFrom={derivePeriod(lines)?.fromMonth ?? `${new Date().getFullYear()}-01`} initialTo={derivePeriod(lines)?.toMonth ?? `${new Date().getFullYear()}-12`} allowedYear={Number(derivePeriod(lines)?.from.slice(0, 4) ?? new Date().getFullYear())} scopeLabel="Факт по статьям данного ЦФО, без ВГО" onCopy={copyActuals} onClose={closeActualsDialog}/>}
      {confirmation !== null && <div role="alert">
        <p>{confirmation === 'leave'
                    ? 'В форме есть несохранённые изменения. Отменить их и вернуться к списку?'
                    : 'Загрузка заменит ваш несохранённый ввод текущей версией документа. Продолжить?'}</p>
        <button type="button" onClick={() => {
                    const action = confirmation;
                    setConfirmation(null);
                    if (action === 'leave')
                        leaveForm();
                    else if (current)
                        void readDetail(current.id);
                }}>{confirmation === 'leave' ? 'Отменить изменения' : 'Заменить ввод текущей версией'}</button>
        <button type="button" onClick={() => setConfirmation(null)}>Продолжить работу с вводом</button>
      </div>}
    </div>);
    return (<div className="directory-page budget-instance-workspace">
      <h1 tabIndex={-1} ref={heading}>Экземпляры бюджетов</h1>
      <button type="button" className="budget-primary-action" disabled={listActionsBlocked} onClick={onCreateInstance ?? startCreate}>Новый экземпляр бюджета</button>
      <form className="directory-filters" onSubmit={event => {
            event.preventDefault();
            setPendingDeletion(null);
            setSkip(0);
            setAppliedListFilters({ center: filterCostCenterId, version: filterVersionId, from: filterPeriodFrom, to: filterPeriodTo });
        }}>
        <label>Версия
          <select aria-label="Фильтр по версии" value={filterVersionId} disabled={listActionsBlocked || versions.loading || versions.error} onChange={event => {
            setPendingDeletion(null);
            setFilterVersionId(event.target.value);
            setFilterVersionName(event.target.selectedOptions[0]?.label ?? '');
        }}>
            <option value="">Все версии</option>
            {filterVersionId && !versions.items.some(item => sameId(item.id, filterVersionId))
            && <option value={filterVersionId}>{filterVersionName || 'Выбранная версия'}</option>}
            {versions.items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select>
        </label>
        <label>ЦФО
          <select aria-label="Фильтр по ЦФО" value={filterCostCenterId} disabled={listActionsBlocked || centers.loading} onChange={event => { setPendingDeletion(null); setFilterCostCenterId(event.target.value); }}>
            <option value="">Все доступные ЦФО</option>
            {filterCostCenterId && !centers.page?.items.some(item => sameId(item.id, filterCostCenterId))
            && <option value={filterCostCenterId}>{centerLabel({ costCenterId: filterCostCenterId })}</option>}
            {(centers.page?.items ?? []).map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select>
        </label>
        <label>Период с
          <input aria-label="Фильтр начала периода" type="month" value={filterPeriodFrom} disabled={listActionsBlocked} onChange={event => { setPendingDeletion(null); setFilterPeriodFrom(event.target.value); }}/>
        </label>
        <label>Период по
          <input aria-label="Фильтр окончания периода" type="month" value={filterPeriodTo} disabled={listActionsBlocked} onChange={event => { setPendingDeletion(null); setFilterPeriodTo(event.target.value); }}/>
        </label>
        <div className="directory-filter-actions">
        <button type="submit" disabled={listActionsBlocked}>Применить</button>
        <button type="button" disabled={listActionsBlocked} onClick={() => {
            setFilterCostCenterId('');
            setFilterVersionId('');
            setFilterVersionName('');
            setFilterPeriodFrom('');
            setFilterPeriodTo('');
            setPendingDeletion(null);
            setSkip(0);
            setAppliedListFilters({ center: '', version: '', from: '', to: '' });
        }}>Сбросить</button>
        </div>
      </form>
      {(appliedListFilters.center !== filterCostCenterId || appliedListFilters.version !== filterVersionId
            || appliedListFilters.from !== filterPeriodFrom || appliedListFilters.to !== filterPeriodTo)
            && <p className="muted">Отбор изменён. Нажмите «Применить».</p>}
      {versions.error && <p className="directory-error">Не удалось загрузить список версий. Выбранный отбор сохранён.
        <button type="button" disabled={listActionsBlocked || versions.loading} onClick={versions.retry}>Повторить загрузку версий</button>
      </p>}
      {listLoading && <p role="status">Загрузка экземпляров бюджета…</p>}
      {detailLoading && <p role="status">Открытие экземпляра бюджета…</p>}
      {error !== null && <p className="directory-error" role="alert">{error}</p>}
      {notice !== null && <p role="status">{notice}</p>}
      {deleting && <p role="status">Удаление черновика…</p>}
      {deletionUncertain && <div role="alert">
        <p>Результат удаления неизвестен. Остальные действия заблокированы до подтверждения.</p>
        <button type="button" disabled={deleting} onClick={() => {
                if (deletionAttempt.current)
                    void performDeletion(deletionAttempt.current);
            }}>Повторить удаление</button>
      </div>}
      {listError !== null && <p className="directory-error" role="alert">{listError}
        <button type="button" disabled={listActionsBlocked} onClick={() => setListRevision(value => value + 1)}>Повторить загрузку</button>
      </p>}
      {!listLoading && listError === null && items.length === 0 && <p role="status">Экземпляры бюджетов не найдены.</p>}
      {!listLoading && items.length > 0 && <div className="directory-table-wrap" tabIndex={0} aria-label="Таблица экземпляров бюджетов"><table className="directory-table budget-instance-list" data-bms-header-mode="attached" aria-label="Экземпляры бюджетов">
        <thead><tr><th scope="col">Номер</th><th scope="col">ЦФО</th><th scope="col">Автор</th><th scope="col">Период</th><th scope="col">Статус</th><th scope="col">Входит в версии</th>
          <th scope="col">Строк</th><th scope="col">Итого лимит, ₸</th><th scope="col">Действие</th></tr></thead>
        <tbody>{items.map(item => {
                const displayStatus = budgetInstanceListStatusParts(item, approvalListStatuses[item.id]);
                return <tr key={item.id} {...openableTableRow(`Экземпляр бюджета ${item.number}`, () => onOpenInstance ? onOpenInstance(item.id, item.number) : void readDetail(item.id), deleting || deletionUncertain)}>
          <td>{item.number}</td><td>{centerLabel(item)}</td><td>{item.authorName ?? '—'}</td><td>{formatBudgetPeriodDisplay(item.validFrom, item.validTo)}</td>
          <td><span className={`budget-status-label ${budgetInstanceListStatusClass(item)}`}>{displayStatus.label}</span>{displayStatus.supplement && <span className="budget-status-detail">{displayStatus.supplement}</span>}</td>
          <td className="budget-instance-versions">{item.includedVersions?.length
                        ? item.includedVersions.map((version, index) => <span key={version.id}>{version.name}{index < item.includedVersions!.length - 1 ? ',' : ''}</span>)
                        : '—'}</td>
          <td className="budget-instance-number">{item.lineCount}</td>
          <td className="budget-instance-number">{formatBudgetLimitDisplay(item.totalLimitAmountKzt)}</td>
          <td><div className="budget-instance-list-actions">
            <button type="button" className="line-copy-button" aria-label="Скопировать" title="Скопировать" disabled={listActionsBlocked} onClick={() => onCopyInstance ? onCopyInstance(item.id, item.number) : void copyInstance(item.id)}><Copy size={17} aria-hidden="true"/></button>
            {item.status === 'Draft' && item.canDelete === true && <button type="button" className={`line-delete-button${pendingDeletion === item.id ? ' is-pending' : ''}`} disabled={listActionsBlocked} aria-label={`${pendingDeletion === item.id ? 'Подтвердить удаление' : 'Удалить'} экземпляр ${item.number}`} title={pendingDeletion === item.id ? 'Нажмите ещё раз для удаления черновика' : 'Удалить черновик: два нажатия'} onClick={() => requestDeletion(item)}><Trash2 size={17} aria-hidden="true"/></button>}
          </div></td>
        </tr>;
            })}</tbody>
      </table></div>}
      <nav className="budget-instance-pagination" aria-label="Страницы экземпляров бюджетов">
        <p role="status">Показано {items.length} из {total}</p>
        <span>Страница {Math.floor(skip / listPageSize) + 1} из {Math.max(1, Math.ceil(total / listPageSize))} · По {listPageSize} строк</span>
        <button type="button" disabled={skip <= 0 || listLoading || listActionsBlocked || listError !== null} onClick={() => void reloadList(Math.max(0, skip - listPageSize))}>Назад</button>
        <button type="button" disabled={skip + listPageSize >= total || listLoading || listActionsBlocked || listError !== null} onClick={() => void reloadList(skip + listPageSize)}>Вперёд</button>
      </nav>
    </div>);
}
