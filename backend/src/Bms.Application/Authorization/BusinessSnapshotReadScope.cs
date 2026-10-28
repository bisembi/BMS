using System.Text.Json;

namespace Bms.Application.Authorization;

/// <summary>Historical financial snapshots must not inherit a card's new CFO.</summary>
public static class BusinessSnapshotReadScope
{
    public static bool CanRead(BusinessDataReadScope scope, string? json)
    {
        if (scope.CanReadAll || string.IsNullOrWhiteSpace(json)) return true;
        if (json.Length > 1_000_000) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var snapshot = document.RootElement;
            if (snapshot.ValueKind != JsonValueKind.Object) return false;
            if (snapshot.TryGetProperty("expenseRequest", out var nested)) snapshot = nested;
            if (snapshot.ValueKind == JsonValueKind.Object && snapshot.TryGetProperty("id", out var requestId)
                && requestId.TryGetGuid(out var payrollId) && scope.PayrollRequestIds.Contains(payrollId)
                && snapshot.TryGetProperty("paymentOperation", out var operation) && operation.ValueKind == JsonValueKind.Object
                && operation.TryGetProperty("kind", out var kind) && kind.GetString() == "PayrollAdvance") return true;
            if (snapshot.ValueKind != JsonValueKind.Object
                || !snapshot.TryGetProperty("defaultCostCenterId", out var header)
                || !header.TryGetGuid(out var cfo) || !scope.Contains(cfo)
                || !snapshot.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var line in lines.EnumerateArray())
            {
                if (line.ValueKind != JsonValueKind.Object) return false;
                if (line.TryGetProperty("costCenterId", out var id) && id.ValueKind != JsonValueKind.Null
                    && (!id.TryGetGuid(out var lineCfo) || !scope.Contains(lineCfo))) return false;
            }
            return true;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
