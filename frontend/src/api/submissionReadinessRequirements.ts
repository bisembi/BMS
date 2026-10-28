const maximumCollectionSize = 1000;
const controlCharacterPattern = /[\u0000-\u001f\u007f-\u009f]/u;
const allowedSubmissionReadinessRequirements = new Set([
    'PaymentOperation.AdvanceAmount',
    'PaymentOperation.AdvanceTotalMismatch',
    'PaymentOperation.BudgetCostCenterId',
    'PaymentOperation.PaymentCode',
    'PaymentOperation.PaymentKind',
    'PaymentOperation.Period',
    'PaymentOperation.TaxCurrencyKzt',
    'PaymentOperation.TaxAuthorityName',
    'PaymentOperation.TaxAuthorityCode',
    'PaymentOperation.BudgetCode',
    'PaymentOperation.RegisterNumber',
    'PaymentOperation.RegisterDate',
    'PaymentOperation.RegisterDocument',
    'PaymentOperation.RegisterAmount',
    'PaymentOperation.RegisterTotalMismatch',
    'PaymentOperation.PayrollMode',
    'PaymentOperation.EmployeeName',
    'PaymentOperation.EmployeeNames',
    'PaymentOperation.EmployeeIin',
    'PaymentOperation.AdvancePurpose',
    'PaymentOperation.AdvanceReportDueDate',
    'PaymentOperation.TransferMethod',
    'PaymentOperation.RecipientName',
    'PaymentOperation.RecipientBinIin',
    'PaymentOperation.RecipientIban',
    'PaymentOperation.RecipientBik',
    'PaymentOperation.RecipientKbe',
    'PaymentOperation.WithoutVat',
    'InitiatorExternalId',
    'OrganizationId',
    'DefaultCostCenterId',
    'PlannedPaymentDate',
    'CurrencyCode',
    'BusinessJustification',
    'UrgencyReason',
    'Lines',
    'CurrencyRate',
    'CounterpartyRequirementUnresolved',
    'CandidateRegistrationNumberMissing.NonResidentOnly',
    'CandidateCounterpartyName',
    'CandidateResidencyCode',
    'CandidateBinIin',
    'CandidateCountry',
    'CandidateRegistrationNumberMissingExplanation',
    'CandidateForeignRegistrationNumber',
    'PaymentBasisCode',
    'ContractIdOrIsNewContract',
    'InvoiceNumber',
    'InvoiceDate',
    'PublicOfferName',
    'PublicOfferIdentifier',
    'OtherBasisExplanation',
    'IsNewContract.ContractBasisOnly',
    'CandidateContractNumberMissing.NewContractOnly',
    'NewContractDocumentMissing.NewContractOnly',
    'CandidateContractNumberOrMissingFlag',
    'CandidateContractDate',
    'CandidateContractSubject',
    'NewContractDocumentOrMissingFlag',
    'NewContractDocumentMissing.FilePresent',
    'NewContractDocumentMissingExplanation',
    'SupportingDocumentOrMissingFlag',
    'SupportingDocumentMissing.FilePresent',
    'SupportingDocumentMissingExplanation',
]);
const allowedLineReadinessRequirementPattern = /^Lines\[(?:[1-9]\d{0,3}|10000)\]\.(?:CurrencyConversion|CashFlowItemId|CostCenterId|Amount|VatRateCode|CalculatedAmounts)$/u;
function isPublicRequirementText(value: unknown): value is string {
    return typeof value === 'string'
        && value.length > 0
        && value.length <= 256
        && value.trim() === value
        && !controlCharacterPattern.test(value);
}
export function safeSubmissionReadinessRequirementList(value: unknown): string[] | null {
    if (!Array.isArray(value) || value.length > maximumCollectionSize)
        return null;
    const codes = value.filter((item): item is string => isPublicRequirementText(item));
    if (codes.length !== value.length || new Set(codes).size !== codes.length)
        return null;
    if (codes.some((code) => (!allowedSubmissionReadinessRequirements.has(code)
        && !allowedLineReadinessRequirementPattern.test(code))))
        return null;
    return [...codes];
}
