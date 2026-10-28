using System.Data;
using System.Globalization;
using Bms.Application.Abstractions;
using Bms.Application.ExpenseRequests;
using Microsoft.Data.SqlClient;

namespace Bms.Infrastructure.Persistence;

internal sealed record PayrollCashLeg455(string Key,string FinancialHash,int LineNo,string PaymentTime,decimal PaymentAmountKzt,
    string? Debit,string? Credit,decimal? AmountKzt,string? EntryTime);

internal static class PayrollCashEvidence455
{
    internal static bool Verified(AccountingSourcePayment doc,IReadOnlyList<AccountingSourceCashLeg> expected,IReadOnlyList<PayrollCashLeg455> evidence)
    {
        if(!doc.Eligible||doc.RequestNumber is not null||doc.CurrencyNumericCode!="398"||doc.Amount<=0||doc.Amount!=doc.AmountKzt
            ||expected.Count==0||expected.Count!=doc.LegCount||evidence.Count!=doc.LegCount
            ||expected.Select(l=>l.LineNo).Distinct().Count()!=expected.Count||evidence.Select(l=>l.LineNo).Distinct().Count()!=evidence.Count)return false;
        var byLine=evidence.ToDictionary(e=>e.LineNo);
        foreach(var leg in expected)
        {
            if(!byLine.TryGetValue(leg.LineNo,out var e)||e.Key!=doc.DocumentKey||e.FinancialHash!=doc.FinancialHash
                ||e.Debit!="3350"||e.Credit is null||!new[]{"1010","1021","1030","1040","1050"}.Any(p=>e.Credit.StartsWith(p,StringComparison.Ordinal))
                ||e.PaymentAmountKzt!=leg.AmountKzt||e.AmountKzt!=leg.AmountKzt||leg.AmountKzt<=0
                ||AccountingPaymentSourceReader.NormalizeTime(e.PaymentTime)!=leg.EntryDateLocal||e.EntryTime is null
                ||AccountingPaymentSourceReader.NormalizeTime(e.EntryTime)!=leg.EntryDateLocal
                ||!leg.EntryDateLocal.StartsWith(doc.PaymentDay.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),StringComparison.Ordinal))return false;
        }
        return expected.Sum(l=>l.AmountKzt)==doc.AmountKzt;
    }
    internal static async Task<(Guid SnapshotId,AccountingSourcePayment[] Documents)> ReadAsync(SqlConnection connection,Guid capture,
        IReadOnlyList<AccountingSourcePayment> documents,IReadOnlyList<AccountingSourceCashLeg> legs,CancellationToken token)
    {
        await using var q=connection.CreateCommand();q.CommandType=CommandType.StoredProcedure;q.CommandTimeout=45;
        q.CommandText="dbo.ReadPayrollCashEvidence455";q.Parameters.Add("@CaptureId",SqlDbType.UniqueIdentifier).Value=capture;
        await using var r=await q.ExecuteReaderAsync(token);
        if(!await r.ReadAsync(token)||r.GetGuid(0)!=capture||r.GetGuid(1)==Guid.Empty)throw new ExpenseRequestPaymentSummaryUnavailableException();
        var snapshot=r.GetGuid(1);if(await r.ReadAsync(token)||!await r.NextResultAsync(token))throw new ExpenseRequestPaymentSummaryUnavailableException();
        var rows=new List<PayrollCashLeg455>();
        while(await r.ReadAsync(token))
        {
            if(rows.Count>=200000)throw new ExpenseRequestPaymentSummaryUnavailableException();
            rows.Add(new(r.GetString(0),r.GetString(1),r.GetInt32(2),r.GetString(3),r.GetDecimal(4),r.IsDBNull(5)?null:r.GetString(5),
                r.IsDBNull(6)?null:r.GetString(6),r.IsDBNull(7)?null:r.GetDecimal(7),r.IsDBNull(8)?null:r.GetString(8)));
        }
        if(await r.NextResultAsync(token))throw new ExpenseRequestPaymentSummaryUnavailableException();
        var evidence=rows.GroupBy(e=>e.Key).ToDictionary(g=>g.Key,g=>(IReadOnlyList<PayrollCashLeg455>)g.ToArray(),StringComparer.Ordinal);
        var expected=legs.GroupBy(l=>l.DocumentKey).ToDictionary(g=>g.Key,g=>(IReadOnlyList<AccountingSourceCashLeg>)g.ToArray(),StringComparer.Ordinal);
        return(snapshot,documents.Select(d=>d with{PayrollAdvanceEligible=Verified(d,expected.GetValueOrDefault(d.DocumentKey)??[],evidence.GetValueOrDefault(d.DocumentKey)??[])}).ToArray());
    }
}
