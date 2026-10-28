using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// The expense request application service.
///
/// This part declares the type and its collaborators; the commands, the reads,
/// the idempotency resolution, the access-scope checks and the evidence
/// mapping live in the sibling partial files. The primary constructor is
/// declared here only, so the DI registration and the constructor contract are
/// unchanged by the split.

public sealed partial class ExpenseRequestService(
    IExpenseRequestRepository repository,
    IBusinessAuditSink auditSink,
    IOutboxSink outboxSink,
    IIdempotencyStore idempotencyStore,
    IBudgetRepository budgetRepository,
    IAccessScope accessScope,
    IBusinessTransactionRunner transactionRunner,
    IUnitOfWork unitOfWork,
    BudgetOwnerCreationAuthority budgetOwnerCreationAuthority,
    ExpenseRequestDraftCurrencyService draftCurrencyService,
    IClock clock,
    ICurrentActor currentActor,
    IExpenseRequestPaymentFactReader paymentFactReader,
    IExpenseRequestListBusinessColumnsReader listBusinessColumnsReader,
    IExpenseRequestNumberAllocator requestNumberAllocator,
    IExpenseCashFlowAssignmentValidator cashFlowAssignments,
    IApprovalRouteSnapshotStore approvalRouteSnapshotStore,
    ExpenseRequestSubstitutionAuthority? substitutionAuthority = null,
    IExpenseRequestAttachmentBinder? attachmentBinder = null,
    IExpenseRequestAttachmentStore? attachmentStore = null,
    IExpenseRequestContractValidator? contractValidator = null,
    IAccrualDepartmentCatalog? accrualDepartments = null,
    IExpensePaymentOperationCatalog? paymentOperationCatalog = null,
    PayrollAdvanceCreationPolicy372? payrollAdvanceCreationPolicy372 = null)
{
    private const string CopyPolicyVersion = "copy-policy-v1";
    private const int MaximumIdempotencyKeyLength = 127;
    private const int MaximumIdempotencyScopeLength = 323;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task ValidateContractAsync(ExpenseRequest request, CancellationToken token)
    {
        if (request.PaymentOperation is not null && (paymentOperationCatalog is null
            || !await paymentOperationCatalog.MatchesAsync(request.PaymentOperation, token)))
            throw new DomainRuleViolationException("Choose an active employee or enter the employee details explicitly.");
        // Production DI always supplies the SQL validator. The optional seam
        // preserves historical isolated constructors, not a production bypass.
        if (contractValidator is not null && !await contractValidator.MatchesAsync(
            request.ContractId, request.OrganizationId, request.CounterpartyId, request.CurrencyCode, token))
            throw new DomainRuleViolationException("The selected contract does not match the request details.",
                DomainRuleViolationReason.ExpenseDraftContractMismatch);
    }

    // Read persisted facts in the authorized document transaction. Never infer
    // a successful upload from a client flag or the request's missing flag.
    private async Task<ExpenseRequestDto> ToCurrentDtoAsync(ExpenseRequest request,
        IReadOnlyCollection<BudgetRequestProjection>? projections, CancellationToken token)
    {
        var facts = attachmentStore is null ? SubmissionAttachmentFacts.None
            : await attachmentStore.ReadSubmissionFactsAsync(request.Id, token);
        return ExpenseRequestDto.FromDomain(request, projections, facts);
    }

    private static class StatusCodes
    {
        public const int Ok = 200;
        public const int Created = 201;
    }
}
