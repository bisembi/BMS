using Bms.Domain.Budgeting;

namespace Bms.Domain.ExpenseRequests;

public sealed record SubmissionAttachmentFacts(
    bool HasSupportingDocument,
    bool HasNewContractDocument)
{
    public static SubmissionAttachmentFacts None { get; } = new(false, false);
}

public sealed record SubmissionReadiness(
    bool IsReady,
    IReadOnlyList<string> MissingRequirements);

public static class SubmissionReadinessEvaluator
{
    public static SubmissionReadiness Evaluate(
        ExpenseRequest request,
        SubmissionAttachmentFacts? attachmentFacts = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var facts = attachmentFacts ?? SubmissionAttachmentFacts.None;
        var missing = ExpenseRequestBudgetProjectionCalculator.Calculate(request)
            .MissingRequirements
            .ToList();

        if (request.PaymentOperation is not null)
        {
            missing.AddRange(request.PaymentOperation.MissingRequirements(request.CurrencyCode, facts, request.Lines, request.PlannedPaymentDate));
            AddSupportingDocumentRequirements(request, facts, missing);
            var operationMissing = missing.Distinct(StringComparer.Ordinal).ToArray();
            return new(operationMissing.Length == 0, operationMissing);
        }
        if (!request.CounterpartyId.HasValue && !request.IsNewCounterparty)
        {
            // The specification says the counterparty is conditionally required,
            // but the exact basis/scenario matrix is still OPEN. Block submission
            // conservatively without pretending that a final rule was approved.
            missing.Add("CounterpartyRequirementUnresolved");
        }

        AddCounterpartyCandidateRequirements(request, missing);
        AddPaymentBasisRequirements(request, missing);
        AddContractCandidateRequirements(request, facts, missing);
        AddSupportingDocumentRequirements(request, facts, missing);

        var distinct = missing.Distinct(StringComparer.Ordinal).ToArray();
        return new SubmissionReadiness(distinct.Length == 0, distinct);
    }

    private static void AddCounterpartyCandidateRequirements(
        ExpenseRequest request,
        ICollection<string> missing)
    {
        if (request.CandidateRegistrationNumberMissing &&
            (!request.IsNewCounterparty || request.CandidateResidencyCode != CounterpartyResidency.NonResident))
        {
            missing.Add("CandidateRegistrationNumberMissing.NonResidentOnly");
        }

        if (!request.IsNewCounterparty)
        {
            return;
        }

        AddIfBlank(request.CandidateCounterpartyName, "CandidateCounterpartyName", missing);
        if (!request.CandidateResidencyCode.HasValue)
        {
            missing.Add("CandidateResidencyCode");
            return;
        }

        if (request.CandidateResidencyCode == CounterpartyResidency.ResidentKz)
        {
            AddIfBlank(request.CandidateBinIin, "CandidateBinIin", missing);
            return;
        }

        AddIfBlank(request.CandidateCountry, "CandidateCountry", missing);
        if (request.CandidateRegistrationNumberMissing)
        {
            AddIfBlank(
                request.CandidateRegistrationNumberMissingExplanation,
                "CandidateRegistrationNumberMissingExplanation",
                missing);
        }
        else
        {
            AddIfBlank(
                request.CandidateForeignRegistrationNumber,
                "CandidateForeignRegistrationNumber",
                missing);
        }
    }

    private static void AddPaymentBasisRequirements(
        ExpenseRequest request,
        ICollection<string> missing)
    {
        if (!request.PaymentBasisCode.HasValue)
        {
            missing.Add("PaymentBasisCode");
            return;
        }

        switch (request.PaymentBasisCode.Value)
        {
            case PaymentBasisType.Contract:
                if (!request.ContractId.HasValue && !request.IsNewContract)
                {
                    missing.Add("ContractIdOrIsNewContract");
                }

                break;
            case PaymentBasisType.InvoiceWithoutContract:
                AddIfBlank(request.InvoiceNumber, "InvoiceNumber", missing);
                if (!request.InvoiceDate.HasValue)
                {
                    missing.Add("InvoiceDate");
                }

                break;
            case PaymentBasisType.PublicOffer:
                AddIfBlank(request.PublicOfferName, "PublicOfferName", missing);
                AddIfBlank(request.PublicOfferIdentifier, "PublicOfferIdentifier", missing);
                break;
            case PaymentBasisType.OtherBasis:
                AddIfBlank(request.OtherBasisExplanation, "OtherBasisExplanation", missing);
                break;
        }

        if (request.IsNewContract && request.PaymentBasisCode != PaymentBasisType.Contract)
        {
            missing.Add("IsNewContract.ContractBasisOnly");
        }
    }

    private static void AddContractCandidateRequirements(
        ExpenseRequest request,
        SubmissionAttachmentFacts facts,
        ICollection<string> missing)
    {
        if (request.CandidateContractNumberMissing && !request.IsNewContract)
        {
            missing.Add("CandidateContractNumberMissing.NewContractOnly");
        }

        if (request.NewContractDocumentMissing && !request.IsNewContract)
        {
            missing.Add("NewContractDocumentMissing.NewContractOnly");
        }

        if (!request.IsNewContract)
        {
            return;
        }

        if (!request.CandidateContractNumberMissing &&
            string.IsNullOrWhiteSpace(request.CandidateContractNumber))
        {
            missing.Add("CandidateContractNumberOrMissingFlag");
        }

        if (!request.CandidateContractDate.HasValue)
        {
            missing.Add("CandidateContractDate");
        }

        AddIfBlank(request.CandidateContractSubject, "CandidateContractSubject", missing);

        if (!facts.HasNewContractDocument && !request.NewContractDocumentMissing)
        {
            missing.Add("NewContractDocumentOrMissingFlag");
        }

        if (facts.HasNewContractDocument && request.NewContractDocumentMissing)
        {
            missing.Add("NewContractDocumentMissing.FilePresent");
        }

        if (request.NewContractDocumentMissing)
        {
            AddIfBlank(
                request.NewContractDocumentMissingExplanation,
                "NewContractDocumentMissingExplanation",
                missing);
        }
    }

    private static void AddSupportingDocumentRequirements(
        ExpenseRequest request,
        SubmissionAttachmentFacts facts,
        ICollection<string> missing)
    {
        if (!facts.HasSupportingDocument && !request.SupportingDocumentMissing)
        {
            missing.Add("SupportingDocumentOrMissingFlag");
        }

        if (facts.HasSupportingDocument && request.SupportingDocumentMissing)
        {
            missing.Add("SupportingDocumentMissing.FilePresent");
        }

        if (request.SupportingDocumentMissing)
        {
            AddIfBlank(
                request.SupportingDocumentMissingExplanation,
                "SupportingDocumentMissingExplanation",
                missing);
        }
    }

    private static void AddIfBlank(
        string? value,
        string requirement,
        ICollection<string> missing)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(requirement);
        }
    }
}
