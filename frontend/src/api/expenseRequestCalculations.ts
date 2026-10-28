/// Pure expense request arithmetic: canonicalisation, money parsing, VAT and
/// KZT totals, submission readiness and budget reservation. Nothing here may
/// depend on fetch, React or the DOM.
import type { BudgetReservationSummary, ExpenseRequestDraft, SavedExpenseRequestLine, SubmissionReadiness, VatRateCode } from '../types';
import { isBudgetTaxKind } from '../utils/expensePaymentOperation';
export function nullableGuid(value: string): string | null {
    return value.trim() === '' ? null : value;
}
export function nullableText(value: string): string | null {
    const normalized = value.trim();
    return normalized === '' ? null : normalized;
}
export function canonicalizeExpenseRequestDraft(draft: ExpenseRequestDraft): ExpenseRequestDraft {
    const isNewCounterparty = draft.isNewCounterparty;
    const isResident = isNewCounterparty && draft.candidateResidencyCode === 'ResidentKz';
    const isNonResident = isNewCounterparty && draft.candidateResidencyCode === 'NonResident';
    const registrationNumberMissing = isNonResident && draft.candidateRegistrationNumberMissing;
    const isContractBasis = draft.paymentBasisCode === 'Contract';
    const isNewContract = isContractBasis && draft.isNewContract;
    const contractNumberMissing = isNewContract && draft.candidateContractNumberMissing;
    const newContractDocumentMissing = isNewContract && draft.newContractDocumentMissing;
    return {
        ...draft,
        justification: draft.paymentOperation?.kind === 'PayrollAdvance' && draft.justification.trim() === ''
            ? 'Выплата аванса' : draft.justification,
        ...(draft.paymentOperation ? { paymentOperation: Object.fromEntries(Object.entries(draft.paymentOperation).map(([k, v]) => [k, k === 'employeeNames' && Array.isArray(v) ? v.map(name => name.trim().normalize()) : typeof v === 'string' ? (v.trim() || null) : v])) as typeof draft.paymentOperation } : {}),
        lines: draft.lines.map(line => line.costCenterId.trim() === '' && draft.defaultCostCenterId.trim() !== ''
            ? { ...line, costCenterId: draft.defaultCostCenterId } : line),
        counterpartyId: isNewCounterparty ? '' : draft.counterpartyId,
        candidateCounterpartyName: isNewCounterparty ? draft.candidateCounterpartyName : '',
        candidateResidencyCode: isNewCounterparty ? draft.candidateResidencyCode : '',
        candidateBinIin: isResident ? draft.candidateBinIin : '',
        candidateCountry: isNonResident ? draft.candidateCountry : '',
        candidateForeignRegistrationNumber: isNonResident && !registrationNumberMissing
            ? draft.candidateForeignRegistrationNumber
            : '',
        candidateRegistrationNumberMissing: registrationNumberMissing,
        candidateRegistrationNumberMissingExplanation: registrationNumberMissing
            ? draft.candidateRegistrationNumberMissingExplanation
            : '',
        contractId: isContractBasis && !isNewContract ? draft.contractId : '',
        isNewContract,
        invoiceNumber: draft.paymentBasisCode === 'InvoiceWithoutContract' ? draft.invoiceNumber : '',
        invoiceDate: draft.paymentBasisCode === 'InvoiceWithoutContract' ? draft.invoiceDate : '',
        publicOfferName: draft.paymentBasisCode === 'PublicOffer' ? draft.publicOfferName : '',
        publicOfferIdentifier: draft.paymentBasisCode === 'PublicOffer' ? draft.publicOfferIdentifier : '',
        otherBasisExplanation: draft.paymentBasisCode === 'OtherBasis' ? draft.otherBasisExplanation : '',
        supportingDocumentMissingExplanation: draft.supportingDocumentMissing
            ? draft.supportingDocumentMissingExplanation
            : '',
        candidateContractNumber: isNewContract && !contractNumberMissing ? draft.candidateContractNumber : '',
        candidateContractNumberMissing: contractNumberMissing,
        candidateContractDate: isNewContract ? draft.candidateContractDate : '',
        candidateContractSubject: isNewContract ? draft.candidateContractSubject : '',
        candidateContractTerms: isNewContract ? draft.candidateContractTerms : '',
        newContractDocumentMissing,
        newContractDocumentMissingExplanation: newContractDocumentMissing
            ? draft.newContractDocumentMissingExplanation
            : '',
    };
}
export function roundMoney(value: number): number {
    return Math.round((value + Number.EPSILON) * 100) / 100;
}
const maximumExactBrowserMoneyCents = BigInt(Number.MAX_SAFE_INTEGER);
function serializedMoneyCents(value: number): bigint | null {
    const serialized = JSON.stringify(value);
    const match = /^(?<whole>\d+)(?:\.(?<fraction>\d{1,2}))?$/.exec(serialized);
    if (!match?.groups)
        return null;
    const fraction = (match.groups.fraction ?? '').padEnd(2, '0');
    return BigInt(match.groups.whole) * 100n + BigInt(fraction || '0');
}
export function isValidPositiveMoneyInput(value: string): boolean {
    const match = /^(?<whole>\d{1,16})(?:\.(?<fraction>\d{1,2}))?$/.exec(value);
    if (!match?.groups)
        return false;
    const fraction = (match.groups.fraction ?? '').padEnd(2, '0');
    const cents = BigInt(match.groups.whole) * 100n + BigInt(fraction || '0');
    if (cents <= 0n || cents > maximumExactBrowserMoneyCents)
        return false;
    const parsed = Number(value);
    return Number.isFinite(parsed) && serializedMoneyCents(parsed) === cents;
}
export function parsePositiveMoneyInput(value: string): number {
    if (!isValidPositiveMoneyInput(value)) {
        throw new Error('Сумма должна быть положительной, иметь не более двух знаков после запятой ' +
            'и точно представляться браузером.');
    }
    return Number(value);
}
export function calculateLineAmounts(amount: string | number, vatRateCode: VatRateCode): {
    vatAmount: number;
    amountExcludingVat: number;
    amountKzt: number;
} {
    const parsedAmount = typeof amount === 'number' ? amount : Number(amount);
    const amountKzt = Number.isFinite(parsedAmount) ? parsedAmount : 0;
    const vatAmount = vatRateCode === 'Vat16'
        ? roundMoney(amountKzt * 16 / 116)
        : 0;
    return {
        vatAmount,
        amountExcludingVat: amountKzt - vatAmount,
        amountKzt,
    };
}
export function materialLines(draft: ExpenseRequestDraft) {
    return draft.lines.filter((line) => line.amount.trim() !== '');
}
function missingBudgetRequirements(draft: ExpenseRequestDraft): string[] {
    const lines = materialLines(draft);
    const missing: string[] = [];
    if (draft.organizationId.trim() === '')
        missing.push('OrganizationId');
    if (draft.defaultCostCenterId.trim() === '')
        missing.push('DefaultCostCenterId');
    if (draft.plannedPaymentDate.trim() === '')
        missing.push('PlannedPaymentDate');
    if (draft.currencyCode !== 'KZT')
        missing.push('CurrencyCode.KZT');
    if (draft.justification.trim() === '' && draft.paymentOperation?.kind !== 'PayrollAdvance')
        missing.push('BusinessJustification');
    if (draft.isUrgent && draft.urgencyReason.trim() === '')
        missing.push('UrgencyReason');
    if (lines.length === 0)
        missing.push('Lines');
    lines.forEach((line, index) => {
        const lineNumber = index + 1;
        if (line.cashFlowItemId.trim() === '' && draft.paymentOperation?.kind !== 'PayrollAdvance')
            missing.push(`Lines[${lineNumber}].CashFlowItemId`);
        if (line.costCenterId.trim() === '' && draft.defaultCostCenterId.trim() === '')
            missing.push(`Lines[${lineNumber}].CostCenterId`);
        if (!isValidPositiveMoneyInput(line.amount))
            missing.push(`Lines[${lineNumber}].Amount`);
        if (line.vatRateCode !== 'Vat16' && line.vatRateCode !== 'WithoutVat')
            missing.push(`Lines[${lineNumber}].VatRateCode`);
    });
    return missing;
}
export function calculateSubmissionReadiness(draft: ExpenseRequestDraft): SubmissionReadiness {
    const missing = missingBudgetRequirements(draft);
    const op = draft.paymentOperation;
    if (op) {
        const required = (key: keyof typeof op) => {
            if (op[key] == null || op[key] === '')
                missing.push('PaymentOperation.' + key[0].toUpperCase() + key.slice(1));
        };
        const tax = op.kind === 'Tax', bank = op.kind !== 'PayrollAdvance' && !tax && op.transferMethod === 'Bank';
        if (op.kind === 'PayrollAdvance') {
            required('period');
            required('employeeName');
            required('advanceAmount');
            if (!op.advanceAmount || op.advanceAmount <= 0)
                missing.push('PaymentOperation.AdvanceAmount');
        }
        if (tax) {
            required('paymentKind');
            required('period');
            if (draft.currencyCode !== 'KZT')
                missing.push('PaymentOperation.TaxCurrencyKzt');
            if (isBudgetTaxKind(op.paymentKind)) {
                required('taxAuthorityName');
                required('taxAuthorityCode');
                required('budgetCode');
            }
        }
        else if (op.kind !== 'BusinessTrip')
            required('transferMethod');
        if (op.kind === 'BusinessTrip' && (!op.employeeNames?.length || op.employeeNames.some(name => !name.trim())))
            missing.push('PaymentOperation.EmployeeNames');
        if (op.kind === 'Payroll') {
            required('period');
            required('payrollMode');
        }
        if (op.kind === 'Accountable' || (op.kind === 'Payroll' && op.payrollMode === 'Employee')) {
            required('employeeName');
            required('employeeIin');
        }
        if (op.kind === 'Accountable') {
            required('advancePurpose');
            required('advanceReportDueDate');
        }
        if ((tax && !isBudgetTaxKind(op.paymentKind)) || (op.kind === 'Payroll' && op.payrollMode === 'Register')) {
            required('registerNumber');
            required('registerDate');
            required('registerAmount');
            missing.push('PaymentOperation.RegisterDocument');
            if (op.registerAmount && Math.abs(op.registerAmount - draft.lines.reduce((sum, l) => sum + Number(l.amount.replace(',', '.')), 0)) > 0.005)
                missing.push('PaymentOperation.RegisterTotalMismatch');
        }
        if (tax || bank)
            required('paymentCode');
        if (bank) {
            required('recipientName');
            required('recipientBinIin');
            required('recipientIban');
            required('recipientBik');
            required('recipientKbe');
        }
        if (draft.lines.some(l => l.vatRateCode !== 'WithoutVat'))
            missing.push('PaymentOperation.WithoutVat');
        if (draft.supportingDocumentMissing) {
            if (!draft.supportingDocumentMissingExplanation.trim())
                missing.push('SupportingDocumentMissingExplanation');
        }
        else
            missing.push('SupportingDocumentOrMissingFlag');
        const missingRequirements = [...new Set(missing)];
        return { isReady: !missingRequirements.length, missingRequirements };
    }
    if (draft.counterpartyId.trim() === '' && !draft.isNewCounterparty) {
        missing.push('CounterpartyRequirementUnresolved');
    }
    if (draft.candidateRegistrationNumberMissing
        && (!draft.isNewCounterparty || draft.candidateResidencyCode !== 'NonResident')) {
        missing.push('CandidateRegistrationNumberMissing.NonResidentOnly');
    }
    if (draft.isNewCounterparty) {
        if (draft.candidateCounterpartyName.trim() === '')
            missing.push('CandidateCounterpartyName');
        if (draft.candidateResidencyCode === '')
            missing.push('CandidateResidencyCode');
        if (draft.candidateResidencyCode === 'ResidentKz' && draft.candidateBinIin.trim() === '') {
            missing.push('CandidateBinIin');
        }
        if (draft.candidateResidencyCode === 'NonResident') {
            if (draft.candidateCountry.trim() === '')
                missing.push('CandidateCountry');
            if (draft.candidateRegistrationNumberMissing) {
                if (draft.candidateRegistrationNumberMissingExplanation.trim() === '') {
                    missing.push('CandidateRegistrationNumberMissingExplanation');
                }
            }
            else if (draft.candidateForeignRegistrationNumber.trim() === '') {
                missing.push('CandidateForeignRegistrationNumber');
            }
        }
    }
    if (draft.paymentBasisCode === '') {
        missing.push('PaymentBasisCode');
    }
    else if (draft.paymentBasisCode === 'Contract') {
        if (!draft.isNewContract && draft.contractId.trim() === '') {
            missing.push('ContractIdOrIsNewContract');
        }
    }
    else if (draft.paymentBasisCode === 'InvoiceWithoutContract') {
        if (draft.invoiceNumber.trim() === '')
            missing.push('InvoiceNumber');
        if (draft.invoiceDate.trim() === '')
            missing.push('InvoiceDate');
    }
    else if (draft.paymentBasisCode === 'PublicOffer') {
        if (draft.publicOfferName.trim() === '')
            missing.push('PublicOfferName');
        if (draft.publicOfferIdentifier.trim() === '')
            missing.push('PublicOfferIdentifier');
    }
    else if (draft.otherBasisExplanation.trim() === '') {
        missing.push('OtherBasisExplanation');
    }
    if (draft.isNewContract) {
        if (draft.paymentBasisCode !== '' && draft.paymentBasisCode !== 'Contract') {
            missing.push('IsNewContract.ContractBasisOnly');
        }
        if (!draft.candidateContractNumberMissing && draft.candidateContractNumber.trim() === '') {
            missing.push('CandidateContractNumberOrMissingFlag');
        }
        if (draft.candidateContractDate.trim() === '')
            missing.push('CandidateContractDate');
        if (draft.candidateContractSubject.trim() === '')
            missing.push('CandidateContractSubject');
        if (draft.newContractDocumentMissing) {
            if (draft.newContractDocumentMissingExplanation.trim() === '') {
                missing.push('NewContractDocumentMissingExplanation');
            }
        }
        else {
            missing.push('NewContractDocumentOrMissingFlag');
        }
    }
    else {
        if (draft.candidateContractNumberMissing) {
            missing.push('CandidateContractNumberMissing.NewContractOnly');
        }
        if (draft.newContractDocumentMissing) {
            missing.push('NewContractDocumentMissing.NewContractOnly');
        }
    }
    if (draft.supportingDocumentMissing) {
        if (draft.supportingDocumentMissingExplanation.trim() === '') {
            missing.push('SupportingDocumentMissingExplanation');
        }
    }
    else {
        // W08 has no attachment storage/count contract yet, so a saved file cannot be asserted.
        missing.push('SupportingDocumentOrMissingFlag');
    }
    const missingRequirements = [...new Set(missing)];
    return { isReady: missingRequirements.length === 0, missingRequirements };
}
export function calculateBudgetReservation(draft: ExpenseRequestDraft, savedLines: SavedExpenseRequestLine[]): BudgetReservationSummary {
    type CompleteBudgetPositionLine = SavedExpenseRequestLine & {
        cashFlowItemId: string;
        costCenterId: string;
        amountKzt: number;
    };
    const completePositionLines = savedLines.filter((line): line is CompleteBudgetPositionLine => line.cashFlowItemId !== null
        && line.costCenterId !== null
        && line.amountKzt !== null);
    const missingRequirements = missingBudgetRequirements(draft);
    if (savedLines.some((line) => line.cashFlowItemId === null || line.costCenterId === null)) {
        missingRequirements.push('BudgetReservation.PositionFacts');
    }
    if (savedLines.some((line) => line.amountKzt === null)) {
        missingRequirements.push('CurrencyRate');
    }
    if (missingRequirements.length > 0) {
        return {
            isComplete: false,
            reservedAmountKzt: 0,
            missingRequirements: [...new Set(missingRequirements)],
            positions: [],
        };
    }
    const positions = completePositionLines.map((line) => ({
        expenseRequestLineId: line.id,
        cashFlowItemId: line.cashFlowItemId,
        costCenterId: line.costCenterId,
        budgetMonth: `${draft.plannedPaymentDate.slice(0, 7)}-01`,
        reservedAmountKzt: line.amountKzt,
        limitAmountKzt: null,
        availableAmountKzt: null,
    }));
    return {
        isComplete: true,
        reservedAmountKzt: positions.reduce((sum, position) => sum + position.reservedAmountKzt, 0),
        missingRequirements: [],
        positions,
    };
}
