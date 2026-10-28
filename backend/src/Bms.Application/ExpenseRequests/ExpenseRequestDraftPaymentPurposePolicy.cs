using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// <summary>
/// New drafts do not author the bank purpose; first submission initializes it.
/// An already persisted purpose is editable only by the original initiator
/// after a return. Other statuses and delegated draft edits preserve it.
/// </summary>
internal static class ExpenseRequestDraftPaymentPurposePolicy
{
    internal static string? ResolveUpdatePurpose(
        ExpenseRequest request,
        string actorExternalId,
        string? requestedPurpose)
    {
        if (request.Status == ExpenseRequestStatus.Returned
            && string.Equals(request.InitiatorExternalId, actorExternalId, StringComparison.Ordinal))
        {
            return requestedPurpose;
        }

        EnsureUpdatePreservesPurpose(request.Purpose, requestedPurpose);
        return request.Purpose;
    }

    internal static void EnsureCreateDoesNotSetPurpose(string? requestedPurpose)
    {
        if (Normalize(requestedPurpose) is not null)
        {
            throw new DomainRuleViolationException(
                "Payment purpose cannot be set by a draft command.", DomainRuleViolationReason.ExpenseDraftPurposeManaged);
        }
    }

    internal static void EnsureUpdatePreservesPurpose(
        string? persistedPurpose,
        string? requestedPurpose)
    {
        if (!string.Equals(
                Normalize(persistedPurpose),
                Normalize(requestedPurpose),
                StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException(
                "Payment purpose cannot be changed by a draft command.", DomainRuleViolationReason.ExpenseDraftPurposeManaged);
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
