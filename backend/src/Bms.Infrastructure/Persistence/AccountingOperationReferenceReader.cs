using System.Text.Json;
using System.Text.RegularExpressions;
using Bms.Application.Abstractions;
using Bms.Application.ExpenseRequests;
using Microsoft.Data.SqlClient;

namespace Bms.Infrastructure.Persistence;

internal static class AccountingOperationReferenceReader
{
    internal static AccountingSourcePayment[] Apply(string controls, IReadOnlyList<AccountingSourcePayment> documents)
    {
        if (controls.Length > 20000000) throw new ExpenseRequestPaymentSummaryUnavailableException();
        using var json = JsonDocument.Parse(controls);
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() != 3)
            throw new ExpenseRequestPaymentSummaryUnavailableException();
        var result = documents.ToDictionary(d => d.DocumentKey, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var control in json.RootElement.EnumerateArray())
        {
            if (!control.TryGetProperty("requestReferences", out var references)) continue; // historic captures
            var database = control.GetProperty("database").GetString();
            if (database is not ("ExampleAccountingA" or "ExampleAccountingB" or "ExampleAccountingC")
                || references.ValueKind != JsonValueKind.Array || references.GetArrayLength() > 100000)
                throw new ExpenseRequestPaymentSummaryUnavailableException();
            foreach (var row in references.EnumerateArray())
            {
                var key = row.GetProperty("documentKey").GetString() ?? "";
                var number = row.GetProperty("requestNumber").GetString() ?? "";
                var verified = row.GetProperty("verified").GetBoolean();
                if (!seen.Add(key) || !key.StartsWith(database + "|", StringComparison.Ordinal)
                    || !result.TryGetValue(key, out var doc)
                    || !Regex.IsMatch(number, "^[A-ZА-ЯЁ0-9][A-ZА-ЯЁ0-9-]{0,39}-[0-9]{5,10}$", RegexOptions.CultureInvariant)
                    || verified && (doc.Amount <= 0 || doc.AmountKzt <= 0 || doc.LegCount < 1
                        || string.IsNullOrWhiteSpace(doc.OrganizationName) || !Regex.IsMatch(doc.CurrencyNumericCode, "^[0-9]{3}$")))
                    throw new ExpenseRequestPaymentSummaryUnavailableException();
                result[key] = doc with { RequestNumber = number, OperationReferenceEligible = verified };
            }
        }
        return documents.Select(d => result[d.DocumentKey]).ToArray();
    }
    internal static async Task<AccountingSourcePayment[]> ReadAsync(SqlConnection connection, Guid captureId,
        IReadOnlyList<AccountingSourcePayment> documents, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.ReadAccountingOperationReferences317";
        command.CommandType = System.Data.CommandType.StoredProcedure;
        command.CommandTimeout = 30;
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token) || reader.GetGuid(0) != captureId)
            throw new ExpenseRequestPaymentSummaryUnavailableException();
        var controls = reader.GetString(1);
        if (await reader.ReadAsync(token)) throw new ExpenseRequestPaymentSummaryUnavailableException();
        return Apply(controls, documents);
    }
}
