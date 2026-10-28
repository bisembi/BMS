export type VatRateCode = 'Vat16' | 'WithoutVat';
export type PaymentBasisCode = 'Contract' | 'InvoiceWithoutContract' | 'PublicOffer' | 'OtherBasis';
export type CounterpartyResidencyCode = 'ResidentKz' | 'NonResident';
export type ExpenseRequestStatus = 'DRAFT' | 'RETURNED' | 'IN_APPROVAL' | 'APPROVED' | 'AWAITING_NSI' | 'TO_PAY' | 'PARTIALLY_PAID' | 'PAID' | 'CLOSED' | 'REJECTED' | 'CANCELLED';
export type ExpenseRequestCurrentStage = 'Расчетчик зарплаты' | 'Владелец ЦФО' | 'Финансовый контролёр' | 'Финансовый директор' | 'Генеральный директор' | 'Специалист НСИ' | 'Казначей';
export type ExpenseLineDraft = {
    /** Client-only row identity. It is never included in an API request. */
    clientRowKey?: string;
    id?: string;
    cashFlowItemId: string;
    costCenterId: string;
    accrualPeriod: string;
    accrualCostCenterId?: string;
    amount: string;
    vatRateCode: VatRateCode;
};
export type ExpenseRequestDraft = {
    paymentOperation?: import('./utils/expensePaymentOperation').ExpensePaymentOperation | null;
    organizationId: string;
    defaultCostCenterId: string;
    plannedPaymentDate: string;
    currencyCode: string;
    counterpartyId: string;
    contractId: string;
    purpose: string;
    comment: string;
    justification: string;
    isUrgent: boolean;
    urgencyReason: string;
    isNewCounterparty: boolean;
    isNewContract: boolean;
    paymentBasisCode: PaymentBasisCode | '';
    invoiceNumber: string;
    invoiceDate: string;
    publicOfferName: string;
    publicOfferIdentifier: string;
    otherBasisExplanation: string;
    supportingDocumentMissing: boolean;
    supportingDocumentMissingExplanation: string;
    candidateCounterpartyName: string;
    candidateResidencyCode: CounterpartyResidencyCode | '';
    candidateBinIin: string;
    candidateCountry: string;
    candidateForeignRegistrationNumber: string;
    candidateRegistrationNumberMissing: boolean;
    candidateRegistrationNumberMissingExplanation: string;
    candidateContractNumber: string;
    candidateContractNumberMissing: boolean;
    candidateContractDate: string;
    candidateContractSubject: string;
    candidateContractTerms: string;
    newContractDocumentMissing: boolean;
    newContractDocumentMissingExplanation: string;
    lines: ExpenseLineDraft[];
};
export type SavedExpenseRequestLine = {
    id: string;
    cashFlowItemId: string | null;
    costCenterId: string | null;
    accrualPeriod: string | null;
    accrualCostCenterId?: string | null;
    amount: number;
    vatRateCode: VatRateCode | null;
    vatAmount: number | null;
    amountExcludingVat: number | null;
    amountKzt: number | null;
    vatAmountKzt?: number | null;
    amountExcludingVatKzt?: number | null;
};
export type BudgetReservationPosition = {
    expenseRequestLineId: string;
    cashFlowItemId: string;
    costCenterId: string;
    cashFlowItemName?: string | null;
    costCenterName?: string | null;
    budgetMonth: string;
    reservedAmountKzt: number;
    limitAmountKzt?: number | null;
    availableAmountKzt?: number | null;
    paidAmountKzt?: number;
    isLimitConfigured?: boolean;
    isOverLimit?: boolean | null;
};
export type BudgetPosition = {
    requestControl343?: {
        availableBeforeKzt: number | null;
        availableAfterKzt: number | null;
        positiveDeficitKzt: number | null;
        frozen: boolean;
    };
    planCostCenters?: {
        costCenterId: string;
        code: string | null;
        name: string | null;
        reservedAmountKzt: number;
    }[];
    costCenterId: string;
    cashFlowItemId: string;
    budgetMonth: string;
    limitAmountKzt: number;
    reservedAmountKzt: number;
    paidAmountKzt: number;
    availableAmountKzt: number;
    isLimitConfigured: boolean;
    isOverLimit: boolean;
};
/// Totals over the whole filtered area, not over the page.
///
/// Reserved, paid and effective limits are always numeric. A position without
/// an approved version contributes an explicit zero, so the counters remain
/// additive and the available amount is always calculable.
export type BudgetPositionTotals = {
    totalPositions: number;
    configuredLimitCount: number;
    unconfiguredLimitCount: number;
    limitAmountKzt: number;
    reservedAmountKzt: number;
    paidAmountKzt: number;
    availableAmountKzt: number;
};
/// One expense request that holds part of a budget position's current
/// reserve. A request can put several of its lines on the same budget key;
/// they arrive aggregated into one participant.
///
/// There is no request number in this system, so a participant is identified
/// by its purpose, its planned payment date and its status. No identifier is
/// published: nothing on this screen opens a request.
export type BudgetReservationParticipant = {
    status: ExpenseRequestStatus;
    purpose: string | null;
    plannedPaymentDate: string;
    lineCount: number;
    reservedAmountKzt: number;
};
/// The composition of one budget position's current reserve. The totals
/// describe the whole reserve, not the page: totalReservedAmountKzt must equal
/// the position's own reserved amount.
///
/// Only the reserve is broken down. The paid part of a position is never
/// attributed to individual requests.
export type BudgetReservationPage = {
    factSource?: ExecutionFactSource;
    items: BudgetReservationParticipant[];
    skip: number;
    take: number;
    total: number;
    totalLineCount: number;
    totalReservedAmountKzt: number;
    /** Visible-card subtotal; omitted only by a legacy unrestricted response. */
    visibleReservedAmountKzt?: number;
    position: BudgetPosition;
};
export type BudgetPositionPage = {
    factSource?: ExecutionFactSource;
    items: BudgetPosition[];
    skip: number;
    take: number;
    total: number;
    expenseRequestId: string | null;
    expenseRequestVersion?: string | null;
    totals: BudgetPositionTotals;
};
export type ExecutionFactSource = {
    snapshotId: string;
    sourceGeneration: string;
    grossHash: string;
    capturedAtUtc: string;
    cutoffExclusiveLocal: string;
    sourceYear: number;
};
export type BudgetReservationSummary = {
    isComplete: boolean;
    reservedAmountKzt: number;
    missingRequirements: string[];
    positions: BudgetReservationPosition[];
};
export type SubmissionReadiness = {
    isReady: boolean;
    missingRequirements: string[];
};
export type AttachmentUploadIntentRejectionCode = 'MissingFileName' | 'EmptyFile' | 'FileTooLarge' | 'InvalidFileName' | 'ExecutableExtension';
export type AttachmentUploadIntentResult = {
    isAccepted: boolean;
    rejectionCode: AttachmentUploadIntentRejectionCode | null;
    maximumFileSizeBytes: number;
};
export type PreliminaryCurrencyCalculation = {
    currencyCode: string;
    rateDate: string | null;
    rate: number | null;
    multiplicity: number | null;
    totalAmountKzt: number | null;
};
export type SubmissionPreflightBlocker = {
    category: string;
    code: string;
    lineNumber: number | null;
};
export type SubmissionPreflight = {
    operationKind?: 'PayrollAdvance';
    requestId: string;
    requestVersion: string;
    evaluatedAtUtc: string;
    submissionReadiness: SubmissionReadiness;
    budgetReadiness: {
        isReady: boolean;
        positiveDeficitKzt: number | null;
        blockers: string[];
    };
    capabilities: {
        attachmentFactsReady: boolean;
        accrualSnapshotSourceReady: boolean;
        workflowSeedReady: boolean;
        roleAssignmentsReady: boolean;
    };
    routePreview: {
        previewOnly: true;
        positiveDeficitKzt: number;
        requiredRoleCodes: string[];
        assigneesResolved: boolean;
    } | null;
    canSubmit: boolean;
    blockers: SubmissionPreflightBlocker[];
};
export type BmsAccessProfileCode = 'BUSINESS' | 'SYSTEM_ADMIN' | 'INTEGRATION_ADMIN';
export type SwitchableBmsAccessProfileCode = Exclude<BmsAccessProfileCode, 'INTEGRATION_ADMIN'>;
export type CurrentSession = {
    staffSubstitutionRoles471?: readonly import('./utils/activeRole').StaffRoleChoice471[];
    activeSubstitutionId471?: string;
    ownRoleCodes471?: readonly string[];
    activeRoleCode?: string | null;
    availableRoleCodes?: readonly string[];
    actorExternalId: string;
    /** Display-only name from the active AD-to-employee mapping. */
    actorDisplayName?: string;
    activeProfileCode: BmsAccessProfileCode;
    availableProfileCodes: readonly BmsAccessProfileCode[];
    isBmsAdministrator: boolean;
    isBmsIntegrationAdministrator: boolean;
    canAccessBusinessData: boolean;
    hasFullBusinessDataAccess: boolean;
    canReadBudgetReports?: boolean;
    canReadAllBusinessData?: boolean;
    canManageReferenceData?: boolean;
    canCreateExpenseRequest: boolean;
    canViewBusinessAuditMonitor: boolean;
    /**
     * The antiforgery request token. Absent while the server has the protection
     * switched off, which is the default, so its absence is a normal state and
     * not a failure.
     */
    csrfToken?: string;
};
export type OutboxMonitorItem = {
    messageId: string;
    occurredAtUtc: string;
    messageType: string;
    processedAtUtc: string | null;
    retryCount: number;
    hasLastError: boolean;
};
export type OutboxMonitorPage = {
    items: OutboxMonitorItem[];
    skip: number;
    take: number;
    hasMore: boolean;
};
export type OutboxBacklogSummary = {
    checkedAtUtc: string;
    totalCount: number;
    pendingCount: number;
    processedCount: number;
    retriedPendingCount: number;
    pendingWithLastErrorCount: number;
    maxPendingRetryCount: number;
    oldestPendingOccurredAtUtc: string | null;
};
export type DatabaseReadinessMissingComponent = 'schema-version-table' | 'migration-001' | 'migration-002' | 'migration-003' | 'migration-004' | 'migration-005' | 'migration-006' | 'migration-007' | 'migration-008' | 'migration-009' | 'migration-010' | 'migration-011' | 'migration-012' | 'migration-013' | 'migration-014' | 'migration-015' | 'migration-016' | 'migration-017' | 'migration-018' | 'migration-019' | 'migration-020' | 'migration-021' | 'migration-022' | 'migration-023' | 'migration-024' | 'migration-025' | 'migration-026' | 'migration-027' | 'migration-028' | 'migration-029' | 'migration-030' | 'migration-031' | 'migration-032' | 'migration-033' | 'migration-034' | 'migration-035' | 'migration-036' | 'migration-037' | 'migration-038' | 'migration-039' | 'migration-040' | 'migration-041' | 'migration-042' | 'migration-043' | 'migration-045' | 'migration-046' | 'migration-047' | 'migration-048' | 'migration-049' | 'migration-050' | 'migration-051' | 'migration-052' | 'migration-053' | 'migration-054' | 'cash-flow-display-order-compatibility' | 'identifier-semantics' | 'cash-flow-items' | 'expense-items' | 'expense-request-line-accrual-snapshot' | 'money-precision' | 'counterparties' | 'contracts' | 'budget-projection' | 'employee-functional-roles' | 'currencies-and-rates' | 'expense-request-currency-rate-snapshot' | 'expense-request-line-currency-conversion' | 'expense-request-urgency' | 'overpayment-deficit-storage' | 'treasury-overpayment-storage' | 'budget-owner-cost-center-assignments' | 'approval-route-snapshots' | 'employee-actor-mappings' | 'expense-request-workflow-activation' | 'accrual-closing-cycle-storage' | 'expense-request-closing-activation' | 'accrual-notifications' | 'expense-request-substitutions' | 'expense-request-task-scope-index' | 'expense-request-attachment-storage' | 'budget-instance-storage' | 'budget-instance-line-storage' | 'budget-directory-storage' | 'identity-reference-import-watermark-storage' | 'expense-request-numbering-storage';
export type DatabaseReadinessMonitor = {
    databaseReady: boolean;
    appliedMigrationCount: number;
    requiredMigrationCount: number;
    checkedAtUtc: string;
    missingComponents: DatabaseReadinessMissingComponent[];
};
export type ReferenceProjectionCount = {
    totalCount: number;
    activeCount: number;
};
export type ReferenceProjectionInventory = {
    checkedAtUtc: string;
    employees: ReferenceProjectionCount;
    functionalRoles: ReferenceProjectionCount;
    employeeFunctionalRoles: ReferenceProjectionCount;
    activeAssignmentsWithInactiveEmployeeCount: number;
    activeAssignmentsWithInactiveFunctionalRoleCount: number;
};
export type BusinessAuditMonitorItem = {
    auditId: string;
    occurredAtUtc: string;
    actorExternalId: string;
    objectType: string;
    objectId: string;
    action: string;
    correlationId: string;
    hasBefore: boolean;
    hasAfter: boolean;
};
export type BusinessAuditMonitorPage = {
    items: BusinessAuditMonitorItem[];
    skip: number;
    take: number;
    hasMore: boolean;
};
export type BusinessAuditMonitorDetail = {
    metadata: BusinessAuditMonitorItem;
    kind: ExpenseRequestHistoryKind;
    title: string;
    actorLabel: string;
    detailsStatus: ExpenseRequestHistoryDetailsStatus;
    changes: ExpenseRequestHistoryChange[];
    omittedChanges: number;
};
export type BusinessAuditSearchFilters = {
    actorExternalId?: string;
    objectType?: string;
    objectId?: string;
    action?: string;
    correlationId?: string;
    fromUtc?: string;
    toUtc?: string;
};
export type SavedExpenseRequest = {
    paymentOperation?: import('./utils/expensePaymentOperation').ExpensePaymentOperation | null;
    id: string;
    status: ExpenseRequestStatus;
    requestNumber?: string;
    initiatorExternalId?: string;
    /** Read-only employee name from the authorized detail envelope, not an identity. */
    initiatorDisplayName?: string | null;
    organizationId?: string | null;
    defaultCostCenterId?: string | null;
    plannedPaymentDate?: string | null;
    currencyCode?: string;
    currencyRateDate?: string | null;
    currencyRate?: number | null;
    currencyMultiplicity?: number | null;
    counterpartyId?: string | null;
    contractId?: string | null;
    purpose?: string | null;
    comment?: string | null;
    businessJustification?: string | null;
    isUrgent?: boolean;
    urgencyReason?: string | null;
    isNewCounterparty?: boolean;
    isNewContract?: boolean;
    paymentBasisCode?: PaymentBasisCode | null;
    invoiceNumber?: string | null;
    invoiceDate?: string | null;
    publicOfferName?: string | null;
    publicOfferIdentifier?: string | null;
    otherBasisExplanation?: string | null;
    supportingDocumentMissing?: boolean;
    supportingDocumentMissingExplanation?: string | null;
    candidateCounterpartyName?: string | null;
    candidateResidencyCode?: CounterpartyResidencyCode | null;
    candidateBinIin?: string | null;
    candidateCountry?: string | null;
    candidateForeignRegistrationNumber?: string | null;
    candidateRegistrationNumberMissing?: boolean;
    candidateRegistrationNumberMissingExplanation?: string | null;
    candidateContractNumber?: string | null;
    candidateContractNumberMissing?: boolean;
    candidateContractDate?: string | null;
    candidateContractSubject?: string | null;
    candidateContractTerms?: string | null;
    newContractDocumentMissing?: boolean;
    newContractDocumentMissingExplanation?: string | null;
    totalAmount: number;
    totalAmountKzt: number | null;
    createdAtUtc: string;
    updatedAtUtc: string;
    concurrencyStamp: string;
    lines: SavedExpenseRequestLine[];
    budgetReservation?: BudgetReservationSummary;
    submissionReadiness?: SubmissionReadiness;
    editAuthority?: {
        canEdit: boolean;
    };
};
export type ExpenseRequestPage = {
    items: ExpenseRequestListItem[];
    skip: number;
    take: number;
    total: number;
    businessColumnsIncluded?: true;
    budgetIndicatorsUnavailable?: true;
};
export type ExpenseRequestListBusinessColumns = {
    contractName: string | null;
    paymentBasisCode: string | null;
    obligationAmount: string;
    obligationAmountKzt: string | null;
    paidAmount: string;
    remainingAmount: string;
    fullyPaidDate: string | null;
    closingState: 'notClosed' | 'partiallyClosed' | 'closed';
};
export type ExpenseRequestListItem = Pick<SavedExpenseRequest, 'id' | 'status' | 'requestNumber' | 'initiatorExternalId' | 'initiatorDisplayName' | 'organizationId' | 'defaultCostCenterId' | 'plannedPaymentDate' | 'currencyCode' | 'counterpartyId' | 'purpose' | 'isUrgent' | 'isNewCounterparty' | 'candidateCounterpartyName' | 'totalAmount' | 'totalAmountKzt' | 'createdAtUtc'> & {
    currentStage: ExpenseRequestCurrentStage | null;
    requestDateUtc?: string;
    isOverBudget: boolean | null;
    canOpen: boolean;
    businessColumns?: ExpenseRequestListBusinessColumns;
    /** Authorized business-trip recipients; absent on inaccessible list rows. */
    employeeNames?: readonly string[];
};
export type ExpenseRequestHistoryKind = 'created' | 'updated' | 'workflow' | 'closing' | 'payment' | 'attachment' | 'unsupported';
export type ExpenseRequestHistoryDetailsStatus = 'complete' | 'partial' | 'unavailable';
export type ExpenseRequestHistoryChangeKind = 'added' | 'changed' | 'removed';
export type ExpenseRequestHistoryChange = {
    section: string;
    field: string;
    changeKind: ExpenseRequestHistoryChangeKind;
    before: string | null;
    after: string | null;
};
export type ExpenseRequestHistoryEvent = {
    occurredAtUtc: string;
    kind: ExpenseRequestHistoryKind;
    title: string;
    actorLabel: string;
    detailsStatus: ExpenseRequestHistoryDetailsStatus;
    changes: ExpenseRequestHistoryChange[];
    omittedChanges: number;
    /** Client-only safe placeholder for an otherwise valid future event. */
    requiresClientUpdate?: true;
};
export const CASH_FLOW_ACTIVITY_TYPES = [
    { value: 'Operating', label: 'Операционная деятельность' },
    { value: 'Investing', label: 'Инвестиционная деятельность' },
    { value: 'Financing', label: 'Финансовая деятельность' },
] as const;
export type CashFlowActivityType = typeof CASH_FLOW_ACTIVITY_TYPES[number]['value'];
export const CASH_FLOW_ACTIVITY_TYPE_LABELS: Readonly<Record<CashFlowActivityType, string>> = Object.fromEntries(CASH_FLOW_ACTIVITY_TYPES.map(option => [option.value, option.label])) as Readonly<Record<CashFlowActivityType, string>>;
export type ExpenseRequestHistoryPage = {
    items: ExpenseRequestHistoryEvent[];
    skip: number;
    take: number;
    total: number;
};
export type ReferenceDataItem = {
    currencyCode?: string | null;
    /** Persistent catalogue presentation rank; never used in financial calculations. */
    displayOrder?: number | null;
    id: string;
    code: string;
    name: string;
    bin?: string | null;
    /** Verified import provenance; descriptive only, never a matching key. */
    sourceNames?: string[];
    numericCode?: string | null;
    parentId?: string | null;
    parentCode?: string | null;
    parentName?: string | null;
    groupingName?: string | null;
    ownerNames?: string[];
    flowDirection?: 'Payment' | 'Receipt' | null;
    activityType?: CashFlowActivityType | null;
    /** Current SQL settings, projected only by the cash-flow catalogue window. */
    formsManagerialAccrual?: boolean;
    accrualExpenseItemName?: string | null;
    /** Reverse current DDS mappings, joined by IDs on the server. */
    relatedCashFlowItems?: {
        code: string;
        name: string;
    }[];
    costCenterId?: string | null;
    costCenterName?: string | null;
};
/// Public, identifier-free view of one active budget-owner assignment.
export type BudgetOwnerCostCenterAssignment = {
    employeeCode: string;
    employeeName: string;
    costCenterCode: string;
    costCenterName: string;
};
export type BudgetOwnerCostCenterAssignmentDirectory = {
    catalog: 'budget-owner-cost-center-assignments';
    source: string;
    refreshedAtUtc: null;
    items: BudgetOwnerCostCenterAssignment[];
};
export type ReferenceDataCatalog = {
    catalog: string;
    source: string;
    refreshedAtUtc: string | null;
    items: ReferenceDataItem[];
};
/// One page of a catalogue. `total` counts every row the same filter selects,
/// which is what tells the caller whether another page exists.
export type ReferenceDataPage = {
    items: ReferenceDataItem[];
    total: number;
    skip: number;
    take: number;
};
export type ReferenceCodeItem = {
    code: string;
    name: string;
};
export type ReferenceCodeCatalog = {
    catalog: string;
    source: string;
    refreshedAtUtc: string | null;
    items: ReferenceCodeItem[];
};
