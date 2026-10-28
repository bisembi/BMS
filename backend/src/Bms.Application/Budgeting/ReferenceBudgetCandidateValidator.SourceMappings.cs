using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bms.Application.Budgeting;

/// The mapping between source rows and the reference identifiers they claim.
internal static partial class ReferenceBudgetCandidateValidator
{

    private static void ValidateSourceMappings(
        IReadOnlyList<CostCenter> costCenters,
        IReadOnlyList<CashFlowItem> cashFlowItems)
    {
        var costCenterNames = costCenters.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var owner in cashFlowItems
                     .Select(item => item.Owner)
                     .Where(owner => owner.Length > 0 && owner != ExcludedOwner)
                     .Distinct(StringComparer.Ordinal))
        {
            if (!costCenterNames.Contains(owner))
            {
                throw Invalid($"cash-flow owner '{owner}' has no exact cost-center mapping");
            }
        }

        var derived = costCenters.Single(item => item.FromItemOwner);
        if (!cashFlowItems.Any(item =>
                string.Equals(item.Owner, derived.Name, StringComparison.Ordinal)))
        {
            throw Invalid("the derived cost center is not used by a cash-flow item");
        }
    }
}
