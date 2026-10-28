import { useWorkspaceRefreshAction, useWorkspaceRefreshGuard, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { hasPendingExpenseRequestCommand } from '../api/expenseRequestPendingCommands';
import { BookOpen, Check, CopyPlus, Eye, History, Printer, Save } from 'lucide-react';
import { ExpenseRequestPaymentDetailsPanel } from '../components/expense-request-form/ExpenseRequestPaymentDetailsPanel';
import { ExpenseRequestGuideLink } from '../components/ExpenseRequestGuideLink';
import { TreasuryPaymentDatePanel } from '../components/expense-request-form/TreasuryPaymentDatePanel';
import { ApprovalDetailsEditor } from '../components/expense-request-form/ApprovalDetailsEditor';
import { useAccrualDepartments485 } from '../hooks/useAccrualDepartments485';
import { AccrualDepartmentsNotice485 } from '../components/expense-request-form/AccrualDepartmentsNotice485';
import '../styles/approval-details.css';
import { type FormEvent, type KeyboardEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { calculateLineAmounts, isValidPositiveMoneyInput, copyCompleteExpenseRequest, createDraft, getExpenseRequest, getExpenseRequestSubmissionPreflight, RecoveredPendingSaveError, submitExpenseRequest, updateDraft, validateAttachmentUploadIntent } from '../api';
import type { BudgetReservationSummary, CounterpartyResidencyCode, ExpenseLineDraft, ExpenseRequestDraft, PaymentBasisCode, PreliminaryCurrencyCalculation, SavedExpenseRequest, ExpenseRequestStatus, SubmissionReadiness } from '../types';
import { accrualPeriodFromPlannedPaymentDate, draftFromSaved, draftSignature, emptyLine, formatMoney, initialDraft, preliminaryCurrencyCalculationFromSaved, reconcileSavedLines, sameOpaqueIdentifier } from '../utils/expenseRequestDraft';
import { preflightBlockerLabel, readinessLabel, uniquePreflightBlockers } from '../utils/expenseRequestPreflightLabels';
import { createExpenseInitiatorPresentation, loadingExpenseRequestSystemFields, newExpenseRequestSystemFields, unavailableExpenseRequestSystemFields, type ExpenseRequestSystemFields } from '../utils/expenseRequestSystemFields';
import { expenseRequestCopyErrorMessage, expenseRequestLoadErrorMessage, expenseRequestPreflightErrorMessage, expenseRequestSaveErrorMessage, recoveredPendingSaveWarning } from '../utils/uiErrorMessages';
import { useExpenseRequestReferenceData } from '../hooks/useExpenseRequestReferenceData';
import { ExpenseRequestPrintDocument, type ExpenseRequestPrintData } from '../components/expense-request-form/ExpenseRequestPrintDocument';
import { useExpenseRequestWorkflowCommands } from '../hooks/useExpenseRequestWorkflowCommands';
import { useExpenseRequestStatusLabel } from '../hooks/useExpenseRequestStatusLabel';
import { ExpenseRequestAttachmentsPanel } from '../components/expense-request-form/ExpenseRequestAttachmentsPanel';
import { ExpenseRequestCounterpartyPanel } from '../components/expense-request-form/ExpenseRequestCounterpartyPanel';
import { ExpenseRequestHeaderPanel } from '../components/expense-request-form/ExpenseRequestHeaderPanel';
import { ExpenseRequestLinesPanel } from '../components/expense-request-form/ExpenseRequestLinesPanel';
import { ExpenseRequestApprovalRoute } from '../components/expense-request-form/ExpenseRequestApprovalRoute';
import { ExpenseRequestWorkflowCommandBar } from '../components/expense-request-form/ExpenseRequestWorkflowCommandBar';
import { ApprovalComments360 } from '../components/expense-request-form/ApprovalComments360';
import { ExpenseRequestHistoryPanel } from './ExpenseRequestHistoryPanel';
import { ExpenseRequestPaymentPanel } from '../components/expense-request-form/ExpenseRequestPaymentPanel';
import { ExpenseRequestClosingPanel } from '../components/expense-request-form/ExpenseRequestClosingPanel';
import type { ExpenseRequestClosingState } from '../api';
import { expenseRequestStatusLabel, expenseRequestStatusTone } from '../expenseRequestStatus';
import { useExpenseRequestBudgetRefresh } from '../hooks/useExpenseRequestBudgetRefresh';
import { expenseLineBudgetControl, expenseLineBudgetBeforeRequest } from '../utils/expenseLineBudgetControl';
import { useExpenseDraftBudgetPreview } from '../hooks/useExpenseDraftBudgetPreview';
import { useExpenseBudgetLimits } from '../hooks/useExpenseBudgetLimits';
import { expenseBudgetDetail440 } from '../utils/expenseBudgetDetail440';
import type { RequestBudgetContext442 } from '../api/requestBudgetExplanation442';
import { ExpenseBudgetExplanation442 } from '../components/expense-request-form/ExpenseBudgetExplanation442';
import { useExpenseRequestNumberLease } from '../hooks/useExpenseRequestNumberLease';
import { recoverPendingCreateDraft, recoverPendingExpenseRequestSubmit } from '../api/expenseRequestCommands';
import { expenseDraftInputIssue } from '../utils/expenseDraftInputIssue';
import { useExpenseDraftDefaults } from '../hooks/useExpenseDraftDefaults';
import { useExpenseAccrualSettings } from '../hooks/useExpenseAccrualSettings';
import { useExpenseRequestAttachmentReadiness } from '../hooks/useExpenseRequestAttachmentReadiness';
import { compactRequestDate } from '../components/expense-request-form/CompactRequestDate';
import { useExpenseCurrencyRefresh } from '../hooks/useExpenseCurrencyRefresh';
import { useExpenseSubmissionPreflight } from '../hooks/useExpenseSubmissionPreflight';
import { expenseReadinessTarget, type ExpenseReadinessTarget } from '../utils/expenseReadinessTarget';
const formTabs = [
    { id: 'main', label: 'Основное' },
    { id: 'counterparty', label: 'Контрагент и основание' },
    { id: 'lines', label: 'Расходы' },
    { id: 'explanation', label: 'Расшифровка бюджета' },
    { id: 'budget', label: 'Оплата' },
    { id: 'closing', label: 'Закрытие' },
] as const;
type FormTabId = (typeof formTabs)[number]['id'];
const copyCreatedWarning = 'Копия создана из сохранённых данных. Дата оплаты — следующий рабочий день. Вложения при необходимости добавьте заново.';
function confirmScenarioDiscard(values: readonly unknown[]): boolean {
    return !values.some(value => value === true || (typeof value === 'string' && value.trim() !== ''))
        || window.confirm('При переключении будут очищены заполненные реквизиты предыдущего варианта. Продолжить?');
}
type Props = {
    requestId?: string | null;
    readOnly?: boolean;
    canEditExistingFallback?: boolean;
    canCreateExpenseRequest?: boolean;
    canCopyExpenseRequest?: boolean;
    currentActorExternalId?: string | null;
    currentActorDisplayName?: string | null;
    onNavigationLockChange?: (locked: boolean) => void;
    onSaved?: (request: SavedExpenseRequest) => void;
    onCopied?: (request: SavedExpenseRequest) => void;
    onDocumentPresentationChange?: (value: {
        number: string | null;
        dirty: boolean;
    }) => void;
    showCopyCreatedWarning?: boolean;
};
export function ExpenseRequestFormPage({ requestId = null, readOnly = false, canEditExistingFallback = !readOnly, canCreateExpenseRequest = false, canCopyExpenseRequest = false, currentActorExternalId = null, currentActorDisplayName = null, onNavigationLockChange, onSaved, onCopied, onDocumentPresentationChange, showCopyCreatedWarning = false, }: Props) {
    const formRoot = useRef<HTMLDivElement>(null);
    const focusTarget = useRef<ExpenseReadinessTarget | null>(null);
    const [focusRevision, setFocusRevision] = useState(0);
    const initiatorPresentation = useRef(createExpenseInitiatorPresentation());
    const expenseRequestSystemFieldsFromSaved = initiatorPresentation.current.fromSaved;
    const refreshGeneration = useRef(0);
    const refreshMounted = useRef(false);
    useEffect(() => { refreshMounted.current = true; return () => { refreshMounted.current = false; refreshGeneration.current += 1; }; }, []);
    useEffect(() => { refreshGeneration.current += 1; }, [requestId]);
    const [draft, setDraft] = useState<ExpenseRequestDraft>(initialDraft);
    const [stagedAttachmentCount, setStagedAttachmentCount] = useState(0);
    const initialDraftValue = useRef(draft);
    const newDraftTouched = useRef(false);
    const defaultCfoUserChanged = useRef(false);
    const notifyNavigationLock = useRef(onNavigationLockChange);
    notifyNavigationLock.current = onNavigationLockChange;
    const [systemFields, setSystemFields] = useState<ExpenseRequestSystemFields>(() => (requestId === null
        ? newExpenseRequestSystemFields(currentActorExternalId, currentActorDisplayName)
        : loadingExpenseRequestSystemFields()));
    const [refreshRequired, setRefreshRequired] = useState(false);
    const [isLoading, setIsLoading] = useState(requestId !== null);
    const [printData, setPrintData] = useState<ExpenseRequestPrintData | null>(null);
    // W560-R4. Which request this page has actually got. A page that was asked
    // for an identifier and has not received that exact one is not a new draft,
    // however its other state happens to look.
    const [loadedRequestId, setLoadedRequestId] = useState<string | null>(null);
    const [canEditExisting, setCanEditExisting] = useState<boolean | null>(requestId === null ? true : null);
    const [sourceInitiatorExternalId, setSourceInitiatorExternalId] = useState<string | null>(null);
    const [isSaving, setIsSaving] = useState(false);
    const [attachmentUploadSessionId, setAttachmentUploadSessionId] = useState(() => crypto.randomUUID());
    const [attachmentWorkPending, setAttachmentWorkPending] = useState(false);
    useEffect(() => { setAttachmentUploadSessionId(crypto.randomUUID()); }, [requestId, currentActorExternalId]);
    const [isCopying, setIsCopying] = useState(false);
    const [isSubmitting, setIsSubmitting] = useState(false);
    const submissionInFlight = useRef(false);
    const [message, setMessage] = useState<{
        tone: 'success' | 'warning' | 'error';
        text: string;
    } | null>(null);
    const [savedId, setSavedId] = useState<string | null>(null);
    const [requestStatus, setRequestStatus] = useState<ExpenseRequestStatus | null>(requestId === null ? 'DRAFT' : null);
    const [expectedVersion, setExpectedVersion] = useState<string | null>(null);
    const displayStatusLabel = useExpenseRequestStatusLabel(requestStatus, loadedRequestId, expectedVersion, currentActorExternalId, systemFields.statusLabel);
    const [savedDraftSignature, setSavedDraftSignature] = useState<string | null>(null);
    const [rawBudgetReservation, setBudgetReservation] = useState<BudgetReservationSummary | null>(null);
    const [preliminaryCurrencyCalculation, setPreliminaryCurrencyCalculation] = useState<PreliminaryCurrencyCalculation | null>(null);
    const [storedSubmissionReadiness, setSubmissionReadiness] = useState<SubmissionReadiness | null>(null);
    const [attachmentReadinessRevision, setAttachmentReadinessRevision] = useState(0);
    const [currencyFilesRevision, setCurrencyFilesRevision] = useState(0);
    const { readiness: submissionReadiness, failed: attachmentReadinessFailed, loading: attachmentReadinessLoading } = useExpenseRequestAttachmentReadiness(savedId, expectedVersion, storedSubmissionReadiness, attachmentReadinessRevision);
    const [preflightRevision, setPreflightRevision] = useState(0);
    const invalidatePreflight = useCallback(() => setPreflightRevision(value => value + 1), []);
    const [cancelButtonTarget, setCancelButtonTarget] = useState<HTMLSpanElement | null>(null);
    const savedIdentity = useRef<{
        requestId: string | null;
        requestVersion: string | null;
    }>({
        requestId: null,
        requestVersion: null,
    });
    savedIdentity.current = { requestId: savedId, requestVersion: expectedVersion };
    const [historyInvalidationToken, setHistoryInvalidationToken] = useState(0);
    const accrualDepartments = useAccrualDepartments485(JSON.stringify([requestId ?? 'new', currentActorExternalId]));
    const accrualCostCenters = accrualDepartments.items;
    const [activeTab, setActiveTab] = useState<FormTabId | 'history'>('main');
    const [budgetDetail440, setBudgetDetail440] = useState<{
        scope: string;
        context: RequestBudgetContext442;
    } | null>(null);
    const previousFormTab = useRef<FormTabId>('main');
    const formTabRefs = useRef<Array<HTMLButtonElement | null>>([]);
    const { organizations, costCenters, cashFlowItems, cashFlowItemLabels, counterparties, contracts, currencies, paymentBases, counterpartyResidencies, printReferenceDataLoading, referenceError, } = useExpenseRequestReferenceData({
        currencyCode: draft.currencyCode,
        organizationId: draft.organizationId,
        counterpartyId: draft.counterpartyId,
        plannedPaymentDate: draft.plannedPaymentDate,
        paymentBasisCode: draft.paymentBasisCode,
        isNewCounterparty: draft.isNewCounterparty,
        isNewContract: draft.isNewContract,
        costCenterId: draft.defaultCostCenterId,
        cashFlowItemIds: draft.lines
            .map((line) => line.cashFlowItemId)
            .filter((id) => id !== ''),
    });
    const accrualSettings = useExpenseAccrualSettings(draft.lines.map(line => line.cashFlowItemId), [...cashFlowItemLabels, ...cashFlowItems]);
    const accrualDepartmentFlags = new Map<string, boolean>();
    for (const item of [...accrualSettings.items, ...cashFlowItemLabels, ...cashFlowItems]) {
        const id = item.id.toLowerCase();
        if (!accrualDepartmentFlags.has(id))
            accrualDepartmentFlags.set(id, item.formsManagerialAccrual === true);
    }
    const hasAccrualDepartments = draft.lines.some(line => accrualDepartmentFlags.get(line.cashFlowItemId.toLowerCase()) === true);
    const isWorkflowEditable = requestStatus === null
        || requestStatus === 'DRAFT'
        || requestStatus === 'RETURNED';
    const isExistingEditLocked = requestId !== null && canEditExisting !== true;
    const isDocumentReadOnly = readOnly || refreshRequired || isExistingEditLocked || !isWorkflowEditable;
    // W560-R1. A card that is still being fetched is not editable. The load ends
    // by replacing the whole draft with what the server sent, so anything typed
    // before it arrives would be discarded without a trace - and the person would
    // have no way to tell which of their words survived.
    //
    // W560-R4. The same holds once the fetch has failed. Clearing the identity of
    // a card that would not open stops its data appearing under someone else's
    // number, but it must not turn the page into a blank new draft: this page was
    // asked for a particular request, and until it has that request nothing here
    // may be edited or saved. A page with no requestId at all is a genuine new
    // draft and stays open.
    // The public boundary accepts an identifier in either case, so the comparison
    // that decides whether this page has its request must too: a card loaded under
    // an uppercase identifier is loaded.
    const isAwaitingRequest = requestId !== null
        && !sameOpaqueIdentifier(loadedRequestId, requestId);
    // A stable presentation decision, not the temporary saving/upload lock.
    // A busy author's editable form must never be replaced under their cursor.
    const isReadView = savedId !== null && !isLoading && !isAwaitingRequest
        && (readOnly || isExistingEditLocked || !isWorkflowEditable);
    const showPaymentTabs = !isLoading && !isAwaitingRequest
        && (requestStatus === 'TO_PAY' || requestStatus === 'PARTIALLY_PAID'
            || requestStatus === 'PAID' || requestStatus === 'CLOSED');
    const isPayrollAdvance = draft.paymentOperation?.kind === 'PayrollAdvance';
    const visibleFormTabs = formTabs.filter(tab => (!isPayrollAdvance || tab.id !== 'lines')
        && (tab.id !== 'explanation' || budgetDetail440 !== null && !isPayrollAdvance)
        && (showPaymentTabs || (tab.id !== 'budget' && tab.id !== 'closing')));
    useEffect(() => {
        if (!showPaymentTabs && (activeTab === 'budget' || activeTab === 'closing'))
            setActiveTab('main');
    }, [showPaymentTabs, activeTab]);
    useEffect(() => {
        if (isPayrollAdvance && activeTab === 'lines')
            setActiveTab('counterparty');
    }, [isPayrollAdvance, activeTab]);
    // W608. An unavailable existing card is not a new draft merely because
    // its loaded identity was cleared after the failed read.
    const isNewRequestContext = requestId === null && savedId === null;
    const draftDefaults = useExpenseDraftDefaults(draft.currencyCode, currentActorExternalId);
    const defaultActorCfo = draftDefaults.data?.costCenterId ?? '';
    const titleDate = isNewRequestContext ? compactRequestDate(draftDefaults.data?.businessDate ?? '')
        : /^\d{2}\.\d{2}\.\d{4}$/u.test(systemFields.createdDateLabel)
            ? systemFields.createdDateLabel.replace(/\.(\d{2})(\d{2})$/u, '.$2') : '';
    const numberReservation = useExpenseRequestNumberLease(draft.defaultCostCenterId, isNewRequestContext && !readOnly && canCreateExpenseRequest, currentActorExternalId);
    const canCopyExisting = currentActorExternalId !== null
        && savedId !== null && !isAwaitingRequest;
    // Copy creates a new Draft; the source's status/edit lock does not authorize
    // or forbid it. Identity and current creation capability are both required.
    useEffect(() => {
        if (requestId === null && savedId === null) {
            setSystemFields(newExpenseRequestSystemFields(currentActorExternalId, currentActorDisplayName));
        }
    }, [currentActorDisplayName, currentActorExternalId, requestId, savedId]);
    useEffect(() => {
        if (!requestId)
            return;
        initiatorPresentation.current.clear();
        let cancelled = false;
        setIsLoading(true);
        setMessage(null);
        setSavedDraftSignature(null);
        invalidatePreflight();
        setSystemFields(loadingExpenseRequestSystemFields());
        setLoadedRequestId(null);
        void (async () => {
            try {
                const saved = await getExpenseRequest(requestId);
                if (cancelled)
                    return;
                const loadedDraft = draftFromSaved(saved);
                setDraft(loadedDraft);
                setSavedId(saved.id);
                setSourceInitiatorExternalId(saved.initiatorExternalId ?? null);
                setRequestStatus(saved.status);
                setLoadedRequestId(saved.id);
                setCanEditExisting(saved.editAuthority?.canEdit ?? canEditExistingFallback);
                setExpectedVersion(saved.concurrencyStamp);
                setSavedDraftSignature(draftSignature(loadedDraft));
                setBudgetReservation(saved.budgetReservation ?? null);
                setPreliminaryCurrencyCalculation(preliminaryCurrencyCalculationFromSaved(saved));
                setSubmissionReadiness(saved.submissionReadiness ?? null);
                setSystemFields(expenseRequestSystemFieldsFromSaved(saved, currentActorExternalId, currentActorDisplayName));
                if (showCopyCreatedWarning) {
                    setMessage({ tone: 'warning', text: copyCreatedWarning });
                }
            }
            catch (loadError) {
                if (!cancelled) {
                    // W560-R1. Nothing of the request that failed to open is known, so
                    // nothing is shown. Leaving the previous card's values on screen
                    // under a new identity would invite someone to edit and save one
                    // request believing it was another.
                    setDraft(initialDraft());
                    setSavedId(null);
                    setRequestStatus(null);
                    setCanEditExisting(false);
                    setSourceInitiatorExternalId(null);
                    setExpectedVersion(null);
                    setBudgetReservation(null);
                    setPreliminaryCurrencyCalculation(null);
                    setSubmissionReadiness(null);
                    initiatorPresentation.current.clear();
                    setSystemFields(unavailableExpenseRequestSystemFields());
                    setMessage({
                        tone: 'error',
                        text: expenseRequestLoadErrorMessage(loadError),
                    });
                }
            }
            finally {
                if (!cancelled)
                    setIsLoading(false);
            }
        })();
        return () => {
            cancelled = true;
        };
    }, [canEditExistingFallback, currentActorDisplayName, currentActorExternalId, requestId, showCopyCreatedWarning]);
    const lineAmounts = useMemo(() => draft.lines.map((line) => calculateLineAmounts(isValidPositiveMoneyInput(line.amount) ? line.amount : 0, line.vatRateCode)), [draft.lines]);
    const total = lineAmounts.reduce((sum, amounts) => sum + amounts.amountKzt, 0);
    const totalVat = lineAmounts.reduce((sum, amounts) => sum + amounts.vatAmount, 0);
    const totalExcludingVat = lineAmounts.reduce((sum, amounts) => sum + amounts.amountExcludingVat, 0);
    const currentDraftSignature = useMemo(() => draftSignature(draft), [draft]);
    // Invalidate a multi-step submission synchronously when the card, actor or
    // authority changes. A late response may never dispatch the next command.
    const submissionContextKey = JSON.stringify([requestId, currentActorExternalId,
        readOnly, canCreateExpenseRequest, isDocumentReadOnly]);
    const submissionContext = useRef({ key: submissionContextKey, generation: 0, signature: currentDraftSignature });
    const submissionReadController = useRef<AbortController | null>(null);
    useEffect(() => () => submissionReadController.current?.abort(), [submissionContextKey]);
    submissionContext.current = { key: submissionContextKey,
        generation: submissionContext.current.generation + (submissionContext.current.key === submissionContextKey ? 0 : 1),
        signature: currentDraftSignature };
    const pristineNewDraftSignature = useMemo(() => {
        const initial = initialDraftValue.current;
        const singletonOrganization = organizations.length === 1 ? organizations[0] : null;
        const singletonCostCenter = costCenters.find(c => sameOpaqueIdentifier(c.id, defaultActorCfo))
            ?? (costCenters.length === 1 ? costCenters[0] : null);
        const organizationId = singletonOrganization !== null
            ? singletonOrganization.id.trim()
            : initial.organizationId;
        const defaultCostCenterId = singletonCostCenter !== null
            ? singletonCostCenter.id.trim()
            : initial.defaultCostCenterId;
        return draftSignature({
            ...initial,
            organizationId,
            defaultCostCenterId,
            lines: initial.lines.map((line) => line.costCenterId.trim() === ''
                ? { ...line, costCenterId: defaultCostCenterId }
                : line),
        });
    }, [costCenters, organizations, defaultActorCfo]);
    const isSavedAndClean = savedId !== null
        && savedDraftSignature !== null
        && currentDraftSignature === savedDraftSignature;
    const { reservation: budgetReservation, positions: capturedBudgetPositions } = useExpenseRequestBudgetRefresh({
        requestId: savedId, requestVersion: expectedVersion, reservation: rawBudgetReservation,
        enabled: !isPayrollAdvance && !isLoading && !isAwaitingRequest,
    });
    const canCheckSavedVersion = isSavedAndClean && expectedVersion !== null;
    const hasUnsavedChanges = savedId !== null && !isSavedAndClean;
    const savedLineBudgetControls = useMemo(() => draft.lines.map(line => expenseLineBudgetBeforeRequest(expenseLineBudgetControl(line, draft.plannedPaymentDate, capturedBudgetPositions, isSavedAndClean), line, draft.plannedPaymentDate, ['PAID', 'PARTIALLY_PAID', 'CLOSED'].includes(requestStatus ?? '') ? null : budgetReservation)), [draft.lines, draft.plannedPaymentDate, capturedBudgetPositions, isSavedAndClean, budgetReservation, requestStatus]);
    const previewLineBudgetControls = useExpenseDraftBudgetPreview(draft, savedId, expectedVersion, !isPayrollAdvance && !isDocumentReadOnly && !isLoading && !isAwaitingRequest);
    const lineBudgetControls = isDocumentReadOnly ? savedLineBudgetControls : previewLineBudgetControls;
    const lineBudgetLimits = useExpenseBudgetLimits(draft, !isPayrollAdvance && !isLoading && !isAwaitingRequest);
    const budgetDetailScope440 = JSON.stringify([requestId, savedId, expectedVersion, draft.defaultCostCenterId,
        draft.plannedPaymentDate, currentDraftSignature, isPayrollAdvance,
        isLoading, isAwaitingRequest, isSaving, isSubmitting, refreshRequired]);
    const budgetDetailSource440 = useRef<HTMLAnchorElement | null>(null);
    const [budgetDetailRestore478, setBudgetDetailRestore478] = useState<{
        requestId: string;
        articleId: string;
    } | null>(null);
    useEffect(() => { setBudgetDetail440(null); setActiveTab(tab => tab === 'explanation' ? 'lines' : tab); }, [budgetDetailScope440]);
    const canOpenBudget440 = savedId !== null && expectedVersion !== null && isSavedAndClean && !isPayrollAdvance && !isLoading && !isAwaitingRequest && !isSaving && !isSubmitting && !refreshRequired;
    const openBudget440 = (index: number, source: HTMLAnchorElement | null) => {
        if (!canOpenBudget440)
            return;
        const context = expenseBudgetDetail440(draft, index, lineBudgetLimits, costCenters, [...accrualSettings.items, ...cashFlowItemLabels, ...cashFlowItems]);
        if (context && savedId && expectedVersion) {
            budgetDetailSource440.current = source;
            setBudgetDetail440({ scope: budgetDetailScope440,
                context: { ...context, requestId: savedId, version: expectedVersion, expectedAfter: lineBudgetControls[index]?.availableAmountKzt ?? null } });
            setActiveTab('explanation');
        }
    };
    useEffect(() => {
        if (!budgetDetailRestore478 || !canOpenBudget440)
            return;
        const restore = budgetDetailRestore478;
        if (!sameOpaqueIdentifier(restore.requestId, savedId)) {
            setBudgetDetailRestore478(null);
            return;
        }
        const index = draft.lines.findIndex(line => sameOpaqueIdentifier(line.cashFlowItemId, restore.articleId));
        if (index < 0) {
            setBudgetDetailRestore478(null);
            return;
        }
        // Wait for a fresh, authorized limit. Never reopen the old article from a stale context.
        if (lineBudgetLimits[index]?.state === 'loading')
            return;
        setBudgetDetailRestore478(null);
        openBudget440(index, budgetDetailSource440.current);
    }, [budgetDetailRestore478, budgetDetailScope440, canOpenBudget440, lineBudgetLimits]);
    const closeBudget440 = () => {
        setBudgetDetail440(null);
        setActiveTab('lines');
        if (budgetDetailSource440.current?.isConnected)
            budgetDetailSource440.current.focus();
    };
    const hasNewDraftChanges = savedId === null
        && requestId === null
        && (stagedAttachmentCount > 0 || attachmentWorkPending
            || newDraftTouched.current && currentDraftSignature !== pristineNewDraftSignature);
    const presentationCallback = useRef(onDocumentPresentationChange);
    presentationCallback.current = onDocumentPresentationChange;
    const documentNumber = systemFields.numberLabel ?? numberReservation.lease?.number ?? null;
    useEffect(() => {
        presentationCallback.current?.({ number: documentNumber, dirty: hasUnsavedChanges || hasNewDraftChanges });
    }, [documentNumber, hasUnsavedChanges, hasNewDraftChanges]);
    const applyWorkflowSaved = useCallback((saved: SavedExpenseRequest) => {
        const savedDraft = draftFromSaved(saved);
        setDraft(savedDraft);
        setSavedId(saved.id);
        setSourceInitiatorExternalId(saved.initiatorExternalId ?? null);
        setRequestStatus(saved.status);
        setExpectedVersion(saved.concurrencyStamp);
        setSavedDraftSignature(draftSignature(savedDraft));
        setBudgetReservation(saved.budgetReservation ?? null);
        setPreliminaryCurrencyCalculation(preliminaryCurrencyCalculationFromSaved(saved));
        setSubmissionReadiness(saved.submissionReadiness ?? null);
        invalidatePreflight();
        setSystemFields(expenseRequestSystemFieldsFromSaved(saved, currentActorExternalId, currentActorDisplayName));
        setHistoryInvalidationToken((value) => value + 1);
        onSaved?.(saved);
        setMessage(saved.status === 'IN_APPROVAL' && (requestStatus === 'APPROVED' || requestStatus === 'TO_PAY')
            ? { tone: 'warning', text: 'Бюджет месяца оплаты проверен повторно. Из-за нового превышения заявка направлена на повторное утверждение; в оплату она не передана.' }
            : { tone: 'success', text: 'Команда заявки выполнена.' });
    }, [currentActorDisplayName, currentActorExternalId, onSaved, requestStatus]);
    const workflowCommands = useExpenseRequestWorkflowCommands({
        requestId: savedId,
        requestVersion: expectedVersion,
        onSaved: applyWorkflowSaved,
    });
    const [treasuryDateBusy, setTreasuryDateBusy] = useState(false);
    const [approvalDetailsBusy, setApprovalDetailsBusy] = useState(false);
    const currencyRefresh = useExpenseCurrencyRefresh({
        id: savedId, version: expectedVersion, actor: currentActorExternalId, currencyCode: draft.currencyCode,
        calculation: preliminaryCurrencyCalculation, rate: draftDefaults.data?.currencyRate ?? null,
        enabled: isSavedAndClean && !isDocumentReadOnly && !isLoading && !isAwaitingRequest && !isSaving
            && !isSubmitting && !isCopying && !attachmentWorkPending
            && workflowCommands.executingCode === null && !hasPendingExpenseRequestCommand(savedId),
        onRefreshed: saved => {
            // Only server-derived fields change. Never replace the editable draft or
            // its clean signature; a user may have typed while this request ran.
            invalidatePreflight();
            setExpectedVersion(saved.concurrencyStamp);
            setPreliminaryCurrencyCalculation(preliminaryCurrencyCalculationFromSaved(saved));
            setBudgetReservation(saved.budgetReservation ?? null);
            setSubmissionReadiness(saved.submissionReadiness ?? null);
            setCurrencyFilesRevision(value => value + 1);
            setHistoryInvalidationToken(value => value + 1);
        },
    });
    const { result: submissionPreflight, checking: isCheckingPreflight, error: preflightError } = useExpenseSubmissionPreflight({
        id: savedId, version: expectedVersion, actor: currentActorExternalId,
        revision: preflightRevision + attachmentReadinessRevision,
        enabled: canCheckSavedVersion && !isDocumentReadOnly && !isLoading && !isAwaitingRequest
            && !isSaving && !isSubmitting && !isCopying && !attachmentWorkPending && !currencyRefresh.busy
            && workflowCommands.executingCode === null && !hasPendingExpenseRequestCommand(savedId),
    });
    const navigateToIssue = (code: string, lineNumber?: number | null) => {
        const target = expenseReadinessTarget(code, lineNumber);
        if (!target)
            return;
        focusTarget.current = target;
        setActiveTab(target.tab);
        setFocusRevision(value => value + 1);
    };
    useEffect(() => {
        const target = focusTarget.current, root = formRoot.current;
        if (!target || !root || isSaving || isSubmitting || isLoading || activeTab !== target.tab)
            return;
        const panel = target.attachment ? root.querySelector<HTMLElement>('#expense-panel-attachments')
            : root.querySelector<HTMLElement>(`#expense-panel-${target.tab}`);
        if (!panel)
            return;
        const controls = [...panel.querySelectorAll<HTMLElement>('input,select,textarea,button')];
        let control = controls.find(element => element.getAttribute('aria-label') === target.label);
        if (!control) {
            const label = [...panel.querySelectorAll('label')].find(element => element.querySelector('span')?.textContent?.trim() === target.label);
            control = label?.querySelector<HTMLElement>('input,select,textarea,button') ?? controls.find(element => element.textContent?.trim() === target.label);
        }
        if (control?.matches(':disabled'))
            return;
        focusTarget.current = null;
        const element = control ?? panel;
        if (!control)
            element.tabIndex = -1;
        element.focus({ preventScroll: true });
        element.scrollIntoView?.({ block: 'center', inline: 'nearest' });
    }, [activeTab, focusRevision, isSaving, isSubmitting, isLoading]);
    useWorkspaceRefreshGuard(currencyRefresh.busy ? 'Дождитесь автоматического валютного пересчёта.' : null);
    useWorkspaceRefreshGuard(() => hasPendingExpenseRequestCommand(savedId ?? requestId)
        ? 'Результат отправленной команды заявки не подтверждён. Повторите исходную попытку.'
        : hasUnsavedChanges || hasNewDraftChanges || isSaving || isSubmitting || isCopying
            || workflowCommands.executingCode !== null
            ? 'Сначала сохраните или отмените изменения заявки и завершите текущую операцию.' : null);
    useWorkspaceRefreshBusy(isLoading || workflowCommands.loading || currencyRefresh.busy);
    const refreshSavedRequest478 = async (restore?: {
        requestId: string;
        articleId: string;
    }) => {
        const id = savedIdentity.current.requestId ?? requestId;
        if (id === null)
            return; // A pristine new draft is not a server document.
        const generation = ++refreshGeneration.current;
        setIsLoading(true);
        setMessage(null);
        try {
            const saved = await getExpenseRequest(id);
            if (!refreshMounted.current || generation !== refreshGeneration.current)
                return;
            if (!sameOpaqueIdentifier(saved.id, id))
                throw new Error('Некорректный ответ заявки.');
            const loadedDraft = draftFromSaved(saved);
            setDraft(loadedDraft);
            setSavedId(saved.id);
            setLoadedRequestId(saved.id);
            setRefreshRequired(false);
            setSourceInitiatorExternalId(saved.initiatorExternalId ?? null);
            setRequestStatus(saved.status);
            setCanEditExisting(saved.editAuthority?.canEdit ?? canEditExistingFallback);
            setExpectedVersion(saved.concurrencyStamp);
            setSavedDraftSignature(draftSignature(loadedDraft));
            setBudgetReservation(saved.budgetReservation ?? null);
            setPreliminaryCurrencyCalculation(preliminaryCurrencyCalculationFromSaved(saved));
            setSubmissionReadiness(saved.submissionReadiness ?? null);
            invalidatePreflight();
            setSystemFields(expenseRequestSystemFieldsFromSaved(saved, currentActorExternalId, currentActorDisplayName));
            if (restore && sameOpaqueIdentifier(restore.requestId, saved.id))
                setBudgetDetailRestore478(restore);
        }
        catch (error) {
            if (refreshMounted.current && generation === refreshGeneration.current) {
                setRefreshRequired(true);
                setMessage({ tone: 'error', text: expenseRequestLoadErrorMessage(error) });
            }
            throw error;
        }
        finally {
            if (refreshMounted.current && generation === refreshGeneration.current)
                setIsLoading(false);
        }
    };
    useWorkspaceRefreshAction(() => refreshSavedRequest478());
    const refreshBudgetRequest478 = async () => {
        // Same guard as workspace refresh, including pending writes and local attachment work.
        if (!canOpenBudget440 || !budgetDetail440 || hasPendingExpenseRequestCommand(savedId ?? requestId)
            || hasUnsavedChanges || hasNewDraftChanges || isCopying || attachmentWorkPending || currencyRefresh.busy
            || workflowCommands.executingCode !== null || treasuryDateBusy || approvalDetailsBusy)
            return;
        await refreshSavedRequest478({ requestId: budgetDetail440.context.requestId, articleId: budgetDetail440.context.key.cashFlowItemId });
    };
    const isFormMutationLocked = isDocumentReadOnly || isSubmitting || isLoading || isAwaitingRequest
        || isSaving || isCopying || attachmentWorkPending || currencyRefresh.busy || workflowCommands.executingCode !== null;
    const isCopyMutationLocked = readOnly || refreshRequired || !canCreateExpenseRequest || !canCopyExpenseRequest || !canCopyExisting
        || !savedId || isSubmitting || isLoading || isAwaitingRequest || isSaving || isCopying
        || currencyRefresh.busy || workflowCommands.executingCode !== null || treasuryDateBusy || approvalDetailsBusy;
    useEffect(() => {
        notifyNavigationLock.current?.(hasUnsavedChanges
            || hasNewDraftChanges
            || isSaving
            || isSubmitting
            || isCopying
            || workflowCommands.executingCode !== null || treasuryDateBusy || approvalDetailsBusy);
    }, [
        hasUnsavedChanges,
        hasNewDraftChanges,
        isCopying,
        isSaving,
        isSubmitting,
        workflowCommands.executingCode,
        treasuryDateBusy,
        approvalDetailsBusy,
    ]);
    useEffect(() => () => {
        notifyNavigationLock.current?.(false);
    }, []);
    const applyClosingState = useCallback((closingState: ExpenseRequestClosingState, recovered: boolean) => {
        const currentIdentity = savedIdentity.current;
        if (currentIdentity.requestId === null
            || !sameOpaqueIdentifier(currentIdentity.requestId, closingState.expenseRequestId)) {
            return;
        }
        savedIdentity.current = {
            requestId: closingState.expenseRequestId,
            requestVersion: closingState.concurrencyStamp,
        };
        setRequestStatus(closingState.status);
        setExpectedVersion(closingState.concurrencyStamp);
        // The closing projection has no monetary snapshot. Do not attach the old one
        // to its new version while the complete card is being refreshed.
        setBudgetReservation(null);
        setSystemFields((current) => ({
            ...current,
            statusLabel: expenseRequestStatusLabel(closingState.status),
        }));
        setHistoryInvalidationToken((value) => value + 1);
        setMessage({
            tone: recovered ? 'warning' : 'success',
            text: recovered
                ? 'Предыдущая попытка закрытия подтверждена сервером. Данные обновлены.'
                : 'Состояние закрытия заявки обновлено.',
        });
        // The closing projection deliberately contains no draft/business fields.
        // Refresh the complete public request when available so the surrounding
        // list and every existing read model receive the new status/version too.
        void getExpenseRequest(closingState.expenseRequestId)
            .then((saved) => {
            const latest = savedIdentity.current;
            if (latest.requestId === null || latest.requestVersion === null
                || !sameOpaqueIdentifier(latest.requestId, closingState.expenseRequestId)
                || !sameOpaqueIdentifier(latest.requestVersion, closingState.concurrencyStamp)
                || !sameOpaqueIdentifier(saved.id, closingState.expenseRequestId)
                || !sameOpaqueIdentifier(saved.concurrencyStamp, closingState.concurrencyStamp)) {
                return;
            }
            applyWorkflowSaved(saved);
        })
            .catch(() => {
            // The command result is authoritative and already applied above. A
            // failed secondary read must not turn success into an apparent failure.
        });
    }, [applyWorkflowSaved]);
    const focusFormTab = (index: number) => {
        const total = visibleFormTabs.length;
        const target = ((index % total) + total) % total;
        formTabRefs.current[target]?.focus();
    };
    const onFormTabsKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        const current = formTabRefs.current.slice(0, visibleFormTabs.length).findIndex((tab) => tab === document.activeElement);
        if (current < 0)
            return;
        if (event.key === 'ArrowRight') {
            event.preventDefault();
            focusFormTab(current + 1);
        }
        else if (event.key === 'ArrowLeft') {
            event.preventDefault();
            focusFormTab(current - 1);
        }
        else if (event.key === 'Home') {
            event.preventDefault();
            focusFormTab(0);
        }
        else if (event.key === 'End') {
            event.preventDefault();
            focusFormTab(visibleFormTabs.length - 1);
        }
        else if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            setActiveTab(visibleFormTabs[current].id);
        }
    };
    const patchDraft = (patch: Partial<ExpenseRequestDraft>) => {
        setDraft((current) => {
            const next = { ...current, ...patch };
            if (next.paymentOperation?.kind === 'PayrollAdvance')
                next.lines = [{ ...next.lines[0], costCenterId: next.defaultCostCenterId,
                        amount: next.paymentOperation.advanceAmount == null ? '' : String(next.paymentOperation.advanceAmount),
                        accrualPeriod: next.paymentOperation.period ? `${next.paymentOperation.period}-01` : '', vatRateCode: 'WithoutVat' }];
            return next;
        });
    };
    const updateLine = (index: number, patch: Partial<ExpenseLineDraft>) => {
        setDraft((current) => ({
            ...current,
            lines: current.lines.map((line, lineIndex) => (lineIndex === index ? { ...line, ...patch } : line)),
        }));
    };
    const changeDefaultCostCenter = (defaultCostCenterId: string) => {
        defaultCfoUserChanged.current = true;
        setDraft((current) => ({
            ...current,
            defaultCostCenterId,
            lines: current.lines.map(line => ({ ...line,
                costCenterId: line.costCenterId.trim() === '' || (defaultCostCenterId !== '' && sameOpaqueIdentifier(line.costCenterId, current.defaultCostCenterId))
                    ? defaultCostCenterId : line.costCenterId,
                cashFlowItemId: sameOpaqueIdentifier(defaultCostCenterId, current.defaultCostCenterId) ? line.cashFlowItemId : '',
            })),
        }));
    };
    const changePlannedPaymentDate = (plannedPaymentDate: string) => {
        setDraft((current) => {
            const previousDefault = accrualPeriodFromPlannedPaymentDate(current.plannedPaymentDate);
            const nextDefault = accrualPeriodFromPlannedPaymentDate(plannedPaymentDate);
            return {
                ...current,
                plannedPaymentDate,
                lines: current.lines.map((line) => (line.accrualPeriod.trim() === '' || line.accrualPeriod === previousDefault
                    ? { ...line, accrualPeriod: nextDefault }
                    : line)),
            };
        });
    };
    const addLine = () => {
        setDraft((current) => current.lines.length >= 10000 ? current : ({
            ...current,
            lines: [...current.lines, emptyLine(current.defaultCostCenterId, accrualPeriodFromPlannedPaymentDate(current.plannedPaymentDate), !!current.paymentOperation)],
        }));
    };
    const removeLine = (index: number) => {
        setDraft((current) => ({
            ...current,
            lines: current.lines.length === 1
                ? [emptyLine(current.defaultCostCenterId, accrualPeriodFromPlannedPaymentDate(current.plannedPaymentDate), !!current.paymentOperation)]
                : current.lines.filter((_, itemIndex) => itemIndex !== index),
        }));
    };
    const changePaymentBasis = (paymentBasisCode: PaymentBasisCode | '') => {
        if (paymentBasisCode !== draft.paymentBasisCode && !confirmScenarioDiscard([
            draft.invoiceNumber, draft.invoiceDate, draft.publicOfferName, draft.publicOfferIdentifier,
            draft.otherBasisExplanation, draft.contractId, draft.candidateContractNumber,
            draft.candidateContractDate, draft.candidateContractSubject, draft.candidateContractTerms,
            draft.newContractDocumentMissingExplanation, draft.candidateContractNumberMissing, draft.newContractDocumentMissing,
        ]))
            return;
        setDraft((current) => ({
            ...current,
            paymentBasisCode,
            invoiceNumber: paymentBasisCode === 'InvoiceWithoutContract' ? current.invoiceNumber : '',
            invoiceDate: paymentBasisCode === 'InvoiceWithoutContract' ? current.invoiceDate : '',
            publicOfferName: paymentBasisCode === 'PublicOffer' ? current.publicOfferName : '',
            publicOfferIdentifier: paymentBasisCode === 'PublicOffer' ? current.publicOfferIdentifier : '',
            otherBasisExplanation: paymentBasisCode === 'OtherBasis' ? current.otherBasisExplanation : '',
            contractId: paymentBasisCode === 'Contract' ? current.contractId : '',
            isNewContract: paymentBasisCode === 'Contract' ? current.isNewContract : false,
            candidateContractNumber: paymentBasisCode === 'Contract' ? current.candidateContractNumber : '',
            candidateContractNumberMissing: paymentBasisCode === 'Contract'
                ? current.candidateContractNumberMissing
                : false,
            candidateContractDate: paymentBasisCode === 'Contract' ? current.candidateContractDate : '',
            candidateContractSubject: paymentBasisCode === 'Contract' ? current.candidateContractSubject : '',
            candidateContractTerms: paymentBasisCode === 'Contract' ? current.candidateContractTerms : '',
            newContractDocumentMissing: paymentBasisCode === 'Contract'
                ? current.newContractDocumentMissing
                : false,
            newContractDocumentMissingExplanation: paymentBasisCode === 'Contract'
                ? current.newContractDocumentMissingExplanation
                : '',
        }));
    };
    const changeNewCounterparty = (isNewCounterparty: boolean) => {
        if (isNewCounterparty !== draft.isNewCounterparty && !confirmScenarioDiscard(isNewCounterparty
            ? [draft.counterpartyId, draft.contractId]
            : [draft.candidateCounterpartyName, draft.candidateResidencyCode, draft.candidateBinIin,
                draft.candidateCountry, draft.candidateForeignRegistrationNumber,
                draft.candidateRegistrationNumberMissing, draft.candidateRegistrationNumberMissingExplanation]))
            return;
        setDraft((current) => ({
            ...current,
            isNewCounterparty,
            counterpartyId: isNewCounterparty ? '' : current.counterpartyId,
            contractId: isNewCounterparty ? '' : current.contractId,
            candidateCounterpartyName: isNewCounterparty ? current.candidateCounterpartyName : '',
            candidateResidencyCode: isNewCounterparty ? current.candidateResidencyCode : '',
            candidateBinIin: isNewCounterparty ? current.candidateBinIin : '',
            candidateCountry: isNewCounterparty ? current.candidateCountry : '',
            candidateForeignRegistrationNumber: isNewCounterparty
                ? current.candidateForeignRegistrationNumber
                : '',
            candidateRegistrationNumberMissing: isNewCounterparty
                ? current.candidateRegistrationNumberMissing
                : false,
            candidateRegistrationNumberMissingExplanation: isNewCounterparty
                ? current.candidateRegistrationNumberMissingExplanation
                : '',
        }));
    };
    const changeOrganization = (organizationId: string) => {
        if (!sameOpaqueIdentifier(organizationId, draft.organizationId) && !confirmScenarioDiscard([draft.contractId]))
            return;
        setDraft((current) => ({
            ...current,
            organizationId,
            contractId: sameOpaqueIdentifier(organizationId, current.organizationId) ? current.contractId : '',
        }));
    };
    useEffect(() => {
        if (isDocumentReadOnly || isLoading || (requestId !== null && savedId === null)
            || organizations.length !== 1)
            return;
        const organizationId = organizations[0].id.trim();
        if (organizationId === '')
            return;
        setDraft((current) => current.organizationId.trim() !== ''
            ? current
            : { ...current, organizationId, contractId: '' });
    }, [isDocumentReadOnly, isLoading, organizations, requestId, savedId]);
    useEffect(() => {
        if (isDocumentReadOnly || isLoading || (requestId !== null && savedId === null)
            || !isNewRequestContext || defaultCfoUserChanged.current)
            return;
        const preferred = costCenters.find(c => sameOpaqueIdentifier(c.id, defaultActorCfo))
            ?? (costCenters.length === 1 ? costCenters[0] : null);
        const defaultCostCenterId = preferred?.id.trim() ?? '';
        if (defaultCostCenterId === '')
            return;
        setDraft((current) => current.defaultCostCenterId.trim() !== ''
            ? current
            : {
                ...current,
                defaultCostCenterId,
                lines: current.lines.map((line) => line.costCenterId.trim() === ''
                    ? { ...line, costCenterId: defaultCostCenterId }
                    : line),
            });
    }, [costCenters, defaultActorCfo, isDocumentReadOnly, isLoading, requestId, savedId, isNewRequestContext]);
    const changeCounterparty = (counterpartyId: string) => {
        if (!sameOpaqueIdentifier(counterpartyId, draft.counterpartyId) && !confirmScenarioDiscard([draft.contractId]))
            return;
        setDraft((current) => ({
            ...current,
            counterpartyId,
            contractId: sameOpaqueIdentifier(counterpartyId, current.counterpartyId) ? current.contractId : '',
        }));
    };
    const changeResidency = (candidateResidencyCode: CounterpartyResidencyCode | '') => {
        if (candidateResidencyCode !== draft.candidateResidencyCode && !confirmScenarioDiscard([
            draft.candidateBinIin, draft.candidateCountry, draft.candidateForeignRegistrationNumber,
            draft.candidateRegistrationNumberMissing, draft.candidateRegistrationNumberMissingExplanation,
        ]))
            return;
        setDraft((current) => ({
            ...current,
            candidateResidencyCode,
            candidateBinIin: candidateResidencyCode === 'ResidentKz' ? current.candidateBinIin : '',
            candidateCountry: candidateResidencyCode === 'NonResident' ? current.candidateCountry : '',
            candidateForeignRegistrationNumber: candidateResidencyCode === 'NonResident'
                ? current.candidateForeignRegistrationNumber
                : '',
            candidateRegistrationNumberMissing: candidateResidencyCode === 'NonResident'
                ? current.candidateRegistrationNumberMissing
                : false,
            candidateRegistrationNumberMissingExplanation: candidateResidencyCode === 'NonResident'
                ? current.candidateRegistrationNumberMissingExplanation
                : '',
        }));
    };
    const changeNewContract = (isNewContract: boolean) => {
        if (isNewContract !== draft.isNewContract && !confirmScenarioDiscard(isNewContract
            ? [draft.contractId]
            : [draft.candidateContractNumber, draft.candidateContractNumberMissing, draft.candidateContractDate,
                draft.candidateContractSubject, draft.candidateContractTerms, draft.newContractDocumentMissing,
                draft.newContractDocumentMissingExplanation]))
            return;
        setDraft((current) => ({
            ...current,
            isNewContract,
            contractId: isNewContract ? '' : current.contractId,
            candidateContractNumber: isNewContract ? current.candidateContractNumber : '',
            candidateContractNumberMissing: isNewContract ? current.candidateContractNumberMissing : false,
            candidateContractDate: isNewContract ? current.candidateContractDate : '',
            candidateContractSubject: isNewContract ? current.candidateContractSubject : '',
            candidateContractTerms: isNewContract ? current.candidateContractTerms : '',
            newContractDocumentMissing: isNewContract ? current.newContractDocumentMissing : false,
            newContractDocumentMissingExplanation: isNewContract
                ? current.newContractDocumentMissingExplanation
                : '',
        }));
    };
    const submit = async (event: FormEvent) => {
        event.preventDefault();
        if (isFormMutationLocked || submissionInFlight.current)
            return;
        const submittedDraft = draft;
        invalidatePreflight();
        setMessage(null);
        setIsSaving(true);
        try {
            const isUpdate = savedId !== null;
            let saved: Awaited<ReturnType<typeof createDraft>>;
            if (savedId === null) {
                // A lost create response may already have consumed the lease. Resolve the committed
                // command before asking to renew it, otherwise a successful save becomes unrecoverable.
                await recoverPendingCreateDraft(numberReservation.currentLeaseId());
                const issue = expenseDraftInputIssue(draft, true);
                if (issue) {
                    setActiveTab(issue.tab);
                    setMessage({ tone: 'error', text: issue.text });
                    return;
                }
                const numberLease = await numberReservation.renew();
                if (!numberLease)
                    throw new Error('Номер заявки пока не определён.');
                saved = await createDraft(draft, numberLease.leaseId, attachmentUploadSessionId);
            }
            else {
                const issue = expenseDraftInputIssue(draft, false);
                if (issue) {
                    setActiveTab(issue.tab);
                    setMessage({ tone: 'error', text: issue.text });
                    return;
                }
                if (!expectedVersion) {
                    throw new Error('Версия сохранённого черновика не определена. Обновите данные и повторите сохранение.');
                }
                saved = await updateDraft(savedId, expectedVersion, draft);
            }
            setSavedId(saved.id);
            setSourceInitiatorExternalId(saved.initiatorExternalId ?? null);
            setRequestStatus(saved.status);
            setExpectedVersion(saved.concurrencyStamp);
            setBudgetReservation(saved.budgetReservation ?? null);
            setPreliminaryCurrencyCalculation(preliminaryCurrencyCalculationFromSaved(saved));
            setSubmissionReadiness(saved.submissionReadiness ?? null);
            setSystemFields(expenseRequestSystemFieldsFromSaved(saved, currentActorExternalId, currentActorDisplayName));
            invalidatePreflight();
            setHistoryInvalidationToken((value) => value + 1);
            setDraft((current) => reconcileSavedLines(submittedDraft, current, saved));
            setSavedDraftSignature(draftSignature(draftFromSaved(saved)));
            onSaved?.(saved);
            setMessage({
                tone: 'success',
                text: `${isUpdate ? 'Изменения сохранены' : 'Черновик сохранён'}. Сумма: ${formatMoney(saved.totalAmount)} ${draft.currencyCode}.`,
            });
        }
        catch (error) {
            if (error instanceof RecoveredPendingSaveError) {
                const recovered = error.recoveredRequest;
                const recoveredDraft = draftFromSaved(recovered);
                setDraft((current) => reconcileSavedLines(submittedDraft, current, recovered));
                setSavedId(recovered.id);
                setSourceInitiatorExternalId(recovered.initiatorExternalId ?? null);
                setRequestStatus(recovered.status);
                setExpectedVersion(recovered.concurrencyStamp);
                setSavedDraftSignature(draftSignature(recoveredDraft));
                setBudgetReservation(recovered.budgetReservation ?? null);
                setPreliminaryCurrencyCalculation(preliminaryCurrencyCalculationFromSaved(recovered));
                setSubmissionReadiness(recovered.submissionReadiness ?? null);
                setSystemFields(expenseRequestSystemFieldsFromSaved(recovered, currentActorExternalId, currentActorDisplayName));
                invalidatePreflight();
                setHistoryInvalidationToken((value) => value + 1);
                onSaved?.(recovered);
                setMessage({ tone: 'warning', text: recoveredPendingSaveWarning });
            }
            else {
                setMessage({ tone: 'error', text: expenseRequestSaveErrorMessage(error) });
            }
        }
        finally {
            setIsSaving(false);
        }
    };
    const createCopy = async () => {
        if (isCopyMutationLocked || submissionInFlight.current || !savedId)
            return;
        invalidatePreflight();
        setMessage(null);
        setIsCopying(true);
        try {
            const copied = await copyCompleteExpenseRequest(savedId);
            // The workspace keeps this source mounted under its original document key.
            // Its status, amounts and edit authority must not become those of the copy.
            if (onCopied) {
                onCopied(copied);
                return;
            }
            const copiedDraft = draftFromSaved(copied);
            setDraft(copiedDraft);
            setSavedId(copied.id);
            setSourceInitiatorExternalId(copied.initiatorExternalId ?? null);
            setCanEditExisting(copied.editAuthority?.canEdit ?? canCreateExpenseRequest);
            setRequestStatus(copied.status);
            setExpectedVersion(copied.concurrencyStamp);
            setSavedDraftSignature(draftSignature(copiedDraft));
            setBudgetReservation(copied.budgetReservation ?? null);
            setPreliminaryCurrencyCalculation(preliminaryCurrencyCalculationFromSaved(copied));
            setSubmissionReadiness(copied.submissionReadiness ?? null);
            setSystemFields(expenseRequestSystemFieldsFromSaved(copied, currentActorExternalId, currentActorDisplayName));
            invalidatePreflight();
            setHistoryInvalidationToken((value) => value + 1);
            setActiveTab('main');
            setMessage({ tone: 'warning', text: copyCreatedWarning });
        }
        catch (error) {
            setMessage({
                tone: 'error',
                text: expenseRequestCopyErrorMessage(error),
            });
        }
        finally {
            setIsCopying(false);
        }
    };
    const submitForApproval = async () => {
        if (isFormMutationLocked || submissionInFlight.current
            || (savedId === null && !canCreateExpenseRequest))
            return;
        submissionInFlight.current = true;
        const contextGeneration = submissionContext.current.generation;
        const clickedDraft = draft;
        const allowedSignatures = new Set([currentDraftSignature]);
        const contextIsCurrent = () => refreshMounted.current
            && submissionContext.current.generation === contextGeneration;
        const inputsAreCurrent = () => allowedSignatures.has(submissionContext.current.signature);
        let id = savedId;
        let version = expectedVersion;
        let stage: 'save' | 'check' | 'submit' = 'save';
        const acceptSaved = (saved: SavedExpenseRequest, preserveInputs = false) => {
            const savedDraft = draftFromSaved(saved);
            allowedSignatures.add(draftSignature(savedDraft));
            setDraft(current => preserveInputs ? current : reconcileSavedLines(clickedDraft, current, saved));
            setSavedId(saved.id);
            setSourceInitiatorExternalId(saved.initiatorExternalId ?? null);
            setRequestStatus(saved.status);
            setExpectedVersion(saved.concurrencyStamp);
            setSavedDraftSignature(draftSignature(savedDraft));
            setBudgetReservation(saved.budgetReservation ?? null);
            setPreliminaryCurrencyCalculation(preliminaryCurrencyCalculationFromSaved(saved));
            setSubmissionReadiness(saved.submissionReadiness ?? null);
            setSystemFields(expenseRequestSystemFieldsFromSaved(saved, currentActorExternalId, currentActorDisplayName));
            invalidatePreflight();
            setHistoryInvalidationToken(value => value + 1);
            onSaved?.(saved);
        };
        invalidatePreflight();
        setMessage(null);
        setIsSubmitting(true);
        try {
            if (id !== null) {
                stage = 'submit';
                await recoverPendingExpenseRequestSubmit(id);
                if (!contextIsCurrent() || !inputsAreCurrent())
                    return;
                stage = 'save';
            }
            if (!isSavedAndClean) {
                const issue = expenseDraftInputIssue(clickedDraft, id === null);
                if (issue) {
                    setActiveTab(issue.tab);
                    setMessage({ tone: 'error', text: issue.text });
                    return;
                }
                let saved: SavedExpenseRequest;
                if (id === null) {
                    await recoverPendingCreateDraft(numberReservation.currentLeaseId());
                    if (!contextIsCurrent() || !inputsAreCurrent())
                        return;
                    const lease = await numberReservation.renew();
                    if (!contextIsCurrent() || !inputsAreCurrent())
                        return;
                    if (!lease)
                        throw new Error('Номер заявки пока не определён.');
                    saved = await createDraft(clickedDraft, lease.leaseId, attachmentUploadSessionId);
                }
                else {
                    if (!version)
                        throw new Error('Версия сохранённого черновика не определена. Обновите данные и повторите сохранение.');
                    saved = await updateDraft(id, version, clickedDraft);
                }
                if (!contextIsCurrent())
                    return;
                if (id !== null && !sameOpaqueIdentifier(saved.id, id)) {
                    setMessage({ tone: 'error', text: 'Сервер вернул результат сохранения для другой заявки. Обновите данные.' });
                    return;
                }
                const unchanged = inputsAreCurrent();
                acceptSaved(saved);
                id = saved.id;
                version = saved.concurrencyStamp;
                if (!unchanged) {
                    setMessage({ tone: 'warning', text: 'Во время сохранения поля изменились. Новые изменения сохранены в форме; подача не выполнена.' });
                    return;
                }
            }
            if (!id || !version || !contextIsCurrent() || !inputsAreCurrent())
                return;
            // Never authorize a command from a cached/background result. Check the
            // exact version returned by this save, then pass that stamp to Submit.
            stage = 'check';
            const controller = new AbortController();
            submissionReadController.current = controller;
            let timeout: ReturnType<typeof window.setTimeout> | undefined;
            const checked = await Promise.race([
                getExpenseRequestSubmissionPreflight(id, controller.signal),
                new Promise<never>((_, reject) => {
                    timeout = window.setTimeout(() => {
                        reject(new Error('Проверка готовности временно недоступна. Повторите попытку позднее.'));
                        controller.abort();
                    }, 10000);
                }),
            ]).finally(() => {
                window.clearTimeout(timeout);
                if (submissionReadController.current === controller)
                    submissionReadController.current = null;
            });
            if (!contextIsCurrent() || !inputsAreCurrent())
                return;
            if (!sameOpaqueIdentifier(checked.requestId, id) || !sameOpaqueIdentifier(checked.requestVersion, version)) {
                setMessage({ tone: 'error', text: 'Сервер вернул проверку для другой или устаревшей версии заявки. Обновите данные и повторите попытку.' });
                return;
            }
            if (!checked.canSubmit) {
                setSubmissionReadiness(checked.submissionReadiness);
                setActiveTab('main');
                const first = checked.submissionReadiness.missingRequirements.find(code => expenseReadinessTarget(code));
                const blocker = checked.blockers.find(value => expenseReadinessTarget(value.code, value.lineNumber));
                if (first)
                    navigateToIssue(first);
                else if (blocker)
                    navigateToIssue(blocker.code, blocker.lineNumber);
                const issues = [...new Set([
                        ...checked.submissionReadiness.missingRequirements.map(code => code === 'SupportingDocumentOrMissingFlag'
                            ? 'Не прикреплен подтверждающий документ, либо укажите причину его отсутствия'
                            : readinessLabel(code)),
                        ...uniquePreflightBlockers(checked).map(preflightBlockerLabel),
                    ])];
                window.alert(issues.length ? issues.join('\n') : 'Не удалось подтвердить готовность заявки. Повторите проверку позднее.');
                setMessage({ tone: 'warning', text: 'Черновик сохранён. Заявка не отправлена.' });
                return;
            }
            stage = 'submit';
            const submitted = await submitExpenseRequest(id, version);
            if (!contextIsCurrent())
                return;
            if (!sameOpaqueIdentifier(submitted.id, id)) {
                setMessage({ tone: 'error', text: 'Сервер вернул результат подачи для другой заявки. Обновите данные и повторите попытку.' });
                return;
            }
            acceptSaved(submitted);
            setMessage({ tone: 'success', text: submitted.status === 'TO_PAY'
                    ? 'Заявка передана в оплату.' : submitted.status === 'AWAITING_NSI'
                    ? 'Заявка передана на проверку реквизитов НСИ.' : 'Заявка направлена на согласование.' });
        }
        catch (error) {
            if (!contextIsCurrent())
                return;
            if (error instanceof RecoveredPendingSaveError) {
                const recovered = error.recoveredRequest;
                if (id === null || sameOpaqueIdentifier(recovered.id, id)) {
                    // Recovery can describe an older intent. Never automatically submit
                    // that older payload or overwrite the latest local values with it.
                    acceptSaved(recovered, stage !== 'submit');
                    setMessage({ tone: 'warning', text: stage === 'submit'
                            ? 'Предыдущая попытка подачи подтверждена сервером.'
                            : 'Предыдущая попытка сохранения подтверждена сервером. Текущие поля оставлены в форме; проверьте их и повторите подачу.' });
                }
                else {
                    setMessage({ tone: 'error', text: 'Сервер вернул результат подачи для другой заявки. Обновите данные и повторите попытку.' });
                }
            }
            else {
                const commandError = expenseRequestSaveErrorMessage(error);
                setMessage({ tone: 'error', text: stage === 'check'
                        ? expenseRequestPreflightErrorMessage(error)
                        : stage === 'submit' && commandError === 'Не удалось сохранить черновик.'
                            ? 'Не удалось подтвердить подачу заявки. Повторите попытку — дублирование исключается ключом операции.'
                            : commandError });
            }
        }
        finally {
            submissionInFlight.current = false;
            setIsSubmitting(false);
        }
    };
    return (<div ref={formRoot} className="expense-request-form-page" aria-busy={isLoading}>
        <div className="document-heading">
          <div>
            <h1>Расходная заявка{(systemFields.numberLabel ?? numberReservation.lease?.number)
            ? ` № ${systemFields.numberLabel ?? numberReservation.lease?.number}` : ''}
              {titleDate && ` от ${titleDate}`}
              {(isNewRequestContext || requestStatus === 'DRAFT') && <span className="muted"> (черновик)</span>}</h1>
            {isNewRequestContext && !draft.defaultCostCenterId && <small className="muted">Выберите ЦФО — номер будет присвоен автоматически.</small>}
            {isNewRequestContext && numberReservation.error && <small role="alert">{numberReservation.error}</small>}
          </div>
          <div className="document-statuses">
            <div className="document-state" data-status-tone={expenseRequestStatusTone(requestStatus, displayStatusLabel)}><span className="status-dot"/>{displayStatusLabel}</div>
          </div>
        </div>

        <form onSubmit={submit} onChangeCapture={() => { newDraftTouched.current = true; }} onClickCapture={() => { newDraftTouched.current = true; }}>
          <div className="command-bar document-command-bar">
            {!isReadView && <button className={submissionPreflight?.canSubmit ? 'secondary-button' : 'primary-button'} type="submit" disabled={isFormMutationLocked || isSaving || isLoading || (savedId !== null && !hasUnsavedChanges)} title={isDocumentReadOnly ? 'Режим только для чтения' : undefined}>
              <Save size={18}/>{isLoading ? 'Загрузка…' : isSaving ? 'Сохранение…' : isNewRequestContext ? 'Сохранить черновик' : 'Сохранить изменения'}
            </button>}
            {!isReadView && <button className={submissionPreflight?.canSubmit ? 'primary-button' : 'secondary-button'} type="button" disabled={isFormMutationLocked || (savedId === null && !canCreateExpenseRequest)} title={isDocumentReadOnly
                ? 'Режим только для чтения'
                : 'Сохранить, проверить и отправить на согласование'} onClick={submitForApproval}>
              <Check size={18}/>{isSubmitting ? 'Подача…' : 'Подать на согласование'}
            </button>}
            <span className="document-cancel-command" ref={setCancelButtonTarget}/>
            {canCopyExpenseRequest && <button className="secondary-button" type="button" disabled={isCopyMutationLocked} title={readOnly
                ? 'Режим только для чтения'
                : !savedId
                    ? 'Сначала сохраните черновик, чтобы создать копию'
                    : !canCopyExisting
                        ? 'Сначала откройте доступную заявку'
                        : !canCreateExpenseRequest
                            ? 'Нет прав для создания нового черновика'
                            : 'Создать новый черновик из сохранённых данных заявки'} onClick={createCopy}>
              <CopyPlus size={18}/>{isCopying ? 'Создание копии…' : 'Создать копию'}
            </button>}
            <span className="command-separator"/>
            <ExpenseRequestGuideLink />
            <button className="icon-command" type="button" aria-label="Вложения" title="Открыть вложения" onClick={() => { setActiveTab('main'); window.requestAnimationFrame(() => document.getElementById('expense-panel-attachments')?.scrollIntoView({ block: 'nearest' })); }}>
              <BookOpen />
            </button>
            <button className="icon-command" type="button" aria-label="Печать" title={isLoading
            ? 'Дождитесь загрузки заявки'
            : printReferenceDataLoading
                ? 'Дождитесь загрузки справочников'
                : lineBudgetLimits.some(limit => limit.state === 'loading')
                    ? 'Дождитесь расчёта бюджета'
                    : 'Печать заявки'} disabled={isLoading || printReferenceDataLoading || lineBudgetLimits.some(limit => limit.state === 'loading') || isAwaitingRequest || refreshRequired || isSaving || isSubmitting || attachmentWorkPending} onClick={() => setPrintData(structuredClone({ draft, number: documentNumber, system: systemFields, status: displayStatusLabel,
            unsaved: hasUnsavedChanges || hasNewDraftChanges || savedId === null, organizations, costCenters: [...costCenters, ...accrualCostCenters], counterparties, contracts,
            articles: [...accrualSettings.items, ...cashFlowItemLabels, ...cashFlowItems], paymentBases, residencies: counterpartyResidencies,
            budgetControls: lineBudgetControls, budgetLimits: lineBudgetLimits, requestId: savedId, version: expectedVersion,
            uploadSessionId: attachmentUploadSessionId, stagedCount: stagedAttachmentCount }))}>
              <Printer />
            </button>
            <span className={`document-save-state${isReadView ? ' expense-view-state' : ''}`} aria-live="polite" title={isReadView ? 'Реквизиты доступны только для просмотра. Разрешённые действия показаны отдельно.' : undefined}>
              {isReadView ? <><Eye size={15} aria-hidden="true"/>Только просмотр</> : isLoading ? 'Загрузка…' : isSaving ? 'Сохранение…'
            : message?.tone === 'error' ? 'Проверьте сообщение об ошибке'
                : hasUnsavedChanges || hasNewDraftChanges ? 'Есть несохранённые изменения'
                    : isSavedAndClean ? 'Сохранено' : 'Новый черновик'}</span>
            <button id="expense-history-command" className="secondary-button expense-history-command" type="button" aria-expanded={activeTab === 'history'} aria-controls={activeTab === 'history' ? 'expense-panel-history' : undefined} onClick={() => {
            focusTarget.current = null;
            if (activeTab === 'history') {
                setActiveTab(visibleFormTabs.some(tab => tab.id === previousFormTab.current) ? previousFormTab.current : 'main');
            }
            else {
                previousFormTab.current = activeTab;
                setActiveTab('history');
            }
        }}>
              <History size={18}/>История
            </button>
          </div>

          <ApprovalComments360 requestId={savedId} invalidationToken={historyInvalidationToken}/>
          <ExpenseRequestWorkflowCommandBar commands={workflowCommands.commands} loading={workflowCommands.loading} mutationLocked={readOnly || refreshRequired || isLoading || isCopying || isSaving || isSubmitting || attachmentWorkPending || currencyRefresh.busy || treasuryDateBusy || approvalDetailsBusy} executingCode={workflowCommands.executingCode} error={workflowCommands.error} cancelButtonTarget={cancelButtonTarget} counterparties={counterparties} initialCounterpartyId={draft.counterpartyId || null} initialContractId={draft.contractId || null} isNewCounterparty={draft.isNewCounterparty} isNewContract={draft.isNewContract} organizationId={draft.organizationId} requestCounterpartyId={draft.counterpartyId || null} requestId={savedId} onExecute={workflowCommands.execute}/>

          {!isPayrollAdvance && savedId && expectedVersion && ['IN_APPROVAL', 'APPROVED'].includes(requestStatus ?? '') &&
            <ApprovalDetailsEditor id={savedId} version={expectedVersion} lines={draft.lines} organizationId={draft.organizationId} counterpartyId={draft.counterpartyId} currencyCode={draft.currencyCode} locked={readOnly || refreshRequired || isLoading || isCopying || isSaving || isSubmitting || attachmentWorkPending || currencyRefresh.busy || workflowCommands.executingCode !== null || treasuryDateBusy} onSaved={applyWorkflowSaved} onBusyChange={setApprovalDetailsBusy}/>}

          {savedId && expectedVersion && ['APPROVED', 'TO_PAY', 'PARTIALLY_PAID', 'PAID', 'CLOSED'].includes(requestStatus ?? '') &&
            <TreasuryPaymentDatePanel id={savedId} version={expectedVersion} locked={readOnly || refreshRequired || isLoading || isCopying || isSaving || isSubmitting || attachmentWorkPending || currencyRefresh.busy || workflowCommands.executingCode !== null || approvalDetailsBusy} onSaved={applyWorkflowSaved} onBusyChange={setTreasuryDateBusy}/>}

          {preflightError && <div className="notice error" role="status">{preflightError}</div>}
          {message && <div className={`notice ${message.tone}`} role="status">{message.text}</div>}
          {referenceError && <div className="notice warning" role="status">{referenceError}</div>}
          {accrualSettings.error && <div className="notice warning" role="status">{accrualSettings.error}</div>}
          {isDocumentReadOnly && !isReadView && (<div className="notice warning" role="status">
              {readOnly
                ? 'Режим только для чтения. Бизнес-команды и изменение полей недоступны.'
                : 'Редактирование полей заявки недоступно. Доступные команды показаны выше.'}
            </div>)}

          <div className="document-tabs" role="tablist" aria-label="Разделы расходной заявки" onKeyDown={onFormTabsKeyDown}>
            {visibleFormTabs.map((tab, index) => (<button key={tab.id} id={`expense-tab-${tab.id}`} ref={(element) => {
                formTabRefs.current[index] = element;
            }} className={activeTab === tab.id ? 'active' : ''} type="button" role="tab" aria-controls={activeTab === tab.id ? `expense-panel-${tab.id}` : undefined} aria-selected={activeTab === tab.id} tabIndex={activeTab === tab.id || (activeTab === 'history' && index === 0) ? 0 : -1} onClick={() => { focusTarget.current = null; setActiveTab(tab.id); }}>
                {tab.id === 'counterparty' && draft.paymentOperation ? (draft.paymentOperation.kind === 'BusinessTrip' ? 'Сотрудники' : 'Реквизиты операции') : tab.label}
              </button>))}
          </div>
          {activeTab === 'main' && (<fieldset className="expense-edit-boundary" disabled={isFormMutationLocked}>
              <ExpenseRequestHeaderPanel viewOnly={isReadView} draft={draft} onChange={patchDraft} onOrganizationChange={changeOrganization} onDefaultCostCenterChange={changeDefaultCostCenter} onPlannedPaymentDateChange={changePlannedPaymentDate} organizations={organizations} costCenters={costCenters} currencies={currencies} savedId={savedId} preliminaryCurrencyCalculation={preliminaryCurrencyCalculation} submissionReadiness={isDocumentReadOnly || submissionPreflight ? null : submissionReadiness?.isReady ? null : submissionReadiness} onNavigateToIssue={navigateToIssue} hasUnsavedChanges={hasUnsavedChanges} canEditReturnedPaymentPurpose={requestStatus === 'RETURNED'
                && canEditExisting === true && !isDocumentReadOnly
                && currentActorExternalId !== null
                && currentActorExternalId === sourceInitiatorExternalId} systemFields={{ ...systemFields, statusLabel: displayStatusLabel }} latestRate={draftDefaults.data?.currencyRate ?? null} rateLoading={draftDefaults.loading} rateError={draftDefaults.error}>
              </ExpenseRequestHeaderPanel>
            </fieldset>)}


          {activeTab === 'counterparty' && (<fieldset className="expense-edit-boundary" disabled={isFormMutationLocked}>
              <ExpenseRequestCounterpartyPanel viewOnly={isReadView} requestNumber={documentNumber ?? undefined} draft={draft} onChange={patchDraft} onPaymentBasisChange={changePaymentBasis} onNewCounterpartyChange={changeNewCounterparty} onCounterpartyChange={changeCounterparty} onResidencyChange={changeResidency} onNewContractChange={changeNewContract} counterparties={counterparties} contracts={contracts} paymentBases={paymentBases} counterpartyResidencies={counterpartyResidencies}/>
            </fieldset>)}
          {activeTab === 'lines' && (<>
            {hasAccrualDepartments && <AccrualDepartmentsNotice485 catalog={accrualDepartments}/>}
            <fieldset className="expense-edit-boundary" disabled={isFormMutationLocked}>
              <ExpenseRequestLinesPanel viewOnly={isReadView} lines={draft.lines} withoutVat={!!draft.paymentOperation} lineAmounts={lineAmounts} currencyCode={draft.currencyCode} cashFlowItems={cashFlowItems} costCenters={accrualCostCenters} departmentCatalog={accrualDepartments} showDepartmentNotice={false} total={total} totalVat={totalVat} totalExcludingVat={totalExcludingVat} budgetControls={lineBudgetControls} budgetLimits={lineBudgetLimits} plannedPaymentDate={draft.plannedPaymentDate} cashFlowItemLabels={[...accrualSettings.items, ...cashFlowItemLabels]} accrualSettingsLoading={accrualSettings.loading} onOpenBudget={canOpenBudget440 ? openBudget440 : undefined} onAddLine={addLine} onUpdateLine={updateLine} onRemoveLine={removeLine}/>
            </fieldset>
              <ExpenseRequestApprovalRoute draft={draft} requestId={savedId} version={expectedVersion} status={requestStatus} author={systemFields.initiatorLabel} actor={currentActorExternalId} enabled={!isLoading && !isAwaitingRequest && !isSaving && !isSubmitting && !refreshRequired}/>
            </>)}
          {activeTab === 'explanation' && budgetDetail440?.scope === budgetDetailScope440 && canOpenBudget440 && <ExpenseBudgetExplanation442 key={budgetDetail440.scope + JSON.stringify(budgetDetail440.context.key)} context={budgetDetail440.context} onClose={closeBudget440} onRefreshRequest={refreshBudgetRequest478}/>}
          {isPayrollAdvance && activeTab !== 'history' && <ExpenseRequestApprovalRoute draft={draft} requestId={savedId} version={expectedVersion} status={requestStatus} author={systemFields.initiatorLabel} actor={currentActorExternalId} enabled={!isLoading && !isAwaitingRequest && !isSaving && !isSubmitting && !refreshRequired}/>} 
          {showPaymentTabs && activeTab === 'budget' && (<section id="expense-panel-budget" role="tabpanel" aria-labelledby="expense-tab-budget">
              {isPayrollAdvance && documentNumber && <p className="section-hint">Ссылка для сопоставления оплаты в 1С: <strong>BMS:{documentNumber}</strong></p>}
              <ExpenseRequestPaymentPanel savedId={savedId}/>
              <ExpenseRequestPaymentDetailsPanel key={savedId} requestId={savedId}/>
            </section>)}

          {showPaymentTabs && activeTab === 'closing' && (<ExpenseRequestClosingPanel disabled={refreshRequired || isLoading} requestId={savedId} requestVersion={expectedVersion} onStateChanged={applyClosingState}/>)}

          <div hidden={activeTab !== 'main' && !(activeTab === 'counterparty' && draft.isNewContract)}>
            <div hidden={activeTab !== 'main'}>
            {currencyRefresh.busy && <div role="status">Обновляем курс и пересчёт в тенге…</div>}
            {currencyRefresh.error && <div role="alert" className="notice warning">{currencyRefresh.error} <button type="button" onClick={currencyRefresh.retry}>Повторить пересчёт</button></div>}
            {attachmentReadinessLoading && <div role="status">Обновляем проверку вложений…</div>}
            {attachmentReadinessFailed && !submissionPreflight && <div role="status" className="notice warning">Не удалось обновить готовность после изменения вложений. Обновите карточку. Несохранённые поля не изменены.</div>}
            </div>
            <ExpenseRequestAttachmentsPanel viewOnlyMetadata={isReadView} contractSaveRequired={savedId !== null && hasUnsavedChanges && draft.isNewContract} onSaveBeforeContractUpload={() => { void submit({ preventDefault() { } } as FormEvent); }} contractOnly={activeTab === 'counterparty'} reloadRevision={currencyFilesRevision} embedded uploadSessionId={isNewRequestContext && !isDocumentReadOnly && canCreateExpenseRequest ? attachmentUploadSessionId : undefined} onPendingWorkChanged={setAttachmentWorkPending} key={savedId ?? 'unsaved-expense-request'} supportingDocumentMissing={draft.supportingDocumentMissing} supportingDocumentMissingExplanation={draft.supportingDocumentMissingExplanation} isNewContract={draft.isNewContract} newContractDocumentMissing={draft.newContractDocumentMissing} newContractDocumentMissingExplanation={draft.newContractDocumentMissingExplanation} submissionReadiness={submissionReadiness} onChange={patchDraft} onValidateUploadIntent={validateAttachmentUploadIntent} requestId={savedId} requestVersion={expectedVersion} metadataDisabled={isFormMutationLocked} mutationTemporarilyDisabled={refreshRequired || isLoading || isSaving || isCopying || isSubmitting || isAwaitingRequest || currencyRefresh.busy} onRequestVersionChanged={(nextVersion) => {
            setExpectedVersion(nextVersion);
            invalidatePreflight();
        }} onAttachmentsChanged={() => {
            setAttachmentReadinessRevision((value) => value + 1);
            invalidatePreflight();
            setHistoryInvalidationToken((value) => value + 1);
        }} onStagedFileCountChanged={setStagedAttachmentCount}/>
          </div>
          {activeTab === 'history' && <ExpenseRequestHistoryPanel requestId={savedId} invalidationToken={historyInvalidationToken} labelledBy="expense-history-command"/>}
        </form>
        {printData && <ExpenseRequestPrintDocument data={printData} onClose={() => setPrintData(null)}/>}
    </div>);
}
