using System.Text.Json;
using Bms.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed partial class ExpenseRequestHistoryRepository
{
    // Only IDs already present in this visible page of immutable snapshots are
    // looked up. Include inactive records: revocation must not erase history.
    // No raw audit JSON or technical IDs become part of the public response.
    private async Task<ExpenseRequestHistoryReferences> ResolveReferencesAsync(
        IReadOnlyList<ExpenseRequestAuditSnapshotRow> rows, CancellationToken cancellationToken)
    {
        var ids = CollectReferenceIds(rows);
        var organizations = await dbContext.Organizations.AsNoTracking()
            .Where(item => ids.Organizations.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var centers = await dbContext.CostCenters.AsNoTracking()
            .Where(item => ids.CostCenters.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var counterparties = await dbContext.Counterparties.AsNoTracking()
            .Where(item => ids.Counterparties.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var contracts = await dbContext.Contracts.AsNoTracking()
            .Where(item => ids.Contracts.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var articles = await dbContext.CashFlowItems.AsNoTracking()
            .Where(item => ids.CashFlowItems.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        return new(organizations, centers, counterparties, contracts, articles);
    }

    internal sealed class ReferenceIds
    {
        public HashSet<Guid> Organizations { get; } = [];
        public HashSet<Guid> CostCenters { get; } = [];
        public HashSet<Guid> Counterparties { get; } = [];
        public HashSet<Guid> Contracts { get; } = [];
        public HashSet<Guid> CashFlowItems { get; } = [];
    }

    internal static ReferenceIds CollectReferenceIds(IReadOnlyList<ExpenseRequestAuditSnapshotRow> rows)
    {
        var result = new ReferenceIds();
        foreach (var row in rows)
        {
            Collect(row.BeforeJson);
            Collect(row.AfterJson);
        }
        return result;

        void Collect(string? json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 1_000_000) return;
            try
            {
                using var document = JsonDocument.Parse(json);
                var snapshot = document.RootElement;
                if (snapshot.ValueKind != JsonValueKind.Object) return;
                if (TryProperty(snapshot, "expenseRequest", out var nested)) snapshot = nested;
                if (snapshot.ValueKind != JsonValueKind.Object) return;
                Read(snapshot, "organizationId", result.Organizations);
                Read(snapshot, "defaultCostCenterId", result.CostCenters);
                Read(snapshot, "counterpartyId", result.Counterparties);
                Read(snapshot, "contractId", result.Contracts);
                if (!TryProperty(snapshot, "lines", out var lines) || lines.ValueKind != JsonValueKind.Array) return;
                foreach (var line in lines.EnumerateArray())
                {
                    if (line.ValueKind != JsonValueKind.Object) continue;
                    Read(line, "cashFlowItemId", result.CashFlowItems);
                    Read(line, "costCenterId", result.CostCenters);
                    Read(line, "accrualCostCenterId", result.CostCenters);
                }
            }
            catch (JsonException) { /* Damaged legacy event retains the safe unavailable projection. */ }
        }

        static void Read(JsonElement snapshot, string property, HashSet<Guid> target)
        {
            if (TryProperty(snapshot, property, out var value) && value.ValueKind == JsonValueKind.String
                && value.TryGetGuid(out var id) && id != Guid.Empty) target.Add(id);
        }
    }

    // Legacy Web snapshots may use PascalCase. Ambiguous duplicate names are
    // not a trustworthy identity lookup and retain the explicit missing label.
    private static bool TryProperty(JsonElement source, string name, out JsonElement value)
    {
        value = default;
        var found = false;
        foreach (var property in source.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (found) return false;
            value = property.Value;
            found = true;
        }
        return found;
    }
}
