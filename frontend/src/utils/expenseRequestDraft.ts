import { canonicalizeExpenseRequestDraft, type getBudgetPositionsForRequest } from '../api';
import type { BudgetReservationSummary, ExpenseLineDraft, ExpenseRequestDraft, PreliminaryCurrencyCalculation, SavedExpenseRequest } from '../types';
import { toBusinessDateInput } from './businessDate';
/// Pure draft and line transformations lifted out of ExpenseRequestFormPage.
/// No React state, no requests, no DOM.
let nextClientRowNumber = 0;
export const accrualPeriodFromPlannedPaymentDate = (plannedPaymentDate: string): string => {
    const canonicalDate = plannedPaymentDate.trim();
    const match = /^(\d{4})-(\d{2})-(\d{2})$/u.exec(canonicalDate);
    if (!match)
        return '';
    const parsed = new Date(`${canonicalDate}T00:00:00Z`);
    return !Number.isNaN(parsed.valueOf()) && parsed.toISOString().slice(0, 10) === canonicalDate
        ? `${match[1]}-${match[2]}-01`
        : '';
};
export const emptyLine = (defaultCostCenterId = '', defaultAccrualPeriod = '', withoutVat = false): ExpenseLineDraft => ({
    clientRowKey: `local-row-${++nextClientRowNumber}`,
    cashFlowItemId: '',
    costCenterId: defaultCostCenterId,
    accrualPeriod: defaultAccrualPeriod,
    amount: '',
    vatRateCode: withoutVat ? 'WithoutVat' : 'Vat16',
});
export const initialDraft = (): ExpenseRequestDraft => {
    const plannedPaymentDate = toBusinessDateInput();
    return {
        organizationId: '',
        defaultCostCenterId: '',
        plannedPaymentDate,
        currencyCode: 'KZT',
        counterpartyId: '',
        contractId: '',
        purpose: '',
        comment: '',
        justification: '',
        isUrgent: false,
        urgencyReason: '',
        isNewCounterparty: false,
        isNewContract: false,
        paymentBasisCode: '',
        invoiceNumber: '',
        invoiceDate: '',
        publicOfferName: '',
        publicOfferIdentifier: '',
        otherBasisExplanation: '',
        supportingDocumentMissing: false,
        supportingDocumentMissingExplanation: '',
        candidateCounterpartyName: '',
        candidateResidencyCode: '',
        candidateBinIin: '',
        candidateCountry: '',
        candidateForeignRegistrationNumber: '',
        candidateRegistrationNumberMissing: false,
        candidateRegistrationNumberMissingExplanation: '',
        candidateContractNumber: '',
        candidateContractNumberMissing: false,
        candidateContractDate: '',
        candidateContractSubject: '',
        candidateContractTerms: '',
        newContractDocumentMissing: false,
        newContractDocumentMissingExplanation: '',
        lines: [emptyLine('', accrualPeriodFromPlannedPaymentDate(plannedPaymentDate))],
    };
};
export function draftSignature(draft: ExpenseRequestDraft): string {
    const normalized = canonicalizeExpenseRequestDraft(draft);
    return JSON.stringify({
        ...normalized,
        lines: normalized.lines.filter((line) => !(line.id == null
            && line.cashFlowItemId.trim() === ''
            && (!line.accrualCostCenterId || sameOpaqueIdentifier(line.accrualCostCenterId, normalized.defaultCostCenterId))
            && (line.costCenterId.trim() === ''
                || sameOpaqueIdentifier(line.costCenterId, normalized.defaultCostCenterId))
            && (line.accrualPeriod.trim() === ''
                || line.accrualPeriod === accrualPeriodFromPlannedPaymentDate(normalized.plannedPaymentDate))
            && line.amount.trim() === ''
            && line.vatRateCode === 'Vat16')).map(({ clientRowKey: _clientRowKey, ...line }) => line),
    });
}
export function sameOpaqueIdentifier(left: unknown, right: string): boolean {
    return typeof left === 'string'
        && left.toLocaleLowerCase('en-US') === right.toLocaleLowerCase('en-US');
}
function opaqueIdentifierKey(value: string): string {
    return value.toLocaleLowerCase('en-US');
}
export function opaqueReferenceOptionValue(itemId: string, selectedId: string): string {
    return selectedId !== '' && sameOpaqueIdentifier(itemId, selectedId) ? selectedId : itemId;
}
export function formatMoney(value: number): string {
    return new Intl.NumberFormat('ru-RU', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(value);
}
/** Keep editable money values canonical while showing grouped digits to users. */
export { normalizeEditableMoney as normalizeMoneyInput, formatEditableMoney as formatMoneyInput } from './moneyInput';
export function formatBudgetMonth(value: string): string {
    const [year, month] = value.slice(0, 7).split('-');
    return year && month ? `${month}.${year}` : value;
}
function lineBusinessFieldsEqual(left: ExpenseLineDraft, right: ExpenseLineDraft): boolean {
    return sameOpaqueIdentifier(left.cashFlowItemId, right.cashFlowItemId)
        && sameOpaqueIdentifier(left.costCenterId, right.costCenterId)
        && sameOpaqueIdentifier(left.accrualCostCenterId ?? left.costCenterId, right.accrualCostCenterId ?? right.costCenterId)
        && left.accrualPeriod === right.accrualPeriod
        && left.amount === right.amount
        && left.vatRateCode === right.vatRateCode;
}
function hasClientRowKey(line: ExpenseLineDraft): line is ExpenseLineDraft & {
    clientRowKey: string;
} {
    return typeof line.clientRowKey === 'string' && line.clientRowKey !== '';
}
function hasSavedLineId(line: ExpenseLineDraft): line is ExpenseLineDraft & {
    id: string;
} {
    return typeof line.id === 'string' && line.id !== '';
}
export function mergeCurrentLineOntoSaved(current: ExpenseLineDraft, submitted: ExpenseLineDraft, authoritative: ExpenseLineDraft): ExpenseLineDraft {
    return {
        ...authoritative,
        ...(!sameOpaqueIdentifier(current.accrualCostCenterId ?? current.costCenterId, submitted.accrualCostCenterId ?? submitted.costCenterId)
            ? { accrualCostCenterId: current.accrualCostCenterId ?? current.costCenterId } : {}),
        cashFlowItemId: !sameOpaqueIdentifier(current.cashFlowItemId, submitted.cashFlowItemId)
            ? current.cashFlowItemId
            : authoritative.cashFlowItemId,
        costCenterId: !sameOpaqueIdentifier(current.costCenterId, submitted.costCenterId)
            ? current.costCenterId
            : authoritative.costCenterId,
        accrualPeriod: current.accrualPeriod !== submitted.accrualPeriod
            ? current.accrualPeriod
            : authoritative.accrualPeriod,
        amount: current.amount !== submitted.amount ? current.amount : authoritative.amount,
        vatRateCode: current.vatRateCode !== submitted.vatRateCode
            ? current.vatRateCode
            : authoritative.vatRateCode,
    };
}
export function reconcileSavedLines(submitted: ExpenseRequestDraft, current: ExpenseRequestDraft, saved: SavedExpenseRequest): ExpenseRequestDraft {
    const authoritativeDraft = draftFromSaved(saved);
    const submittedMaterial = submitted.lines.filter((line) => line.amount.trim() !== '');
    const submittedAllIds = new Set(submitted.lines.flatMap((line) => line.id ? [opaqueIdentifierKey(line.id)] : []));
    const submittedExistingIds = new Set(submittedMaterial.flatMap((line) => line.id ? [opaqueIdentifierKey(line.id)] : []));
    const savedNewLines = saved.lines.filter((line) => !submittedExistingIds.has(opaqueIdentifierKey(line.id)));
    const submittedForSavedId = new Map<string, ExpenseLineDraft>();
    let nextSavedNewLine = 0;
    for (const line of submittedMaterial) {
        if (line.id) {
            const lineKey = opaqueIdentifierKey(line.id);
            if (saved.lines.some((savedLine) => opaqueIdentifierKey(savedLine.id) === lineKey)) {
                submittedForSavedId.set(lineKey, line);
            }
            continue;
        }
        const savedLine = savedNewLines[nextSavedNewLine++];
        if (savedLine)
            submittedForSavedId.set(opaqueIdentifierKey(savedLine.id), line);
    }
    const currentByClientRowKey = new Map(current.lines
        .filter(hasClientRowKey)
        .map((line) => [line.clientRowKey, line]));
    const currentById = new Map(current.lines
        .filter(hasSavedLineId)
        .map((line) => [opaqueIdentifierKey(line.id), line]));
    const unmatchedCurrent = new Set(current.lines);
    const savedIds = new Set(saved.lines.map((line) => opaqueIdentifierKey(line.id)));
    const reconciledLines: ExpenseLineDraft[] = [];
    for (const savedLine of saved.lines) {
        const authoritative = lineDraftFromSaved(savedLine);
        const submittedLine = submittedForSavedId.get(opaqueIdentifierKey(savedLine.id));
        if (!submittedLine) {
            reconciledLines.push(authoritative);
            continue;
        }
        const currentLine = submittedLine.clientRowKey
            ? currentByClientRowKey.get(submittedLine.clientRowKey)
            : submittedLine.id
                ? currentById.get(submittedLine.id)
                : undefined;
        if (!currentLine) {
            // A row deleted while the request was in flight must stay deleted locally.
            continue;
        }
        unmatchedCurrent.delete(currentLine);
        reconciledLines.push(lineBusinessFieldsEqual(currentLine, submittedLine)
            ? authoritative
            : mergeCurrentLineOntoSaved(currentLine, submittedLine, authoritative));
    }
    // Rows not present in the submitted command are local-only and stay at the tail.
    for (const line of current.lines) {
        if (!unmatchedCurrent.has(line))
            continue;
        reconciledLines.push(line.id
            && submittedAllIds.has(opaqueIdentifierKey(line.id))
            && !savedIds.has(opaqueIdentifierKey(line.id))
            ? { ...line, id: undefined }
            : line);
    }
    const reconciledDraft = { ...authoritativeDraft };
    const submittedHeaderValues = new Map(Object.entries(submitted).filter(([key]) => key !== 'lines'));
    for (const [key, value] of Object.entries(current)) {
        if (key === 'lines')
            continue;
        if (value !== submittedHeaderValues.get(key)) {
            Object.assign(reconciledDraft, { [key]: value });
        }
    }
    reconciledDraft.lines = reconciledLines.length > 0
        ? reconciledLines
        : [emptyLine(reconciledDraft.defaultCostCenterId, accrualPeriodFromPlannedPaymentDate(reconciledDraft.plannedPaymentDate))];
    return reconciledDraft;
}
export function lineDraftFromSaved(line: SavedExpenseRequest['lines'][number]): ExpenseLineDraft {
    return {
        clientRowKey: `saved-row:${line.id}`,
        id: line.id,
        cashFlowItemId: line.cashFlowItemId ?? '',
        costCenterId: line.costCenterId ?? '',
        ...(line.accrualCostCenterId ? { accrualCostCenterId: line.accrualCostCenterId } : {}),
        accrualPeriod: line.accrualPeriod ?? '',
        amount: String(line.amount),
        vatRateCode: line.vatRateCode ?? 'Vat16',
    };
}
export function draftFromSaved(saved: SavedExpenseRequest): ExpenseRequestDraft {
    return {
        ...(saved.paymentOperation ? { paymentOperation: saved.paymentOperation } : {}),
        organizationId: saved.organizationId ?? '',
        defaultCostCenterId: saved.defaultCostCenterId ?? '',
        plannedPaymentDate: saved.plannedPaymentDate ?? '',
        currencyCode: saved.currencyCode ?? 'KZT',
        counterpartyId: saved.counterpartyId ?? '',
        contractId: saved.contractId ?? '',
        purpose: saved.purpose ?? '',
        comment: saved.comment ?? '',
        justification: saved.businessJustification ?? '',
        isUrgent: saved.isUrgent ?? false,
        urgencyReason: saved.urgencyReason ?? '',
        isNewCounterparty: saved.isNewCounterparty ?? false,
        isNewContract: saved.isNewContract ?? false,
        paymentBasisCode: saved.paymentBasisCode ?? '',
        invoiceNumber: saved.invoiceNumber ?? '',
        invoiceDate: saved.invoiceDate ?? '',
        publicOfferName: saved.publicOfferName ?? '',
        publicOfferIdentifier: saved.publicOfferIdentifier ?? '',
        otherBasisExplanation: saved.otherBasisExplanation ?? '',
        supportingDocumentMissing: saved.supportingDocumentMissing ?? false,
        supportingDocumentMissingExplanation: saved.supportingDocumentMissingExplanation ?? '',
        candidateCounterpartyName: saved.candidateCounterpartyName ?? '',
        candidateResidencyCode: saved.candidateResidencyCode ?? '',
        candidateBinIin: saved.candidateBinIin ?? '',
        candidateCountry: saved.candidateCountry ?? '',
        candidateForeignRegistrationNumber: saved.candidateForeignRegistrationNumber ?? '',
        candidateRegistrationNumberMissing: saved.candidateRegistrationNumberMissing ?? false,
        candidateRegistrationNumberMissingExplanation: saved.candidateRegistrationNumberMissingExplanation ?? '',
        candidateContractNumber: saved.candidateContractNumber ?? '',
        candidateContractNumberMissing: saved.candidateContractNumberMissing ?? false,
        candidateContractDate: saved.candidateContractDate ?? '',
        candidateContractSubject: saved.candidateContractSubject ?? '',
        candidateContractTerms: saved.candidateContractTerms ?? '',
        newContractDocumentMissing: saved.newContractDocumentMissing ?? false,
        newContractDocumentMissingExplanation: saved.newContractDocumentMissingExplanation ?? '',
        lines: saved.lines.length > 0
            ? saved.lines.map(lineDraftFromSaved)
            : [emptyLine(saved.defaultCostCenterId ?? '', accrualPeriodFromPlannedPaymentDate(saved.plannedPaymentDate ?? ''))],
    };
}
export function preliminaryCurrencyCalculationFromSaved(saved: SavedExpenseRequest): PreliminaryCurrencyCalculation {
    return {
        currencyCode: saved.currencyCode ?? 'KZT',
        rateDate: saved.currencyRateDate ?? null,
        rate: saved.currencyRate ?? null,
        multiplicity: saved.currencyMultiplicity ?? null,
        totalAmountKzt: saved.totalAmountKzt ?? null,
    };
}
export function enrichBudgetReservation(reservation: BudgetReservationSummary | undefined, aggregatePositions: Awaited<ReturnType<typeof getBudgetPositionsForRequest>>['items']): BudgetReservationSummary | null {
    if (!reservation) {
        return null;
    }
    const aggregatesByKey = new Map(aggregatePositions.map((position) => [
        `${opaqueIdentifierKey(position.costCenterId)}:${opaqueIdentifierKey(position.cashFlowItemId)}:${position.budgetMonth}`,
        position,
    ]));
    return {
        ...reservation,
        positions: reservation.positions.map((position) => {
            const aggregate = aggregatesByKey.get(`${opaqueIdentifierKey(position.costCenterId)}:${opaqueIdentifierKey(position.cashFlowItemId)}:${position.budgetMonth}`);
            return aggregate
                ? {
                    ...position,
                    limitAmountKzt: aggregate.limitAmountKzt,
                    availableAmountKzt: aggregate.availableAmountKzt,
                    paidAmountKzt: aggregate.paidAmountKzt,
                    isLimitConfigured: aggregate.isLimitConfigured,
                    isOverLimit: aggregate.isOverLimit,
                }
                : position;
        }),
    };
}
