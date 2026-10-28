using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;

namespace Bms.Application.ExpenseRequests;

/// Validates command shapes that the JSON binder can represent even though the
/// public command annotations describe non-null line items.  The domain and
/// fingerprint code intentionally operate on real line records; a null item
/// must be rejected before either path enumerates the collection.
internal static class ExpenseRequestCommandShapeValidator
{
    internal static void EnsureCreateShape(CreateExpenseRequestCommand command)
    {
        if (command is null)
        {
            throw new DomainRuleViolationException(
                "Expense request command cannot be null.");
        }

        EnsureLineCount(command.Lines?.Count ?? 0);
        if (command.Lines?.Any(line => line is null) == true)
        {
            throw new DomainRuleViolationException(
                "Expense request lines cannot contain null items.");
        }
    }

    internal static void EnsureUpdateShape(UpdateExpenseRequestCommand command)
    {
        if (command is null)
        {
            throw new DomainRuleViolationException(
                "Expense request command cannot be null.");
        }

        EnsureLineCount(command.Lines?.Count ?? 0);
        if (command.Lines?.Any(line => line is null) == true)
        {
            throw new DomainRuleViolationException(
                "Expense request lines cannot contain null items.");
        }
    }

    private static void EnsureLineCount(int count)
    {
        // Match the existing public DTO, approval split and payment ceiling.
        // Refuse before hashing or acquiring the global business transaction.
        if (count > ExpenseRequestAllocationPolicy.MaximumLines)
            throw new DomainRuleViolationException("Expense request line limit exceeded.");
    }
}
