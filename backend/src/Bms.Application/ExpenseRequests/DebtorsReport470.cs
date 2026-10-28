using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Common;

namespace Bms.Application.ExpenseRequests;

public sealed record DebtorsQuery470(Guid? OrganizationId=null,Guid? CostCenterId=null,Guid? CounterpartyId=null,
    DateOnly? PlannedPaymentDate=null,bool? IsUrgent=null)
{
    public void Validate()
    {
        if(OrganizationId==Guid.Empty||CostCenterId==Guid.Empty||CounterpartyId==Guid.Empty)
            throw new ArgumentException("Empty debtor report filter.");
    }
}
public sealed record DebtorRequest470(Guid Id,string InitiatorExternalId,string CurrencyCode,decimal Amount,decimal? AmountKzt);
public interface IDebtorsReader470
{
    Task<IReadOnlyList<DebtorRequest470>> ReadAsync(string actor,DebtorsQuery470 query,CancellationToken token);
}
public sealed record DebtorPerson470(string Key,string Name,int RequestCount,string KnownAmountKzt,int UnknownAmountCount)
{
    public string? AmountKzt=>UnknownAmountCount==0?KnownAmountKzt:null;
}
public sealed record DebtorsReport470(DateTimeOffset GeneratedAtUtc,IReadOnlyList<DebtorPerson470> Items,
    int RequestCount,string KnownAmountKzt,int UnknownAmountCount)
{
    public string? AmountKzt=>UnknownAmountCount==0?KnownAmountKzt:null;
}
public sealed class DebtorsReportUnavailable470:Exception
{
    public DebtorsReportUnavailable470(Exception? inner=null):base("Debtor report is unavailable.",inner){}
}

/// Read-only current PAID request amounts, not historical debt or an accrual
/// balance. Aggregate the entire authorized filter in one Serializable attempt.
public sealed class DebtorsService470(IDebtorsReader470 reader,IEmployeeActorDisplayNamesReader names,
    ICurrentActor actor,IBusinessTransactionRunner transactions,IClock clock)
{
    public async Task<DebtorsReport470> ReadAsync(DebtorsQuery470 query,CancellationToken token)
    {
        var expected=ActorIdentity.RequireBusinessDataAccess(actor);query.Validate();token.ThrowIfCancellationRequested();
        try
        {
            return await transactions.ExecuteAsync(async t=>
            {
                if(ActorIdentity.RequireBusinessDataAccess(actor)!=expected)throw new ActorForbiddenException();
                var rows=await reader.ReadAsync(expected,query,t);
                if(rows.Count>20000)throw new DebtorsReportUnavailable470();
                var mappings=new List<EmployeeActorMappingProjectionRow>();
                foreach(var batch in rows.Select(r=>r.InitiatorExternalId).Where(ActorIdentity.IsCanonical)
                    .Distinct(StringComparer.Ordinal).Chunk(100))
                {
                    t.ThrowIfCancellationRequested();
                    try{mappings.AddRange(await names.ReadAsync(batch,t));}
                    catch(OperationCanceledException) when(t.IsCancellationRequested){throw;}
                    catch(Exception e) when(!ProcessFatalExceptionClassifier.IsFatal(e))
                    { /* Name outage does not merge unidentified people or expose SIDs. */ }
                }
                return Compose(rows,mappings,clock.UtcNow,t);
            },token);
        }
        catch(Exception e) when(!ProcessFatalExceptionClassifier.IsFatal(e)
            &&e is not(OperationCanceledException or ActorNotAuthenticatedException or ActorForbiddenException or DebtorsReportUnavailable470))
        {throw new DebtorsReportUnavailable470(e);}
    }
    public static DebtorsReport470 Compose(IReadOnlyList<DebtorRequest470> rows,
        IReadOnlyList<EmployeeActorMappingProjectionRow> mappings,DateTimeOffset at,CancellationToken token=default)
    {
        if(rows.Count>20000||at==default||at.Offset!=TimeSpan.Zero)throw new DebtorsReportUnavailable470();
        var ids=new HashSet<Guid>();var groups=new Dictionary<string,(string Name,int Count,decimal Known,int Unknown)>(StringComparer.Ordinal);
        var byActor=mappings.ToLookup(m=>m.ActorExternalId,StringComparer.Ordinal);
        foreach(var row in rows)
        {
            token.ThrowIfCancellationRequested();
            if(row.Id==Guid.Empty||!ids.Add(row.Id)||string.IsNullOrWhiteSpace(row.InitiatorExternalId)
                ||row.CurrencyCode.Length!=3||row.CurrencyCode.Any(c=>c is <'A' or >'Z'))throw new DebtorsReportUnavailable470();
            MoneyAmountPolicy.EnsureNonNegative(row.Amount,"Debtor obligation");
            if(row.AmountKzt is{} stored)MoneyAmountPolicy.EnsureNonNegative(stored,"Debtor KZT obligation");
            // Native KZT never requires a conversion rate. Foreign currency
            // uses its stored equivalent; missing rates remain unknown.
            var value=row.CurrencyCode=="KZT"?row.Amount:row.AmountKzt;
            if(row.CurrencyCode=="KZT"&&row.AmountKzt is{} kzt&&kzt!=row.Amount)throw new DebtorsReportUnavailable470();
            var identity="actor:"+row.InitiatorExternalId;var label="Имя недоступно";
            var matches=byActor[row.InitiatorExternalId].ToArray();
            if(ActorIdentity.IsCanonical(row.InitiatorExternalId)&&matches.Length==1)
            {
                var m=matches[0];var name=m.EmployeeName?.Trim();
                if(m.EmployeeId!=Guid.Empty&&m.MappingIsActive&&m.EmployeeIsActive&&!string.IsNullOrWhiteSpace(name)
                    &&name.Length<=256&&!name.Any(char.IsControl)&&!name.StartsWith("S-1-",StringComparison.OrdinalIgnoreCase))
                {identity="employee:"+m.EmployeeId.ToString("D");label=name;}
            }
            var g=groups.GetValueOrDefault(identity,(Name:label,Count:0,Known:0m,Unknown:0));
            if(g.Name!=label)throw new DebtorsReportUnavailable470();
            groups[identity]=(label,g.Count+1,checked(g.Known+(value??0)),g.Unknown+(value is null?1:0));
        }
        string Text(decimal v)=>v.ToString("0.00",CultureInfo.InvariantCulture);
        var items=groups.Select(g=>new DebtorPerson470(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(g.Key))),
            g.Value.Name,g.Value.Count,Text(g.Value.Known),g.Value.Unknown))
            .OrderBy(g=>g.Name,StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"),false)).ThenBy(g=>g.Key,StringComparer.Ordinal).ToArray();
        return new(at,items,rows.Count,Text(groups.Values.Sum(g=>g.Known)),groups.Values.Sum(g=>g.Unknown));
    }
}
