import { isPaymentOperation } from '../utils/expensePaymentOperation';
import type { BudgetReservationPosition, BudgetReservationSummary, SavedExpenseRequest, SavedExpenseRequestLine, SubmissionReadiness } from '../types';
import { isExpenseRequestStatus } from '../expenseRequestStatus';
import { safeSubmissionReadinessRequirementList } from './submissionReadinessRequirements';
const invalidResponseMessage = 'Некорректный ответ API расходной заявки.';
const controlCharacterPattern = /[\u0000-\u001f\u007f-\u009f]/u;
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
const currencyPattern = /^[A-Z]{3}$/u;
const requestNumberPattern = /^[\p{L}][\p{L}\p{N}]*(?:-[\p{L}\p{N}]+)*-\d{5,}$/u;
const maximumMoney = 90071992547409.9;
const maximumCurrencyRate = 999999999.999999;
const maximumCurrencyMultiplicity = 9999999999;
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function isUuid(value: unknown): value is string {
    return typeof value === 'string'
        && value !== '00000000-0000-0000-0000-000000000000'
        && uuidPattern.test(value);
}
function isOptionalUuid(value: unknown): value is string | null | undefined {
    return value === undefined || value === null || isUuid(value);
}
function isPublicText(value: unknown, maximumLength: number): value is string {
    return typeof value === 'string'
        && value.length > 0
        && value.length <= maximumLength
        && value.trim() === value
        && !controlCharacterPattern.test(value);
}
function isOptionalPublicText(value: unknown, maximumLength: number): value is string | null | undefined {
    return value === undefined || value === null || isPublicText(value, maximumLength);
}
// Textarea contents may contain line breaks. Validate without altering the
// returned business value; all other controls and single-line fields stay strict.
export function isPublicExpenseRequestMultilineText(value: unknown, maximumLength: number): value is string {
    return typeof value === 'string' && value.length <= maximumLength
        && isPublicText(value.replace(/[\r\n]/gu, ' '), maximumLength);
}
export function isOptionalPublicExpenseRequestNumber(value: unknown): value is string | undefined {
    return value === undefined
        || (typeof value === 'string'
            && value.length > 0
            && value.length <= 160
            && value.trim() === value
            && !controlCharacterPattern.test(value)
            && requestNumberPattern.test(value));
}
function isCanonicalDate(value: unknown): value is string {
    if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/u.test(value))
        return false;
    const parsed = new Date(`${value}T00:00:00Z`);
    return !Number.isNaN(parsed.valueOf()) && parsed.toISOString().slice(0, 10) === value;
}
function isOptionalDate(value: unknown): value is string | null | undefined {
    return value === undefined || value === null || isCanonicalDate(value);
}
function isBudgetMonth(value: unknown): value is string {
    return isCanonicalDate(value) && value.slice(-2) === '01';
}
function isTimestamp(value: unknown): value is string {
    if (typeof value !== 'string' || value.length === 0 || value.length > 64
        || controlCharacterPattern.test(value)
        || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/u.test(value)
        || !isCanonicalDate(value.slice(0, 10)))
        return false;
    return !Number.isNaN(new Date(value).valueOf());
}
function isSignedMoney(value: unknown): value is number {
    return typeof value === 'number' && moneyCents(value) !== null;
}
function isMoney(value: unknown, allowZero = true): value is number {
    return isSignedMoney(value) && value >= (allowZero ? 0 : 0.01);
}
function isOptionalMoney(value: unknown): value is number | null | undefined {
    return value === undefined || value === null || isMoney(value);
}
function isCurrencyRate(value: unknown): value is number {
    if (typeof value !== 'number' || !Number.isFinite(value)
        || value <= 0 || value > maximumCurrencyRate)
        return false;
    const scaled = value * 1000000;
    return Math.abs(scaled - Math.round(scaled)) < 0.000001;
}
function isCurrencyMultiplicity(value: unknown): value is number {
    return typeof value === 'number'
        && Number.isSafeInteger(value)
        && value > 0
        && value <= maximumCurrencyMultiplicity;
}
function isCurrencySnapshot(value: Record<string, unknown>): boolean {
    const snapshotRateDate = value.currencyRateDate;
    const snapshotRate = value.currencyRate;
    const snapshotMultiplicity = value.currencyMultiplicity;
    const isEmpty = snapshotRateDate == null
        && snapshotRate == null
        && snapshotMultiplicity == null;
    if (isEmpty)
        return true;
    if (value.currencyCode === 'KZT') {
        return snapshotRateDate == null && snapshotRate === 1 && snapshotMultiplicity === 1;
    }
    return isCanonicalDate(snapshotRateDate)
        && isCurrencyRate(snapshotRate)
        && isCurrencyMultiplicity(snapshotMultiplicity);
}
function hasCurrencySnapshot(value: Record<string, unknown>): boolean {
    return value.currencyRateDate != null
        || value.currencyRate != null
        || value.currencyMultiplicity != null;
}
function isOptionalBoolean(value: unknown): value is boolean | undefined {
    return value === undefined || typeof value === 'boolean';
}
function moneyCents(value: number): number | null {
    if (!Number.isFinite(value) || Math.abs(value) > maximumMoney)
        return null;
    // Check the canonical decimal representation, not a fixed epsilon on the
    // binary product value * 100. Never round extra source precision into money.
    const text = value.toFixed(2);
    if (Number(text) !== value)
        return null;
    const negative = text.startsWith('-');
    const [whole, fraction] = (negative ? text.slice(1) : text).split('.');
    const cents = BigInt(whole) * 100n + BigInt(fraction);
    if (cents > BigInt(Number.MAX_SAFE_INTEGER))
        return null;
    return Number(negative ? -cents : cents);
}
function sumMoneyCents(values: readonly number[]): number | null {
    let total = 0;
    for (const value of values) {
        const cents = moneyCents(value);
        if (cents === null || !Number.isSafeInteger(total + cents))
            return null;
        total += cents;
    }
    return total;
}
type PublicVatRateCode = 'Vat16' | 'WithoutVat' | null;
function isVatRateCode(value: unknown): value is PublicVatRateCode {
    return value === null || value === 'Vat16' || value === 'WithoutVat';
}
function hasOwnField(value: Record<string, unknown>, field: string): boolean {
    return Object.prototype.hasOwnProperty.call(value, field);
}
function expectedVatCents(amount: number, vatRateCode: PublicVatRateCode): bigint | null {
    const amountCents = moneyCents(amount);
    if (amountCents === null)
        return null;
    if (vatRateCode === null)
        return null;
    if (vatRateCode === 'WithoutVat')
        return 0n;
    // The server uses decimal.Round(amount * 16 / 116, 2, AwayFromZero).
    // Positive money can be reproduced exactly in integer cents without relying
    // on binary floating-point multiplication.
    return (BigInt(amountCents) * 16n + 58n) / 116n;
}
function matchesSourceCalculatedAmounts(value: Record<string, unknown>, amount: number, vatRateCode: PublicVatRateCode): boolean {
    const hasVatAmount = hasOwnField(value, 'vatAmount');
    const hasAmountExcludingVat = hasOwnField(value, 'amountExcludingVat');
    if (!hasVatAmount && !hasAmountExcludingVat)
        return true;
    if (!hasVatAmount || !hasAmountExcludingVat)
        return false;
    const expectedVat = expectedVatCents(amount, vatRateCode);
    if (expectedVat === null) {
        return value.vatAmount === null && value.amountExcludingVat === null;
    }
    if (typeof value.vatAmount !== 'number'
        || typeof value.amountExcludingVat !== 'number')
        return false;
    const actualVat = moneyCents(value.vatAmount);
    const actualExcludingVat = moneyCents(value.amountExcludingVat);
    const amountCents = moneyCents(amount);
    if (actualVat === null || actualExcludingVat === null || amountCents === null)
        return false;
    return BigInt(actualVat) === expectedVat
        && BigInt(actualExcludingVat) === BigInt(amountCents) - expectedVat;
}
function matchesKztCalculatedAmounts(value: Record<string, unknown>): boolean {
    const hasAmountKzt = hasOwnField(value, 'amountKzt');
    const hasVatAmountKzt = hasOwnField(value, 'vatAmountKzt');
    const hasAmountExcludingVatKzt = hasOwnField(value, 'amountExcludingVatKzt');
    if (!hasAmountKzt && !hasVatAmountKzt && !hasAmountExcludingVatKzt)
        return true;
    const amountKzt = value.amountKzt;
    if (amountKzt === null || amountKzt === undefined) {
        return (value.vatAmountKzt === null || value.vatAmountKzt === undefined)
            && (value.amountExcludingVatKzt === null || value.amountExcludingVatKzt === undefined);
    }
    if (typeof amountKzt !== 'number')
        return false;
    // Legacy DTOs may omit both KZT VAT components. If either is supplied,
    // however, both must be numeric and reconcile exactly to amountKzt.
    if (!hasVatAmountKzt && !hasAmountExcludingVatKzt)
        return true;
    if (!hasVatAmountKzt || !hasAmountExcludingVatKzt
        || typeof value.vatAmountKzt !== 'number'
        || typeof value.amountExcludingVatKzt !== 'number')
        return false;
    const amountCents = moneyCents(amountKzt);
    const vatCents = moneyCents(value.vatAmountKzt);
    const excludingVatCents = moneyCents(value.amountExcludingVatKzt);
    if (amountCents === null || vatCents === null || excludingVatCents === null)
        return false;
    return vatCents + excludingVatCents === amountCents;
}
function matchesAggregateTotals(lines: readonly SavedExpenseRequestLine[], totalAmount: number, totalAmountKzt: unknown, hasTotalAmountKzt: boolean, currencyCode: unknown): boolean {
    const expectedAmountCents = sumMoneyCents(lines.map((line) => line.amount));
    const declaredAmountCents = moneyCents(totalAmount);
    if (expectedAmountCents === null
        || declaredAmountCents === null
        || expectedAmountCents !== declaredAmountCents) {
        return false;
    }
    // Older consumers may omit the nullable KZT total. Preserve that wire
    // compatibility, but whenever the field is present it must agree with the
    // same line-level snapshot used by the server aggregate.
    if (!hasTotalAmountKzt)
        return true;
    // Older empty-aggregate fixtures also omitted currencyCode. There is no
    // reliable KZT/null distinction in that shape, so retain it as a legacy
    // compatibility case rather than rejecting a valid zero draft.
    if (lines.length === 0 && currencyCode === undefined)
        return true;
    const kztValues = lines.map((line) => line.amountKzt);
    const expectedKztCents = lines.length === 0
        ? currencyCode === 'KZT' ? 0 : null
        : kztValues.some((value) => value === null)
            ? null
            : sumMoneyCents(kztValues.filter((value): value is number => value !== null));
    if (expectedKztCents === null)
        return totalAmountKzt === null;
    return typeof totalAmountKzt === 'number'
        && moneyCents(totalAmountKzt) === expectedKztCents;
}
type ValidatedOptionalAggregateFields = Pick<SavedExpenseRequest, 'requestNumber' | 'organizationId' | 'defaultCostCenterId' | 'plannedPaymentDate' | 'currencyCode' | 'currencyRateDate' | 'currencyRate' | 'currencyMultiplicity' | 'counterpartyId' | 'contractId' | 'purpose' | 'comment' | 'businessJustification' | 'isUrgent' | 'urgencyReason' | 'isNewCounterparty' | 'isNewContract' | 'paymentBasisCode' | 'invoiceNumber' | 'invoiceDate' | 'publicOfferName' | 'publicOfferIdentifier' | 'otherBasisExplanation' | 'supportingDocumentMissing' | 'supportingDocumentMissingExplanation' | 'candidateCounterpartyName' | 'candidateResidencyCode' | 'candidateBinIin' | 'candidateCountry' | 'candidateForeignRegistrationNumber' | 'candidateRegistrationNumberMissing' | 'candidateRegistrationNumberMissingExplanation' | 'candidateContractNumber' | 'candidateContractNumberMissing' | 'candidateContractDate' | 'candidateContractSubject' | 'candidateContractTerms' | 'newContractDocumentMissing' | 'newContractDocumentMissingExplanation' | 'totalAmountKzt'>;
const allowedBudgetRequirementCodes = new Set([
    'InitiatorExternalId',
    'OrganizationId',
    'DefaultCostCenterId',
    'PlannedPaymentDate',
    'CurrencyCode',
    'BusinessJustification',
    'UrgencyReason',
    'Lines',
    'CurrencyRate',
]);
const allowedBudgetLineRequirementPattern = /^Lines\[(?:[1-9]\d{0,3}|10000)\]\.(?:CashFlowItemId|CostCenterId|Amount|VatRateCode|CalculatedAmounts|CurrencyConversion)$/u;
function isPublicBudgetRequirement(value: string): boolean {
    return allowedBudgetRequirementCodes.has(value)
        || allowedBudgetLineRequirementPattern.test(value);
}
function isSafeBudgetRequirementList(value: unknown): value is string[] {
    return Array.isArray(value)
        && value.length <= 1000
        && value.every((item) => typeof item === 'string'
            && isPublicText(item, 256)
            && isPublicBudgetRequirement(item));
}
function publicLine(value: unknown): SavedExpenseRequestLine | null {
    if (!isRecord(value))
        return null;
    const vatRateCode = value.vatRateCode;
    if (!isUuid(value.id)
        || !isOptionalUuid(value.cashFlowItemId)
        || !isOptionalUuid(value.costCenterId)
        || !isOptionalUuid(value.accrualCostCenterId)
        || !isOptionalDate(value.accrualPeriod)
        || !isMoney(value.amount, false)
        || !isVatRateCode(vatRateCode)
        || !isOptionalMoney(value.vatAmount)
        || !isOptionalMoney(value.amountExcludingVat)
        || !isOptionalMoney(value.amountKzt)
        || !isOptionalMoney(value.vatAmountKzt)
        || !isOptionalMoney(value.amountExcludingVatKzt)) {
        return null;
    }
    if (!matchesSourceCalculatedAmounts(value, value.amount, vatRateCode)
        || !matchesKztCalculatedAmounts(value))
        return null;
    return {
        id: value.id,
        cashFlowItemId: value.cashFlowItemId ?? null,
        costCenterId: value.costCenterId ?? null,
        ...(value.accrualCostCenterId === undefined ? {} : { accrualCostCenterId: value.accrualCostCenterId as string | null }),
        accrualPeriod: value.accrualPeriod ?? null,
        amount: value.amount,
        vatRateCode,
        vatAmount: value.vatAmount ?? null,
        amountExcludingVat: value.amountExcludingVat ?? null,
        amountKzt: value.amountKzt ?? null,
        ...(value.vatAmountKzt === undefined ? {} : { vatAmountKzt: value.vatAmountKzt }),
        ...(value.amountExcludingVatKzt === undefined
            ? {}
            : { amountExcludingVatKzt: value.amountExcludingVatKzt }),
    };
}
function publicBudgetPosition(value: unknown): BudgetReservationPosition | null {
    if (!isRecord(value)
        || !isUuid(value.expenseRequestLineId)
        || !isUuid(value.cashFlowItemId)
        || !isUuid(value.costCenterId)
        || !isBudgetMonth(value.budgetMonth)
        || !isMoney(value.reservedAmountKzt)
        || !isOptionalPublicText(value.cashFlowItemName, 1000)
        || !isOptionalPublicText(value.costCenterName, 1000)
        || !isOptionalMoney(value.limitAmountKzt)
        || !(value.availableAmountKzt === undefined || value.availableAmountKzt === null
            || isSignedMoney(value.availableAmountKzt))
        || !isOptionalMoney(value.paidAmountKzt)
        || !isOptionalBoolean(value.isLimitConfigured)
        || !(value.isOverLimit === undefined
            || value.isOverLimit === null
            || typeof value.isOverLimit === 'boolean')) {
        return null;
    }
    return {
        expenseRequestLineId: value.expenseRequestLineId,
        cashFlowItemId: value.cashFlowItemId,
        costCenterId: value.costCenterId,
        ...(value.cashFlowItemName === undefined ? {} : { cashFlowItemName: value.cashFlowItemName }),
        ...(value.costCenterName === undefined ? {} : { costCenterName: value.costCenterName }),
        budgetMonth: value.budgetMonth,
        reservedAmountKzt: value.reservedAmountKzt,
        ...(value.limitAmountKzt === undefined ? {} : { limitAmountKzt: value.limitAmountKzt }),
        ...(value.availableAmountKzt === undefined
            ? {}
            : { availableAmountKzt: value.availableAmountKzt }),
        ...(value.paidAmountKzt === undefined || value.paidAmountKzt === null
            ? {}
            : { paidAmountKzt: value.paidAmountKzt }),
        ...(value.isLimitConfigured === undefined
            ? {}
            : { isLimitConfigured: value.isLimitConfigured }),
        ...(value.isOverLimit === undefined ? {} : { isOverLimit: value.isOverLimit }),
    };
}
function matchesPaymentReservation(aggregate: SavedExpenseRequest, isComplete: boolean, positions: readonly BudgetReservationPosition[], reservedAmountCents: number): boolean {
    const paymentDate = aggregate.plannedPaymentDate;
    if (!isComplete || aggregate.lines.length === 0 || !isCanonicalDate(paymentDate))
        return false;
    const lineAmounts: number[] = [];
    let originalAmountCents = 0;
    for (const line of aggregate.lines) {
        if (!isUuid(line.costCenterId) || !isUuid(line.cashFlowItemId)
            || !isMoney(line.amountKzt, false))
            return false;
        const cents = moneyCents(line.amountKzt);
        if (cents === null || !Number.isSafeInteger(originalAmountCents + cents))
            return false;
        originalAmountCents += cents;
        lineAmounts.push(cents);
    }
    if (aggregate.status === 'PAID' || aggregate.status === 'CLOSED') {
        return reservedAmountCents === 0 && positions.length === 0;
    }
    // Only the existing KZT writer has an accepted partial-payment projection.
    // Do not invent a foreign-currency allocation or rounding rule here.
    if (aggregate.status !== 'PARTIALLY_PAID' || aggregate.currencyCode !== 'KZT'
        || reservedAmountCents <= 0 || reservedAmountCents >= originalAmountCents)
        return false;
    const budgetMonth = `${paymentDate.slice(0, 7)}-01`;
    if (positions.some((position) => position.reservedAmountKzt <= 0
        || position.budgetMonth !== budgetMonth))
        return false;
    const byLine = new Map(positions.map((position) => [position.expenseRequestLineId.toLowerCase(), position]));
    // Settlement consumes the canonical line order FIFO. Therefore the unpaid
    // remainder fills its suffix, with at most one partially reserved line.
    // Validate that absolute state; no money is calculated for a write here.
    let remainingCents = reservedAmountCents;
    for (let index = aggregate.lines.length - 1; index >= 0; index--) {
        const expectedCents = Math.min(lineAmounts[index], remainingCents);
        const position = byLine.get(aggregate.lines[index].id.toLowerCase());
        const actualCents = position ? moneyCents(position.reservedAmountKzt) : 0;
        if (actualCents !== expectedCents)
            return false;
        remainingCents -= expectedCents;
    }
    return remainingCents === 0;
}
function publicBudgetReservation(value: unknown, aggregate: SavedExpenseRequest): BudgetReservationSummary | null {
    if (!isRecord(value)
        || typeof value.isComplete !== 'boolean'
        || !isMoney(value.reservedAmountKzt)
        || !isSafeBudgetRequirementList(value.missingRequirements)
        || value.isComplete !== (value.missingRequirements.length === 0)
        || !Array.isArray(value.positions)
        || value.positions.length > 10000) {
        return null;
    }
    // Payroll advances deliberately redact the HR budget from the public DTO.
    // A complete document is not a visible reservation: accept only that exact
    // empty, zero-valued privacy projection, while retaining document checks.
    if (aggregate.paymentOperation?.kind === 'PayrollAdvance' && value.isComplete
        && value.reservedAmountKzt === 0 && value.positions.length === 0) {
        const operation = aggregate.paymentOperation;
        const line = aggregate.lines[0];
        if (!isUuid(operation.budgetCostCenterId) || !isUuid(aggregate.defaultCostCenterId)
            || aggregate.lines.length !== 1 || !line || !isUuid(line.cashFlowItemId)
            || line.costCenterId?.toLowerCase() !== aggregate.defaultCostCenterId.toLowerCase()
            || line.vatRateCode !== 'WithoutVat' || !isMoney(operation.advanceAmount, false)
            || moneyCents(operation.advanceAmount) !== moneyCents(aggregate.totalAmount))
            return null;
        return { isComplete: true, reservedAmountKzt: 0, missingRequirements: [], positions: [] };
    }
    // Array.prototype.map preserves sparse holes, and the subsequent `some`
    // would skip those holes and silently publish a truncated reservation.
    // Array.from visits every index so a hole becomes `undefined` and is refused
    // by the public boundary like any other malformed position.
    const positions = Array.from(value.positions, publicBudgetPosition);
    if (positions.some((item) => item === null))
        return null;
    const publicPositions = positions.filter((item): item is BudgetReservationPosition => item !== null);
    const aggregateLines = new Map(aggregate.lines.map((line) => [line.id.toLowerCase(), line]));
    const isReleaseStatus = aggregate.status === 'REJECTED' || aggregate.status === 'CANCELLED';
    const isPaymentStatus = aggregate.status === 'PARTIALLY_PAID'
        || aggregate.status === 'PAID' || aggregate.status === 'CLOSED';
    const positionLineIds = publicPositions.map((position) => position.expenseRequestLineId.toLowerCase());
    const uniquePositionLineIds = new Set(positionLineIds);
    const reservedAmountCents = moneyCents(value.reservedAmountKzt);
    const positionAmountCents = sumMoneyCents(publicPositions.map((position) => position.reservedAmountKzt));
    if (uniquePositionLineIds.size !== positionLineIds.length
        || positionLineIds.some((lineId, index) => {
            const line = aggregateLines.get(lineId);
            const position = publicPositions[index];
            const lineAmountCents = line?.amountKzt === null || line?.amountKzt === undefined
                ? null
                : moneyCents(line.amountKzt);
            const positionAmountCents = moneyCents(position.reservedAmountKzt);
            return line === undefined
                || line.cashFlowItemId === null
                || line.costCenterId === null
                || position.cashFlowItemId.toLowerCase() !== line.cashFlowItemId.toLowerCase()
                || position.costCenterId.toLowerCase() !== line.costCenterId.toLowerCase()
                || (value.isComplete && !isPaymentStatus
                    && (lineAmountCents === null
                        || positionAmountCents === null
                        || lineAmountCents !== positionAmountCents));
        })
        || reservedAmountCents === null
        || positionAmountCents === null
        || reservedAmountCents !== positionAmountCents
        || (value.isComplete && !isPaymentStatus && (aggregateLines.size === 0
            || (uniquePositionLineIds.size !== aggregateLines.size
                // Completeness describes the document, not an active reservation.
                // Historical REJECT/CANCEL responses may retain their full snapshot.
                && !(isReleaseStatus && publicPositions.length === 0 && value.reservedAmountKzt === 0))))) {
        return null;
    }
    if (isPaymentStatus && !matchesPaymentReservation(aggregate, value.isComplete, publicPositions, reservedAmountCents))
        return null;
    return {
        isComplete: value.isComplete,
        reservedAmountKzt: value.reservedAmountKzt,
        missingRequirements: value.missingRequirements,
        positions: publicPositions,
    };
}
function publicSubmissionReadiness(value: unknown): SubmissionReadiness | null {
    const missingRequirements = isRecord(value)
        ? safeSubmissionReadinessRequirementList(value.missingRequirements)
        : null;
    if (!isRecord(value)
        || typeof value.isReady !== 'boolean'
        || !missingRequirements
        || value.isReady !== (missingRequirements.length === 0)) {
        return null;
    }
    return {
        isReady: value.isReady,
        missingRequirements,
    };
}
function hasSafeOptionalFields(value: Record<string, unknown>): value is Record<string, unknown> & ValidatedOptionalAggregateFields {
    const nullableTextFields: ReadonlyArray<readonly [
        string,
        number
    ]> = [
        ['purpose', 1000],
        ['comment', 1000],
        ['businessJustification', 1000],
        ['urgencyReason', 1000],
        ['invoiceNumber', 100],
        ['publicOfferName', 300],
        ['publicOfferIdentifier', 1000],
        ['otherBasisExplanation', 1000],
        ['supportingDocumentMissingExplanation', 1000],
        ['candidateCounterpartyName', 300],
        ['candidateBinIin', 12],
        ['candidateCountry', 100],
        ['candidateForeignRegistrationNumber', 100],
        ['candidateRegistrationNumberMissingExplanation', 1000],
        ['candidateContractNumber', 100],
        ['candidateContractSubject', 300],
        ['candidateContractTerms', 1000],
        ['newContractDocumentMissingExplanation', 1000],
    ];
    const multilineFields = new Set(['purpose', 'comment', 'businessJustification', 'urgencyReason',
        'otherBasisExplanation', 'supportingDocumentMissingExplanation',
        'candidateRegistrationNumberMissingExplanation', 'candidateContractSubject',
        'candidateContractTerms', 'newContractDocumentMissingExplanation']);
    if (!nullableTextFields.every(([field, maximum]) => (multilineFields.has(field)
        ? value[field] === undefined || value[field] === null || isPublicExpenseRequestMultilineText(value[field], maximum)
        : isOptionalPublicText(value[field], maximum))))
        return false;
    return isPaymentOperation(value.paymentOperation) && ['organizationId', 'defaultCostCenterId', 'counterpartyId', 'contractId']
        .every((field) => isOptionalUuid(value[field]))
        && ['plannedPaymentDate', 'currencyRateDate', 'invoiceDate', 'candidateContractDate']
            .every((field) => isOptionalDate(value[field]))
        && ['isUrgent', 'isNewCounterparty', 'isNewContract', 'supportingDocumentMissing',
            'candidateRegistrationNumberMissing', 'candidateContractNumberMissing',
            'newContractDocumentMissing']
            .every((field) => isOptionalBoolean(value[field]))
        && (value.currencyCode === undefined || (typeof value.currencyCode === 'string' && currencyPattern.test(value.currencyCode)))
        && (value.paymentBasisCode === undefined || value.paymentBasisCode === null
            || value.paymentBasisCode === 'Contract'
            || value.paymentBasisCode === 'InvoiceWithoutContract'
            || value.paymentBasisCode === 'PublicOffer'
            || value.paymentBasisCode === 'OtherBasis')
        && (value.candidateResidencyCode === undefined || value.candidateResidencyCode === null
            || value.candidateResidencyCode === 'ResidentKz'
            || value.candidateResidencyCode === 'NonResident')
        && (value.currencyRate === undefined || value.currencyRate === null
            || isCurrencyRate(value.currencyRate))
        && (value.currencyMultiplicity === undefined || value.currencyMultiplicity === null
            || isCurrencyMultiplicity(value.currencyMultiplicity))
        && isCurrencySnapshot(value)
        && isOptionalPublicExpenseRequestNumber(value.requestNumber)
        && isOptionalMoney(value.totalAmountKzt);
}
function publicAggregate(value: unknown): SavedExpenseRequest | null {
    if (!isRecord(value)
        || !isUuid(value.id)
        || !isExpenseRequestStatus(value.status)
        || !(value.initiatorExternalId === undefined
            || isPublicText(value.initiatorExternalId, 256))
        || !isMoney(value.totalAmount)
        || !isTimestamp(value.createdAtUtc)
        || !isTimestamp(value.updatedAtUtc)
        || !isUuid(value.concurrencyStamp)
        || !Array.isArray(value.lines)
        || value.lines.length > 10000
        || !hasSafeOptionalFields(value)) {
        return null;
    }
    // Do not let a sparse JSON-shaped array disappear through map/filter: every
    // declared index must carry a valid public line.
    const lines = Array.from(value.lines, publicLine);
    if (lines.some((item) => item === null))
        return null;
    const publicLines = lines.filter((item): item is SavedExpenseRequestLine => item !== null);
    const lineIds = publicLines.map((line) => line.id.toLowerCase());
    if (new Set(lineIds).size !== lineIds.length
        || !matchesAggregateTotals(publicLines, value.totalAmount, value.totalAmountKzt, Object.prototype.hasOwnProperty.call(value, 'totalAmountKzt'), value.currencyCode))
        return null;
    if (hasCurrencySnapshot(value)
        && (value.totalAmountKzt == null || publicLines.some((line) => line.amountKzt === null))) {
        return null;
    }
    return {
        id: value.id,
        status: value.status,
        ...(value.paymentOperation == null ? {} : { paymentOperation: value.paymentOperation as import('../utils/expensePaymentOperation').ExpensePaymentOperation }),
        ...(value.requestNumber === undefined ? {} : { requestNumber: value.requestNumber }),
        ...(value.initiatorExternalId === undefined ? {}
            : { initiatorExternalId: value.initiatorExternalId }),
        ...(value.organizationId === undefined ? {} : { organizationId: value.organizationId }),
        ...(value.defaultCostCenterId === undefined ? {}
            : { defaultCostCenterId: value.defaultCostCenterId }),
        ...(value.plannedPaymentDate === undefined ? {}
            : { plannedPaymentDate: value.plannedPaymentDate }),
        ...(value.currencyCode === undefined ? {} : { currencyCode: value.currencyCode }),
        ...(value.currencyRateDate === undefined ? {} : { currencyRateDate: value.currencyRateDate }),
        ...(value.currencyRate === undefined ? {} : { currencyRate: value.currencyRate }),
        ...(value.currencyMultiplicity === undefined
            ? {}
            : { currencyMultiplicity: value.currencyMultiplicity }),
        ...(value.counterpartyId === undefined ? {} : { counterpartyId: value.counterpartyId }),
        ...(value.contractId === undefined ? {} : { contractId: value.contractId }),
        ...(value.purpose === undefined ? {} : { purpose: value.purpose }),
        ...(value.comment === undefined ? {} : { comment: value.comment }),
        ...(value.businessJustification === undefined
            ? {}
            : { businessJustification: value.businessJustification }),
        ...(value.isUrgent === undefined ? {} : { isUrgent: value.isUrgent }),
        ...(value.urgencyReason === undefined ? {} : { urgencyReason: value.urgencyReason }),
        ...(value.isNewCounterparty === undefined
            ? {}
            : { isNewCounterparty: value.isNewCounterparty }),
        ...(value.isNewContract === undefined ? {} : { isNewContract: value.isNewContract }),
        ...(value.paymentBasisCode === undefined ? {} : { paymentBasisCode: value.paymentBasisCode }),
        ...(value.invoiceNumber === undefined ? {} : { invoiceNumber: value.invoiceNumber }),
        ...(value.invoiceDate === undefined ? {} : { invoiceDate: value.invoiceDate }),
        ...(value.publicOfferName === undefined ? {} : { publicOfferName: value.publicOfferName }),
        ...(value.publicOfferIdentifier === undefined
            ? {}
            : { publicOfferIdentifier: value.publicOfferIdentifier }),
        ...(value.otherBasisExplanation === undefined
            ? {}
            : { otherBasisExplanation: value.otherBasisExplanation }),
        ...(value.supportingDocumentMissing === undefined
            ? {}
            : { supportingDocumentMissing: value.supportingDocumentMissing }),
        ...(value.supportingDocumentMissingExplanation === undefined
            ? {}
            : { supportingDocumentMissingExplanation: value.supportingDocumentMissingExplanation }),
        ...(value.candidateCounterpartyName === undefined
            ? {}
            : { candidateCounterpartyName: value.candidateCounterpartyName }),
        ...(value.candidateResidencyCode === undefined
            ? {}
            : { candidateResidencyCode: value.candidateResidencyCode }),
        ...(value.candidateBinIin === undefined ? {} : { candidateBinIin: value.candidateBinIin }),
        ...(value.candidateCountry === undefined ? {} : { candidateCountry: value.candidateCountry }),
        ...(value.candidateForeignRegistrationNumber === undefined
            ? {}
            : { candidateForeignRegistrationNumber: value.candidateForeignRegistrationNumber }),
        ...(value.candidateRegistrationNumberMissing === undefined
            ? {}
            : { candidateRegistrationNumberMissing: value.candidateRegistrationNumberMissing }),
        ...(value.candidateRegistrationNumberMissingExplanation === undefined
            ? {}
            : { candidateRegistrationNumberMissingExplanation: value.candidateRegistrationNumberMissingExplanation }),
        ...(value.candidateContractNumber === undefined
            ? {}
            : { candidateContractNumber: value.candidateContractNumber }),
        ...(value.candidateContractNumberMissing === undefined
            ? {}
            : { candidateContractNumberMissing: value.candidateContractNumberMissing }),
        ...(value.candidateContractDate === undefined
            ? {}
            : { candidateContractDate: value.candidateContractDate }),
        ...(value.candidateContractSubject === undefined
            ? {}
            : { candidateContractSubject: value.candidateContractSubject }),
        ...(value.candidateContractTerms === undefined
            ? {}
            : { candidateContractTerms: value.candidateContractTerms }),
        ...(value.newContractDocumentMissing === undefined
            ? {}
            : { newContractDocumentMissing: value.newContractDocumentMissing }),
        ...(value.newContractDocumentMissingExplanation === undefined
            ? {}
            : { newContractDocumentMissingExplanation: value.newContractDocumentMissingExplanation }),
        totalAmount: value.totalAmount,
        totalAmountKzt: isMoney(value.totalAmountKzt) ? value.totalAmountKzt : null,
        createdAtUtc: value.createdAtUtc,
        updatedAtUtc: value.updatedAtUtc,
        concurrencyStamp: value.concurrencyStamp,
        lines: publicLines,
    };
}
export function publicSavedExpenseRequest(payload: unknown, expectedRequestId?: string, options: {
    requireEditAuthority?: boolean;
} = {}): SavedExpenseRequest {
    if (!isRecord(payload))
        throw new Error(invalidResponseMessage);
    const hasEnvelope = Object.prototype.hasOwnProperty.call(payload, 'expenseRequest');
    const aggregatePayload = hasEnvelope ? payload.expenseRequest : payload;
    const aggregate = publicAggregate(aggregatePayload);
    if (!aggregate
        || (expectedRequestId !== undefined
            && (!isUuid(expectedRequestId)
                || aggregate.id.toLowerCase() !== expectedRequestId.toLowerCase()))) {
        throw new Error(invalidResponseMessage);
    }
    const nestedAggregate = isRecord(aggregatePayload) ? aggregatePayload : undefined;
    const budgetValue = payload.budgetReservation ?? nestedAggregate?.budgetReservation;
    const readinessValue = payload.submissionReadiness ?? nestedAggregate?.submissionReadiness;
    const budgetReservation = budgetValue === undefined
        ? undefined
        : publicBudgetReservation(budgetValue, aggregate);
    const submissionReadiness = readinessValue === undefined
        ? undefined
        : publicSubmissionReadiness(readinessValue);
    if (budgetReservation === null || submissionReadiness === null) {
        throw new Error(invalidResponseMessage);
    }
    const hasEditAuthority = hasEnvelope
        && Object.prototype.hasOwnProperty.call(payload, 'editAuthority');
    const editAuthorityValue = hasEditAuthority ? payload.editAuthority : undefined;
    if ((options.requireEditAuthority === true && (!hasEnvelope || !hasEditAuthority))
        || (hasEditAuthority
            && (!isRecord(editAuthorityValue)
                || typeof editAuthorityValue.canEdit !== 'boolean'))) {
        throw new Error(invalidResponseMessage);
    }
    const editAuthority = isRecord(editAuthorityValue)
        && typeof editAuthorityValue.canEdit === 'boolean'
        ? { canEdit: editAuthorityValue.canEdit }
        : undefined;
    const initiatorDisplayName = hasEnvelope ? payload.initiatorDisplayName : undefined;
    if (initiatorDisplayName !== undefined && initiatorDisplayName !== null
        && (!isPublicText(initiatorDisplayName, 256)
            || initiatorDisplayName.trim() !== initiatorDisplayName
            || initiatorDisplayName.length === 0
            || /^S-1-/iu.test(initiatorDisplayName))) {
        throw new Error(invalidResponseMessage);
    }
    return {
        ...aggregate,
        ...(budgetReservation ? { budgetReservation } : {}),
        ...(submissionReadiness ? { submissionReadiness } : {}),
        ...(editAuthority ? { editAuthority } : {}),
        ...(initiatorDisplayName === undefined ? {} : { initiatorDisplayName }),
    };
}
export { isCanonicalDate as isPublicExpenseRequestDate, isMoney as isPublicExpenseRequestMoney, moneyCents as publicExpenseRequestMoneyCents, isOptionalMoney as isOptionalPublicExpenseRequestMoney, isOptionalPublicText as isOptionalPublicExpenseRequestText, isOptionalUuid as isOptionalPublicExpenseRequestUuid, isPublicText as isPublicExpenseRequestText, isTimestamp as isPublicExpenseRequestTimestamp, isUuid as isPublicExpenseRequestUuid, };
