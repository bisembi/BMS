using System.Security.Cryptography;
using System.Text;
using Bms.Domain.Budgeting;
using Bms.Domain.Payments;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Shared transaction-owned application-lock namespace. Existing budget
/// resource names are preserved for mixed-version compatibility; new treasury
/// resources sort after them under one ordinal acquisition policy.
/// </summary>
internal static class BusinessManagedLockResources
{
    internal const int SqlServerMaximumResourceLength = 255;

    internal static string ExpenseRequest(Guid expenseRequestId)
    {
        EnsureId(expenseRequestId, "Expense request lock");
        return $"bms-web:budget-request:{expenseRequestId:N}";
    }

    internal static string Budget(BudgetKey key)
    {
        if (key.CostCenterId == Guid.Empty
            || key.CashFlowItemId == Guid.Empty
            || key.BudgetMonth == default)
        {
            throw new ArgumentException(
                "Budget lock requires a complete budget key.",
                nameof(key));
        }

        return $"bms-web:budget:{key.CostCenterId:N}:{key.CashFlowItemId:N}:{key.BudgetMonth:yyyyMMdd}";
    }

    internal static string TreasuryEvent(Guid eventId)
    {
        EnsureId(eventId, "Treasury event lock");
        return $"bms-web:treasury:event:{eventId:N}";
    }

    internal static string TreasuryCommand(Guid commandId)
    {
        EnsureId(commandId, "Treasury command lock");
        return $"bms-web:treasury:command:{commandId:N}";
    }

    internal static string TreasuryManualCorrection(Guid correctionId)
    {
        EnsureId(correctionId, "Treasury manual correction lock");
        return $"bms-web:treasury:correction:{correctionId:N}";
    }

    internal static string TreasuryPaymentIdentity(
        string sourceSystem,
        string externalId)
    {
        EnsureStructuralIdentifier(
            sourceSystem,
            ConfirmedPaymentEventContract.SourceSystemMaximumLength,
            "Payment source system lock");
        EnsureStructuralIdentifier(
            externalId,
            ConfirmedPaymentEventContract.ExternalIdMaximumLength,
            "Payment external id lock");

        var hash = HashLengthPrefixed(sourceSystem, externalId);
        return "bms-web:treasury:payment-identity:" + hash;
    }

    internal static string TreasuryDeficitSettlementSource(
        string kind,
        string sourceReference)
    {
        EnsureStructuralIdentifier(kind, 40, "Deficit settlement source kind lock");
        EnsureStructuralIdentifier(
            sourceReference,
            150,
            "Deficit settlement source reference lock");
        return "bms-web:treasury:source-reference:" +
            HashLengthPrefixed(kind, sourceReference);
    }

    internal static string TreasuryDeficit(Guid deficitId)
    {
        EnsureId(deficitId, "Treasury deficit lock");
        return $"bms-web:treasury:target-deficit:{deficitId:N}";
    }

    internal static string TreasuryScope(
        Guid organizationId,
        Guid counterpartyId,
        Guid currencyId)
    {
        EnsureId(organizationId, "Treasury organization scope lock");
        EnsureId(counterpartyId, "Treasury counterparty scope lock");
        EnsureId(currencyId, "Treasury currency scope lock");
        return $"bms-web:treasury:scope:{organizationId:N}:{counterpartyId:N}:{currencyId:N}";
    }

    internal static string TreasurySource(Guid sourceId)
    {
        EnsureId(sourceId, "Treasury source lock");
        return $"bms-web:treasury:source:{sourceId:N}";
    }

    internal static IReadOnlyList<string> Order(IEnumerable<string> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var ordered = resources
            .Select(ValidateResource)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException(
                "At least one managed lock resource is required.",
                nameof(resources));
        }

        return Array.AsReadOnly(ordered);
    }

    private static string ValidateResource(string resource)
    {
        if (string.IsNullOrWhiteSpace(resource)
            || resource.Length > SqlServerMaximumResourceLength
            || resource.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Managed lock resource must be nonblank, bounded and free of control characters.",
                nameof(resource));
        }

        return resource;
    }

    private static void EnsureId(Guid value, string fieldName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                $"{fieldName} requires a nonempty id.",
                nameof(value));
        }
    }

    private static void EnsureStructuralIdentifier(
        string value,
        int maximumLength,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Length > maximumLength
            || value.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"{fieldName} must be exact, bounded and free of control characters.",
                nameof(value));
        }
    }

    private static string HashLengthPrefixed(params string[] values)
    {
        var canonical = string.Concat(values.Select(value => string.Concat(
            value.Length.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
            value)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
