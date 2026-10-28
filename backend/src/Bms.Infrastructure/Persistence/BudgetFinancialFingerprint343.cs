using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bms.Domain.ExpenseRequests;

namespace Bms.Infrastructure.Persistence;

internal static class BudgetFinancialFingerprint343
{
    internal static string? FromRequest(ExpenseRequest request) => Create(request.OrganizationId,
        request.PaymentOperation?.IsPayrollAdvance == true ? request.PaymentOperation.BudgetCostCenterId : request.DefaultCostCenterId,
        request.PlannedPaymentDate,request.CurrencyCode,request.Lines.Select(l=>(l.CashFlowItemId,l.AmountKzt)));

    internal static (string Fingerprint,string Status)? FromApprovedAudit(string json)
    {
        if(json.Length>2_000_000)return null;
        try
        {
            using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=48});var root=doc.RootElement;
            var nested=Property(root,"expenseRequest");if(nested.ValueKind==JsonValueKind.Object)root=nested;
            var status=Text(root,"status");if(status is not("APPROVED" or "TO_PAY"))return null;
            Guid? Id(string field)=>Guid.TryParse(Text(root,field),out var id)?id:null;
            DateOnly? planned=DateOnly.TryParse(Text(root,"plannedPaymentDate"),CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date:null;
            var lines=Property(root,"lines");if(lines.ValueKind!=JsonValueKind.Array||lines.GetArrayLength()>10000)return null;
            var amounts=new List<(Guid?,decimal?)>();
            foreach(var line in lines.EnumerateArray())
            {
                var cashId=Guid.TryParse(Text(line,"cashFlowItemId"),out var id)?id:(Guid?)null;
                var amount=Property(line,"amountKzt");amounts.Add((cashId,amount.ValueKind==JsonValueKind.Number&&amount.TryGetDecimal(out var value)?value:null));
            }
            var operation=Property(root,"paymentOperation");
            var budgetCfo = Text(operation,"kind") == ExpensePaymentOperation.PayrollAdvanceKind
                && Guid.TryParse(Text(operation,"budgetCostCenterId"),out var hr) ? hr : Id("defaultCostCenterId");
            var fingerprint=Create(Id("organizationId"),budgetCfo,planned,Text(root,"currencyCode"),amounts);
            return fingerprint is null?null:(fingerprint,status);
        }
        catch(JsonException){return null;}
    }

    private static string? Create(Guid? organization,Guid? cfo,DateOnly? date,string? currency,IEnumerable<(Guid? Article,decimal? Amount)> input)
    {
        var lines=input.ToArray();
        if(organization is null||organization==Guid.Empty||cfo is null||cfo==Guid.Empty||date is null
            ||currency is not{Length:3}||lines.Length==0||lines.Any(l=>l.Article is null||l.Article==Guid.Empty||l.Amount is null||l.Amount<=0m||decimal.Round(l.Amount.Value,2)!=l.Amount))return null;
        var text=new StringBuilder("BMS-BudgetFinancial-343\n").Append(organization.Value.ToString("N")).Append('\n')
            .Append(cfo.Value.ToString("N")).Append('\n').Append(date.Value.ToString("yyyy-MM",CultureInfo.InvariantCulture)).Append('\n').Append(currency).Append('\n');
        foreach(var row in lines.GroupBy(l=>l.Article??throw new InvalidOperationException("Guarded financial article missing.")).OrderBy(g=>g.Key.ToString("N"),StringComparer.Ordinal))
            text.Append(row.Key.ToString("N")).Append('|').Append(row.Sum(l=>l.Amount??throw new InvalidOperationException("Guarded amount missing.")).ToString("0.00",CultureInfo.InvariantCulture)).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static JsonElement Property(JsonElement element,string name)
    {
        if(element.ValueKind!=JsonValueKind.Object)return default;
        if(element.TryGetProperty(name,out var value))return value;
        return element.TryGetProperty(char.ToUpperInvariant(name[0])+name[1..],out value)?value:default;
    }
    private static string? Text(JsonElement element,string name)
    {
        var value=Property(element,name);return value.ValueKind==JsonValueKind.String?value.GetString():null;
    }
}
