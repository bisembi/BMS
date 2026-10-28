export type ManualDeficitCorrectionState = 'PendingApproval' | 'ApprovedAndApplied' | 'Rejected' | 'Annulled';
export type ManualDeficit = {
    id: string;
    organizationName: string;
    counterpartyName: string;
    currencyCode: string;
    occurredAtUtc: string;
    originalAmount: number;
    originalAmountKzt: number;
    currentAmount: number;
    currentAmountKzt: number;
    correctionCount: number;
    hasPendingCorrection: boolean;
    canPropose: boolean;
    canDecide: boolean;
};
export type ManualDeficitCorrection = {
    id: string;
    deficitId: string;
    currencyCode: string;
    state: ManualDeficitCorrectionState;
    amount: number;
    amountKzt: number;
    deficitAmountAtCreation: number;
    deficitAmountKztAtCreation: number;
    basis: string;
    decidedAtUtc: string | null;
    decisionComment: string | null;
    rejectionReason: string | null;
};
export type ManualDeficitQuery = {
    organizationId?: string;
    includeSettled: boolean;
    onlyPending: boolean;
    skip: number;
    take: number;
};
export type ManualDeficitPage = {
    skip: number;
    take: number;
    total: number;
    items: ManualDeficit[];
};
export type ManualDeficitDetail = {
    deficit: ManualDeficit;
    skip: number;
    take: number;
    total: number;
    corrections: ManualDeficitCorrection[];
};
export type ManualDeficitAttempt = {
    operation: 'create';
    deficitId: string;
    amount: number;
    basis: string;
} | {
    operation: 'approve';
    deficitId: string;
    correctionId: string;
    comment: string;
} | {
    operation: 'reject';
    deficitId: string;
    correctionId: string;
    reason: string;
    comment: string;
};
export type ManualDeficitExecution = {
    correction: ManualDeficitCorrection;
    recovered: boolean;
};
